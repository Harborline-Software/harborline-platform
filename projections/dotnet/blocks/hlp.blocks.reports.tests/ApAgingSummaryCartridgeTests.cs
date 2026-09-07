using System;
using System.Linq;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ApAgingSummary;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// Unit tests for <see cref="ApAgingSummaryCartridge"/>.
/// Each test seeds neutral payable and party rows through the shared query source.
/// Mirrors <see cref="ArAgingSummaryCartridgeTests"/> with vendor/bill semantics.
/// </summary>
public sealed class ApAgingSummaryCartridgeTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-ap-aging");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    // Reference date: 2026-05-17 (mirrors AR counterpart)
    private static readonly DateOnly Today = new(2026, 5, 17);
    private static int _billSeq;

    private static ReportExecutionContext Context(DateOnly? asOf = null)
    {
        var dt = asOf ?? Today;
        var utc = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 12, 0, 0, TimeSpan.Zero);
        return new ReportExecutionContext(Tenant, "marker:ap:1", utc, Principal);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (ApAgingSummaryCartridge Cartridge, InMemoryReportQuerySource Source) Build()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "AP", "USD", true));
        return (new ApAgingSummaryCartridge(source), source);
    }

    [Fact]
    public async Task ApAgingSummary_UnknownChart_ThrowsValidationException()
    {
        var (sut, source) = Build();
        source.Charts.Clear();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() => sut.ExecuteAsync(
            Context(), new ApAgingSummaryParameters { ChartId = Chart }));
    }

    private static PartyId NewPartyId() => new(Guid.NewGuid().ToString());

    private static ReportOpenItem MakeReceivedBill(
        PartyId vendorId,
        DateOnly dueDate,
        decimal amount,
        string? propertyId = null,
        TenantId? tenantId = null)
    {
        var seq = System.Threading.Interlocked.Increment(ref _billSeq);
        var billNumber = $"VND-2026-{seq:D4}";
        // Inert seed value: the fake classifies from DueDate and the requested asOf.
        return new ReportOpenItem(billNumber, tenantId ?? Tenant, Chart, vendorId,
            propertyId, dueDate, amount, AgingBucket.Current);
    }

    private static Task<PartyId> SeedPartyAsync(InMemoryReportQuerySource source, string name)
    {
        var partyId = NewPartyId();
        source.Parties.Add(new ReportParty(partyId, name));
        return Task.FromResult(partyId);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Edge case — empty
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_EmptyChart_ReturnsZeroRowsAndZeroTotals()
    {
        var (sut, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        Assert.Empty(result.ByVendor);
        Assert.Empty(result.ByProperty);
        Assert.Empty(result.TopOverdue);
        Assert.Equal(0m, result.Totals.Current);
        Assert.Equal(0m, result.Totals.Days0To30);
        Assert.Equal(0m, result.Totals.Days31To60);
        Assert.Equal(0m, result.Totals.Days61To90);
        Assert.Equal(0m, result.Totals.Days90Plus);
        Assert.Equal(0m, result.Totals.TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Edge case — single record
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_SingleBillCurrent_AppearsInCurrentBucket()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        // Due tomorrow — still current.
        var bill = MakeReceivedBill(vendor, Today.AddDays(1), 100m);
        source.Payables.Add(bill);

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        Assert.Single(result.ByVendor);
        Assert.Equal(100m, result.ByVendor[0].Current);
        Assert.Equal(0m, result.ByVendor[0].Days90Plus);
        Assert.Equal(100m, result.Totals.Current);
    }

    [Fact]
    public async Task ApAgingSummary_SingleBill90PlusDays_AppearsInTopOverdue()
    {
        var (sut, source) = Build();
        var vendorId = await SeedPartyAsync(source, "Acme Plumbing");

        // 100 days past due.
        var bill = MakeReceivedBill(vendorId, Today.AddDays(-100), 500m);
        source.Payables.Add(bill);

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart, TopOverdueN = 5 });

        Assert.Single(result.TopOverdue);
        Assert.Equal(vendorId, result.TopOverdue[0].VendorId);
        Assert.Equal("Acme Plumbing", result.TopOverdue[0].VendorName);
        Assert.Equal(500m, result.TopOverdue[0].Days90PlusBalance);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Vendor rollup
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_MultipleBillsSameVendor_AggregatedInOneRow()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 100m));
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(10), 200m));
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(-10), 50m));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        Assert.Single(result.ByVendor);
        Assert.Equal(300m, result.ByVendor[0].Current); // 100 + 200 current
        Assert.Equal(50m, result.ByVendor[0].Days0To30); // 10 days past due
        Assert.Equal(350m, result.ByVendor[0].TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Property rollup
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_MultipleBillsSameProperty_AggregatedInOneRow()
    {
        var (sut, source) = Build();
        var v1 = NewPartyId();
        var v2 = NewPartyId();
        source.Payables.Add(MakeReceivedBill(v1, Today.AddDays(5), 100m, "prop-A"));
        source.Payables.Add(MakeReceivedBill(v2, Today.AddDays(5), 200m, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        var propRow = result.ByProperty.Single(r => r.GroupKey == "prop-A");
        Assert.Equal(300m, propRow.Current);
    }

    [Fact]
    public async Task ApAgingSummary_BillWithNullPropertyId_RolledIntoUnassigned()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 75m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        var unassigned = result.ByProperty.Single(r => r.GroupKey == "Unassigned");
        Assert.Equal(75m, unassigned.Current);
    }

    [Fact]
    public async Task ApAgingSummary_UnassignedSortsLast()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 50m, "prop-Z"));
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 50m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        Assert.Equal("Unassigned", result.ByProperty.Last().GroupKey);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Filters
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_VendorIdsFilter_OmitsOtherVendors()
    {
        var (sut, source) = Build();
        var included = NewPartyId();
        var excluded = NewPartyId();
        source.Payables.Add(MakeReceivedBill(included, Today.AddDays(5), 100m));
        source.Payables.Add(MakeReceivedBill(excluded, Today.AddDays(5), 200m));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters
            {
                ChartId = Chart,
                VendorIds = new[] { included },
            });

        Assert.Single(result.ByVendor);
        Assert.Equal(included.Value, result.ByVendor[0].GroupKey);
        Assert.Equal(100m, result.Totals.TotalOpen);
    }

    [Fact]
    public async Task ApAgingSummary_PropertyIdsFilter_OmitsOtherProperties()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 100m, "prop-A"));
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 200m, "prop-B"));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters
            {
                ChartId = Chart,
                PropertyIds = new[] { "prop-A" },
            });

        Assert.Single(result.ByProperty);
        Assert.Equal("prop-A", result.ByProperty[0].GroupKey);
        Assert.Equal(100m, result.Totals.TotalOpen);
    }

    // When the property filter is active, bills with null PropertyId (Unassigned)
    // are excluded from the filtered view.
    [Fact]
    public async Task ApAgingSummary_PropertyIdsFilter_ExcludesUnassigned()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 100m, "prop-A"));
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 50m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters
            {
                ChartId = Chart,
                PropertyIds = new[] { "prop-A" },
            });

        Assert.DoesNotContain(result.ByProperty, r => r.GroupKey == "Unassigned");
        Assert.Equal(100m, result.Totals.TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Parameter validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_TopOverdueNNegative_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new ApAgingSummaryParameters { ChartId = Chart, TopOverdueN = -1 }));
    }

    [Fact]
    public async Task ApAgingSummary_TopOverdueNOverCap_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new ApAgingSummaryParameters { ChartId = Chart, TopOverdueN = 101 }));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Top-overdue behaviour
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_TopOverdue_OrderedDescendingBy90Plus()
    {
        var (sut, source) = Build();
        var small = NewPartyId();
        var large = NewPartyId();
        source.Payables.Add(MakeReceivedBill(small, Today.AddDays(-100), 100m));
        source.Payables.Add(MakeReceivedBill(large, Today.AddDays(-100), 500m));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart, TopOverdueN = 10 });

        Assert.Equal(2, result.TopOverdue.Count);
        Assert.Equal(500m, result.TopOverdue[0].Days90PlusBalance);
        Assert.Equal(100m, result.TopOverdue[1].Days90PlusBalance);
    }

    [Fact]
    public async Task ApAgingSummary_TopOverdueN_RespectsCap()
    {
        var (sut, source) = Build();
        for (var i = 0; i < 5; i++)
        {
            var v = NewPartyId();
            source.Payables.Add(MakeReceivedBill(v, Today.AddDays(-100), 100m));
        }

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart, TopOverdueN = 3 });

        Assert.Equal(3, result.TopOverdue.Count);
    }

    [Fact]
    public async Task ApAgingSummary_TopOverdueN_ZeroReturnsEmpty()
    {
        var (sut, source) = Build();
        var v = NewPartyId();
        source.Payables.Add(MakeReceivedBill(v, Today.AddDays(-100), 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart, TopOverdueN = 0 });

        Assert.Empty(result.TopOverdue);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Totals consistency
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_Totals_EqualSumOfByVendorRows()
    {
        var (sut, source) = Build();
        var v1 = NewPartyId();
        var v2 = NewPartyId();
        source.Payables.Add(MakeReceivedBill(v1, Today.AddDays(5), 100m));
        source.Payables.Add(MakeReceivedBill(v1, Today.AddDays(-10), 50m));
        source.Payables.Add(MakeReceivedBill(v2, Today.AddDays(-50), 200m));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        var summedFromVendors = result.ByVendor.Sum(r => r.TotalOpen);
        Assert.Equal(summedFromVendors, result.Totals.TotalOpen);
    }

    [Fact]
    public async Task ApAgingSummary_Totals_EqualSumOfByPropertyRows()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(5), 100m, "prop-A"));
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(-10), 50m, "prop-B"));
        source.Payables.Add(MakeReceivedBill(vendor, Today.AddDays(-50), 200m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        var summedFromProperty = result.ByProperty.Sum(r => r.TotalOpen);
        Assert.Equal(summedFromProperty, result.Totals.TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Tenant isolation — different tenant's bills must not appear
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_TenantIsolation_OtherTenantBillsExcluded()
    {
        var (cartridge, source) = Build();

        var otherTenant = new TenantId("other-tenant-ap");
        var myVendor = NewPartyId();
        var theirVendor = NewPartyId();
        var myBill = MakeReceivedBill(myVendor, Today.AddDays(5), 100m);
        var otherBill = MakeReceivedBill(theirVendor, Today.AddDays(5), 250m, tenantId: otherTenant);
        source.Payables.Add(myBill);
        source.Payables.Add(otherBill);

        var result = await cartridge.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        // Should only see the bill scoped to Tenant, not the other tenant's bill.
        Assert.Single(result.ByVendor);
        Assert.Equal(myVendor.Value, result.ByVendor[0].GroupKey);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Party name resolution
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_VendorName_ResolvedFromPartyReadModel()
    {
        var (sut, source) = Build();
        var vendorId = await SeedPartyAsync(source, "Harbor Plumbers LLC");
        source.Payables.Add(MakeReceivedBill(vendorId, Today.AddDays(5), 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        Assert.Equal("Harbor Plumbers LLC", result.ByVendor.Single().GroupLabel);
    }

    [Fact]
    public async Task ApAgingSummary_UnknownVendor_FallsBackToPartyIdValue()
    {
        var (sut, source) = Build();
        var vendorId = NewPartyId();
        // Do NOT seed a Party record — resolution should degrade gracefully.
        source.Payables.Add(MakeReceivedBill(vendorId, Today.AddDays(5), 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        Assert.Equal(vendorId.Value, result.ByVendor.Single().GroupLabel);
    }

    // ──────────────────────────────────────────────────────────────────
    //  AsOfDate wiring
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_ExplicitAsOfDate_UsedForBucketClassification()
    {
        var (sut, source) = Build();
        var vendor = NewPartyId();
        var contextDate = new DateOnly(2026, 5, 17);
        var explicitAsOf = new DateOnly(2026, 6, 1);
        var dueDate = new DateOnly(2026, 4, 22);
        source.Payables.Add(MakeReceivedBill(vendor, dueDate, 300m));

        var result = await sut.ExecuteAsync(Context(contextDate),
            new ApAgingSummaryParameters { ChartId = Chart, AsOfDate = explicitAsOf });

        Assert.Equal(explicitAsOf, result.AsOf);
        // Explicit asOf: 40 days => Days31To60; context date would be 25 => Days0To30.
        Assert.Equal(300m, result.ByVendor.Single().Days31To60);
        Assert.Equal(0m, result.ByVendor.Single().Days0To30);
    }

    [Fact]
    public async Task ApAgingSummary_NullAsOfDate_DefaultsToContextDate()
    {
        var (sut, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        Assert.Equal(Today, result.AsOf);
    }

    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ApAgingSummary_ForwardsResolvedAsOf()
    {
        var (sut, source) = Build();
        await sut.ExecuteAsync(Context(), new ApAgingSummaryParameters { ChartId = Chart });
        Assert.Equal([Today], source.ObservedPayableAsOfs);
    }
    //  Bucket boundary correctness — mirrors AR test for completeness
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApAgingSummary_AllBuckets_PopulatedCorrectly()
    {
        var (sut, source) = Build();
        var asOf = Today;
        var vendor = NewPartyId();
        source.Payables.Add(MakeReceivedBill(vendor, asOf.AddDays(5),   100m)); // Current
        source.Payables.Add(MakeReceivedBill(vendor, asOf.AddDays(-15), 200m)); // 0-30
        source.Payables.Add(MakeReceivedBill(vendor, asOf.AddDays(-45), 300m)); // 31-60
        source.Payables.Add(MakeReceivedBill(vendor, asOf.AddDays(-75), 400m)); // 61-90
        source.Payables.Add(MakeReceivedBill(vendor, asOf.AddDays(-120), 500m)); // 90+

        var result = await sut.ExecuteAsync(Context(),
            new ApAgingSummaryParameters { ChartId = Chart });

        var row = result.ByVendor.Single();
        Assert.Equal(100m, row.Current);
        Assert.Equal(200m, row.Days0To30);
        Assert.Equal(300m, row.Days31To60);
        Assert.Equal(400m, row.Days61To90);
        Assert.Equal(500m, row.Days90Plus);
        Assert.Equal(1500m, row.TotalOpen);
        Assert.Equal(1500m, result.Totals.TotalOpen);
    }
}
