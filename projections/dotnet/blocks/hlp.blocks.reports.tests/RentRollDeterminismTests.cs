using System;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.RentRoll;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 6 — determinism assertions for <see cref="RentRollCartridge"/>.
/// Per-field comparisons because <see cref="RentRollResult"/> carries
/// <c>IReadOnlyList</c> properties whose reference-based equality would
/// break standard record structural equality.
/// </summary>
public sealed class RentRollDeterminismTests
{
    private static readonly TenantId Tenant = new("tenant-rr-det");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly DateOnly AsOf = new(2026, 5, 17);
    private static int _partySeq;

    private static (RentRollCartridge Cartridge,
                    InMemoryReportQuerySource Leases,
                    InMemoryReportQuerySource Invoices,
                    InMemoryReportQuerySource Parties)
        Build()
    {
        var source = new InMemoryReportQuerySource();        source.Charts.Add(new ReportChart(Chart, "Rent roll determinism", "USD", true));
        return (new RentRollCartridge(source), source, source, source);
    }

    private static ReportExecutionContext Context()
        => new ReportExecutionContext(
            Tenant, "marker:rr:det:1",
            new DateTimeOffset(AsOf.Year, AsOf.Month, AsOf.Day, 12, 0, 0, TimeSpan.Zero),
            Principal);

    private static RentRollParameters Parameters()
        => new() { ChartId = Chart, AsOfDate = AsOf };

    private static Task<ReportLease> MakeActiveLease(
        InMemoryReportQuerySource svc,
        string propertyKey,
        string unitLabel,
        PartyId tenant,
        decimal rent)
    {
        var lease = new ReportLease(LeaseId.NewId(), Tenant, propertyKey, unitLabel,
            new[] { tenant }, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            LeasePhase.Active, rent);
        svc.Leases.Add(lease);
        return Task.FromResult(lease);
    }

    private static PartyId NewPartyId()
        => new($"party-det-{System.Threading.Interlocked.Increment(ref _partySeq)}");

    private static void AssertEqual(RentRollResult r1, RentRollResult r2)
    {
        Assert.Equal(r1.AsOf, r2.AsOf);
        Assert.Equal(r1.Properties.Count, r2.Properties.Count);
        Assert.Equal(r1.Portfolio.PropertiesCovered, r2.Portfolio.PropertiesCovered);
        Assert.Equal(r1.Portfolio.TotalUnits, r2.Portfolio.TotalUnits);
        Assert.Equal(r1.Portfolio.OccupiedUnits, r2.Portfolio.OccupiedUnits);
        Assert.Equal(r1.Portfolio.OccupancyRate, r2.Portfolio.OccupancyRate);
        Assert.Equal(r1.Portfolio.MonthlyRentTotal, r2.Portfolio.MonthlyRentTotal);
        Assert.Equal(r1.Portfolio.OpenBalanceTotal, r2.Portfolio.OpenBalanceTotal);

        for (var i = 0; i < r1.Properties.Count; i++)
        {
            var p1 = r1.Properties[i];
            var p2 = r2.Properties[i];
            Assert.Equal(p1.PropertyKey, p2.PropertyKey);
            Assert.Equal(p1.Units.Count, p2.Units.Count);
            Assert.Equal(p1.Summary.OccupancyRate, p2.Summary.OccupancyRate);
            Assert.Equal(p1.Summary.MonthlyRentTotal, p2.Summary.MonthlyRentTotal);

            for (var j = 0; j < p1.Units.Count; j++)
            {
                var u1 = p1.Units[j];
                var u2 = p2.Units[j];
                Assert.Equal(u1.UnitLabel, u2.UnitLabel);
                Assert.Equal(u1.MonthlyRent, u2.MonthlyRent);
                Assert.Equal(u1.ProjectedNextMonthRent, u2.ProjectedNextMonthRent);
                Assert.Equal(u1.Status, u2.Status);
                Assert.Equal(u1.DelinquencyBucket, u2.DelinquencyBucket);
                Assert.Equal(u1.OpenBalance, u2.OpenBalance);
                Assert.Equal(u1.ExpiringSoon, u2.ExpiringSoon);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_IsDeterministic_AcrossRepeatedRuns()
    {
        var (sut, leases, _, _) = Build();
        var t1 = NewPartyId();
        var t2 = NewPartyId();

        await MakeActiveLease(svc: leases, propertyKey: "prop-a", unitLabel: "u1", tenant: t1, rent: 1200m);
        await MakeActiveLease(svc: leases, propertyKey: "prop-a", unitLabel: "u2", tenant: t2, rent: 1500m);

        var ctx = Context();
        var p   = Parameters();
        var r1  = await sut.ExecuteAsync(ctx, p);
        var r2  = await sut.ExecuteAsync(ctx, p);
        AssertEqual(r1, r2);
    }

    [Fact]
    public async Task ExecuteAsync_SameMarker_SameResult()
    {
        var (sut, leases, _, _) = Build();
        var tenant = NewPartyId();

        await MakeActiveLease(
            svc: leases,
            propertyKey: "prop-a",
            unitLabel: "u1",
            tenant: tenant,
            rent: 900m);

        var p   = Parameters();
        var ctx = Context();
        var r1  = await sut.ExecuteAsync(ctx, p);
        var r2  = await sut.ExecuteAsync(ctx, p);
        AssertEqual(r1, r2);
    }
}
