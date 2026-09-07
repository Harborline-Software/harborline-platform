using System;
using System.Linq;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.RentRoll;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 6 — unit tests for <see cref="RentRollCartridge"/>.
/// Seeds the shared <see cref="InMemoryReportQuerySource"/> so the full
/// neutral read-side paths execute without mocking.
///
/// <para>
/// Substrate deviations matched in assertions:
/// D1 — Property grouping by UnitId.Authority.
/// D2 — Current lease derived from ListAsync + phase/date filtering.
/// D3 — AR aging joined by CustomerId (primary tenant).
/// D4 — PrepaidBalance always 0; LastPaymentDate always null.
/// D5 — VacancyReason from LeasePhase approximation.
/// </para>
/// </summary>
public sealed class RentRollCartridgeTests
{
    // ──────────────────────────────────────────────────────────────────
    //  Shared constants
    // ──────────────────────────────────────────────────────────────────

    private static readonly TenantId Tenant = new("tenant-rent-roll");
    private static readonly TenantId OtherTenant = new("tenant-other");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    // Reference date per env
    private static readonly DateOnly Today = new(2026, 5, 17);

    private static int _partySeq;
    private static int _invoiceSeq;

    // ──────────────────────────────────────────────────────────────────
    //  Factory helpers
    // ──────────────────────────────────────────────────────────────────

    private static (RentRollCartridge Cartridge,
                    InMemoryReportQuerySource Leases,
                    InMemoryReportQuerySource Invoices,
                    InMemoryReportQuerySource Parties)
        Build()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "Rent roll", "USD", true));
        return (new RentRollCartridge(source), source, source, source);
    }

    private static ReportExecutionContext Context(DateOnly? asOf = null)
    {
        var d   = asOf ?? Today;
        var utc = new DateTimeOffset(d.Year, d.Month, d.Day, 12, 0, 0, TimeSpan.Zero);
        return new ReportExecutionContext(Tenant, "marker:rr:1", utc, Principal);
    }

    private sealed record UnitKey(string PropertyKey, string UnitLabel);

    private static UnitKey UnitId(string propertyAuthority, string unitLocal)
        => new(propertyAuthority, unitLocal);

    private static PartyId NewPartyId()
        => new($"party-{System.Threading.Interlocked.Increment(ref _partySeq)}");

    private static RentRollParameters DefaultParams(DateOnly? asOf = null) => new()
    {
        ChartId  = Chart,
        AsOfDate = asOf ?? Today,
    };

    /// <summary>Create a neutral lease row in the shared query source.</summary>
    private static Task<ReportLease> MakeLeaseAsync(
        InMemoryReportQuerySource svc,
        UnitKey unitId,
        PartyId tenant,
        DateOnly start,
        DateOnly end,
        decimal rent,
        LeasePhase targetPhase = LeasePhase.Active,
        TenantId? tenantOverride = null)
    {
        var lease = new ReportLease(
            LeaseId.NewId(), tenantOverride ?? Tenant, unitId.PropertyKey, unitId.UnitLabel,
            new[] { tenant }, start, end, targetPhase, rent);
        svc.Leases.Add(lease);
        return Task.FromResult(lease);
    }

    private static ReportOpenItem MakeIssuedInvoice(
        PartyId customerId,
        DateOnly dueDate,
        decimal amount)
    {
        var seq = System.Threading.Interlocked.Increment(ref _invoiceSeq);
        var daysOverdue = Today.DayNumber - dueDate.DayNumber;
        var bucket = daysOverdue switch
        {
            <= 0 => AgingBucket.Current,
            <= 30 => AgingBucket.Days0To30,
            <= 60 => AgingBucket.Days31To60,
            <= 90 => AgingBucket.Days61To90,
            _ => AgingBucket.Days90Plus,
        };
        return new ReportOpenItem($"INV-2026-05-17-RR-{seq:D4}", Tenant, Chart, customerId, null, dueDate, amount, bucket);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Edge cases — empty
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_NoProperties_ReturnsEmptyBlocksAndZeroPortfolio()
    {
        var (sut, _, _, _) = Build();
        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        Assert.Empty(result.Properties);
        Assert.Equal(0, result.Portfolio.PropertiesCovered);
        Assert.Equal(0, result.Portfolio.TotalUnits);
        Assert.Equal(0, result.Portfolio.OccupiedUnits);
        Assert.Equal(0m, result.Portfolio.OccupancyRate);
        Assert.Equal(0m, result.Portfolio.MonthlyRentTotal);
        Assert.Equal(0m, result.Portfolio.OpenBalanceTotal);
    }

    [Fact]
    public async Task RentRoll_OnePropertyNoUnits_WhenAllLeasesExpired_ReturnVacantRows()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "unit-1");

        // A lease that ended before today → not active on asOf, so vacant row.
        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 1000m,
            LeasePhase.Terminated);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        // One block, one vacant row.
        Assert.Single(result.Properties);
        var block = result.Properties[0];
        Assert.Equal("prop-a", block.PropertyKey);
        Assert.Single(block.Units);
        Assert.Equal(OccupancyStatus.Vacant, block.Units[0].Status);
        Assert.Equal(VacancyReason.EndOfTerm, block.Units[0].VacancyReason);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Edge cases — single record
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_OneOccupiedUnit_ShowsTenantAndRent()
    {
        var (sut, leases, _, parties) = Build();
        var unit = UnitId("prop-a", "unit-1");

        // Seed party first; use the returned id for the lease (so the cartridge can resolve the name).
        var seededParty = new ReportParty(NewPartyId(), "Alice Smith");
        parties.Parties.Add(seededParty);

        await MakeLeaseAsync(leases, unit, seededParty.Id,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1500m);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(OccupancyStatus.Occupied, row.Status);
        Assert.Equal(1500m, row.MonthlyRent);
        Assert.Equal(1500m, row.ProjectedNextMonthRent); // D4: same as monthly rent
        Assert.Equal("Alice Smith", row.TenantName);
        Assert.Equal(seededParty.Id, row.TenantId);
        Assert.Null(row.LastPaymentDate);                // D4: stub
        Assert.Equal(0m, row.PrepaidBalance);            // D4: stub
        Assert.Null(row.VacancyReason);
    }

    [Fact]
    public async Task RentRoll_OneVacantUnit_IncludeVacantTrue_ShownAsVacant()
    {
        var (sut, leases, _, _) = Build();
        var tenantParty = NewPartyId();
        var unit        = UnitId("prop-a", "unit-1");

        // Past lease — terminated.
        await MakeLeaseAsync(leases, unit, tenantParty,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), 800m,
            LeasePhase.Terminated);

        var p = DefaultParams() with { IncludeVacant = true };
        var result = await sut.ExecuteAsync(Context(), p);

        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(OccupancyStatus.Vacant, row.Status);
        Assert.Equal(0m, row.MonthlyRent);
    }

    [Fact]
    public async Task RentRoll_OneVacantUnit_IncludeVacantFalse_Omitted()
    {
        var (sut, leases, _, _) = Build();
        var tenantParty = NewPartyId();
        var unit        = UnitId("prop-a", "unit-1");

        await MakeLeaseAsync(leases, unit, tenantParty,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), 800m,
            LeasePhase.Terminated);

        var p = DefaultParams() with { IncludeVacant = false };
        var result = await sut.ExecuteAsync(Context(), p);

        // No occupied rows → no property block (D1: groups by UnitId.Authority)
        // or block with empty unit list.
        var allRows = result.Properties.SelectMany(b => b.Units).ToList();
        Assert.Empty(allRows);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Property filter
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_PropertyIdsFilter_OmitsOtherProperties()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();

        await MakeLeaseAsync(leases, UnitId("prop-a", "u1"), t1,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);
        await MakeLeaseAsync(leases, UnitId("prop-b", "u1"), t2,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2000m);

        var p = DefaultParams() with { PropertyAuthorityKeys = new[] { "prop-a" } };
        var result = await sut.ExecuteAsync(Context(), p);

        Assert.Single(result.Properties);
        Assert.Equal("prop-a", result.Properties[0].PropertyKey);
    }

    // ──────────────────────────────────────────────────────────────────
    //  As-of date
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_AsOfDateInPast_ShowsLeasesActiveOnThatDate()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        // Lease active in 2025, expired before today.
        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 900m);

        var asOf = new DateOnly(2025, 6, 15);
        var p = DefaultParams(asOf);
        var result = await sut.ExecuteAsync(Context(asOf), p);

        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(OccupancyStatus.Occupied, row.Status);
        Assert.Equal(900m, row.MonthlyRent);
    }

    [Fact]
    public async Task RentRoll_AsOfDateBeforeAnyLease_ShowsAllVacant()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);

        // asOf is before the lease starts.
        var asOf = new DateOnly(2025, 12, 31);
        var p = DefaultParams(asOf) with { IncludeVacant = true };
        var result = await sut.ExecuteAsync(Context(asOf), p);

        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(OccupancyStatus.Vacant, row.Status);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Expiring window
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_LeaseEndingWithinWindow_ExpiringSoonTrue()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        // Lease ends 30 days from today; window is default 90.
        var end = Today.AddDays(30);
        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2026, 1, 1), end, 1000m);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());
        Assert.True(result.Properties[0].Units[0].ExpiringSoon);
        Assert.Equal(OccupancyStatus.NoticeGiven, result.Properties[0].Units[0].Status);
    }

    [Fact]
    public async Task RentRoll_LeaseEndingOutsideWindow_ExpiringSoonFalse()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        // Lease ends 200 days from today; window is 90.
        var end = Today.AddDays(200);
        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2026, 1, 1), end, 1000m);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());
        Assert.False(result.Properties[0].Units[0].ExpiringSoon);
        Assert.Equal(OccupancyStatus.Occupied, result.Properties[0].Units[0].Status);
    }

    [Fact]
    public async Task RentRoll_ExpiringWindowZero_OnlyLeasesEndingTodayAreExpiringSoon()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();

        // Lease ending today.
        await MakeLeaseAsync(leases, UnitId("prop-a", "u1"), t1,
            new DateOnly(2026, 1, 1), Today, 1000m);
        // Lease ending tomorrow.
        await MakeLeaseAsync(leases, UnitId("prop-a", "u2"), t2,
            new DateOnly(2026, 1, 1), Today.AddDays(1), 1000m);

        var p = DefaultParams() with { ExpiringWindowDays = 0 };
        var result = await sut.ExecuteAsync(Context(), p);

        var rows = result.Properties[0].Units.OrderBy(r => r.UnitLabel).ToList();
        Assert.True(rows[0].ExpiringSoon);   // u1 ends today
        Assert.False(rows[1].ExpiringSoon);  // u2 ends tomorrow
    }

    [Fact]
    public async Task RentRoll_ExpiringWindowNegative_ThrowsValidationException()
    {
        var (sut, _, _, _) = Build();
        var p = DefaultParams() with { ExpiringWindowDays = -1 };
        await Assert.ThrowsAsync<ReportParameterValidationException>(
            () => sut.ExecuteAsync(Context(), p));
    }
    [Fact]
    public async Task RentRoll_UnknownChart_ThrowsValidationException()
    {
        var (sut, _, source, _) = Build();
        source.Charts.Clear();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(), DefaultParams()));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Aging delegation (D3)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_TenantWithOpenInvoice_DelinquencyBucketReflectsAging()
    {
        var (sut, leases, invoices, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1200m);

        // Invoice 40 days overdue → Days0To30 bucket is past; 40 days → Days31To60.
        var dueDate = Today.AddDays(-40);
        invoices.Receivables.Add(MakeIssuedInvoice(tenant, dueDate, 1200m));

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(ArAgingBucket.Days31To60, row.DelinquencyBucket);
        Assert.Equal(1200m, row.OpenBalance);
    }

    [Fact]
    public async Task RentRoll_TenantWithNoOpenInvoice_DelinquencyBucketIsNoBalance()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1200m);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(ArAgingBucket.NoBalance, row.DelinquencyBucket);
        Assert.Equal(0m, row.OpenBalance);
    }
    [Fact]
    public async Task RentRoll_ForwardsResolvedAsOfToReceivables()
    {
        var (sut, _, source, _) = Build();
        await sut.ExecuteAsync(Context(), DefaultParams());
        Assert.Equal([Today], source.ObservedReceivableAsOfs);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Vacancy reason (D5)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_UnitNeverLeased_VacancyReasonIsNeverLeased()
    {
        // NeverLeased only appears when there's a unit with no lease history.
        // Since we discover units from leases, this scenario is only possible when
        // all leases are in non-active phases but the unit still appears.
        // Reproduce by creating a Draft lease (never active) which we cannot make
        // active on asOf.
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        // Create a lease in Draft only (no transition) — not active on any date.
        await MakeLeaseAsync(leases, unit, tenant,
            Today.AddDays(-10), Today.AddDays(355), 1000m, LeasePhase.Draft);

        // With IncludeVacant = true, the unit appears as vacant.
        var p = DefaultParams() with { IncludeVacant = true };
        var result = await sut.ExecuteAsync(Context(), p);

        // Draft lease → found in allLeases but not "current" → vacant, NeverLeased.
        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(OccupancyStatus.Vacant, row.Status);
        // Draft is not Terminated or Cancelled → NeverLeased.
        Assert.Equal(VacancyReason.NeverLeased, row.VacancyReason);
    }

    [Fact]
    public async Task RentRoll_UnitWithEndedLease_VacancyReasonIsEndOfTerm()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        await MakeLeaseAsync(leases, unit, tenant,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 900m,
            LeasePhase.Terminated);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());
        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(VacancyReason.EndOfTerm, row.VacancyReason);
    }

    [Fact]
    public async Task RentRoll_UnitWithCancelledLease_VacancyReasonIsTurnover()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();
        var unit   = UnitId("prop-a", "u1");

        // Cancelled from AwaitingSignature (which is allowed).
        await MakeLeaseAsync(leases, unit, tenant,
            Today.AddDays(-10), Today.AddDays(355), 700m, LeasePhase.Cancelled);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());
        var row = Assert.Single(result.Properties[0].Units);
        Assert.Equal(VacancyReason.Turnover, row.VacancyReason);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Property summary
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_PropertySummary_OccupancyRateIsOccupiedOverTotal()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();

        await MakeLeaseAsync(leases, UnitId("prop-a", "u1"), t1,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);
        // Vacant unit (past lease).
        await MakeLeaseAsync(leases, UnitId("prop-a", "u2"), t2,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 900m,
            LeasePhase.Terminated);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());
        var summary = result.Properties[0].Summary;

        Assert.Equal(2, summary.TotalUnits);
        Assert.Equal(1, summary.OccupiedUnits);
        Assert.Equal(0.5m, summary.OccupancyRate);
        Assert.Equal(1000m, summary.MonthlyRentTotal);
    }

    [Fact]
    public async Task RentRoll_PropertySummary_OccupancyRateAllVacant_IsZero()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();

        await MakeLeaseAsync(leases, UnitId("prop-a", "u1"), tenant,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 900m,
            LeasePhase.Terminated);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());
        Assert.Equal(0, result.Properties[0].Summary.OccupiedUnits);
        Assert.Equal(0m, result.Properties[0].Summary.OccupancyRate);
    }

    [Fact]
    public async Task RentRoll_PropertySummary_NoUnits_OccupancyRateIsZero()
    {
        // Empty portfolio → no properties, portfolio rate 0.
        var (sut, _, _, _) = Build();
        var result = await sut.ExecuteAsync(Context(), DefaultParams());
        Assert.Equal(0m, result.Portfolio.OccupancyRate);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Portfolio summary
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_PortfolioTotals_EqualSumAcrossProperties()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();

        await MakeLeaseAsync(leases, UnitId("prop-a", "u1"), t1,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);
        await MakeLeaseAsync(leases, UnitId("prop-b", "u1"), t2,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2000m);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        var expectedRent = result.Properties.Sum(b => b.Summary.MonthlyRentTotal);
        var expectedOpen = result.Properties.Sum(b => b.Summary.OpenBalanceTotal);

        Assert.Equal(expectedRent, result.Portfolio.MonthlyRentTotal);
        Assert.Equal(expectedOpen, result.Portfolio.OpenBalanceTotal);
        Assert.Equal(2, result.Portfolio.PropertiesCovered);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Ordering
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_Properties_OrderedByNameThenId()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();
        var t3 = NewPartyId();

        // Insert out of order.
        await MakeLeaseAsync(leases, UnitId("prop-c", "u1"), t3,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);
        await MakeLeaseAsync(leases, UnitId("prop-a", "u1"), t1,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);
        await MakeLeaseAsync(leases, UnitId("prop-b", "u1"), t2,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        var keys = result.Properties.Select(b => b.PropertyKey).ToList();
        Assert.Equal(new[] { "prop-a", "prop-b", "prop-c" }, keys);
    }

    [Fact]
    public async Task RentRoll_UnitsWithinProperty_OrderedByLabel()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();
        var t3 = NewPartyId();

        // Insert out of order within the same property.
        await MakeLeaseAsync(leases, UnitId("prop-a", "unit-c"), t3,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);
        await MakeLeaseAsync(leases, UnitId("prop-a", "unit-a"), t1,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);
        await MakeLeaseAsync(leases, UnitId("prop-a", "unit-b"), t2,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        var labels = result.Properties[0].Units.Select(r => r.UnitLabel).ToList();
        Assert.Equal(new[] { "unit-a", "unit-b", "unit-c" }, labels);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Tenant isolation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RentRoll_CrossTenantLeases_OnlyOwnTenantVisible()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();

        // Own tenant lease.
        await MakeLeaseAsync(leases, UnitId("prop-a", "u1"), t1,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m,
            tenantOverride: Tenant);
        // Other tenant lease — same unit, different tenant scope.
        await MakeLeaseAsync(leases, UnitId("prop-a", "u2"), t2,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2000m,
            tenantOverride: OtherTenant);

        var result = await sut.ExecuteAsync(Context(), DefaultParams());

        // Only one unit row (the own-tenant lease).
        Assert.Single(result.Properties);
        Assert.Single(result.Properties[0].Units);
    }
}
