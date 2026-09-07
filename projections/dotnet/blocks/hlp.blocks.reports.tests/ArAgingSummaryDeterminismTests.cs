using System;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ArAgingSummary;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 3 — determinism assertions for the AR Aging Summary cartridge.
/// Implements the report-cartridge determinism convention
/// with per-field comparisons because <see cref="ArAgingSummaryResult"/>
/// carries <c>IReadOnlyList</c> properties whose reference-based equality
/// would break standard record structural equality.
/// </summary>
public sealed class ArAgingSummaryDeterminismTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-ar-det");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);
    private static readonly DateOnly AsOf = new(2026, 5, 17);
    private static int _seq = 0;

    private static (ArAgingSummaryCartridge Cartridge, InMemoryReportQuerySource Source)
        Build()
    {
        var source = new InMemoryReportQuerySource();        source.Charts.Add(new ReportChart(Chart, "AR determinism", "USD", true));
        return (new ArAgingSummaryCartridge(source), source);
    }

    private static ArAgingSummaryParameters Parameters()
        => new ArAgingSummaryParameters { ChartId = Chart, AsOfDate = AsOf };

    private static ReportExecutionContext Context()
        => new ReportExecutionContext(
            Tenant,
            "marker:ar:det:1",
            new DateTimeOffset(AsOf.Year, AsOf.Month, AsOf.Day, 12, 0, 0, TimeSpan.Zero),
            Principal);

    private static ReportOpenItem MakeIssuedInvoice(PartyId customerId, DateOnly dueDate, decimal amount)
    {
        var seq = System.Threading.Interlocked.Increment(ref _seq);
        var daysPastDue = AsOf.DayNumber - dueDate.DayNumber;
        var bucket = daysPastDue <= 0 ? AgingBucket.Current
            : daysPastDue <= 30 ? AgingBucket.Days0To30
            : daysPastDue <= 60 ? AgingBucket.Days31To60
            : daysPastDue <= 90 ? AgingBucket.Days61To90
            : AgingBucket.Days90Plus;
        return new ReportOpenItem($"INV-2026-05-17-DT-{seq:D4}", Tenant, Chart, customerId,
            null, dueDate, amount, bucket);
    }

    private static PartyId NewPartyId() => new(Guid.NewGuid().ToString());

    private static void AssertEqual(ArAgingSummaryResult r1, ArAgingSummaryResult r2)
    {
        Assert.Equal(r1.ChartId, r2.ChartId);
        Assert.Equal(r1.AsOf, r2.AsOf);
        Assert.Equal(r1.ByCustomer.Count, r2.ByCustomer.Count);
        Assert.Equal(r1.ByProperty.Count, r2.ByProperty.Count);
        Assert.Equal(r1.TopDelinquent.Count, r2.TopDelinquent.Count);
        Assert.Equal(r1.Totals.TotalOpen, r2.Totals.TotalOpen);
        Assert.Equal(r1.Totals.Current, r2.Totals.Current);
        Assert.Equal(r1.Totals.Days0To30, r2.Totals.Days0To30);
        Assert.Equal(r1.Totals.Days31To60, r2.Totals.Days31To60);
        Assert.Equal(r1.Totals.Days61To90, r2.Totals.Days61To90);
        Assert.Equal(r1.Totals.Days90Plus, r2.Totals.Days90Plus);
        for (var i = 0; i < r1.ByCustomer.Count; i++)
            Assert.Equal(r1.ByCustomer[i], r2.ByCustomer[i]);
        for (var i = 0; i < r1.ByProperty.Count; i++)
            Assert.Equal(r1.ByProperty[i], r2.ByProperty[i]);
        for (var i = 0; i < r1.TopDelinquent.Count; i++)
            Assert.Equal(r1.TopDelinquent[i], r2.TopDelinquent[i]);
    }

    [Fact]
    public async Task ExecuteAsync_IsDeterministic_AcrossRepeatedRuns()
    {
        var (sut, invoices) = Build();
        var c1 = NewPartyId();
        var c2 = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c1, AsOf.AddDays(5), 100m));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c2, AsOf.AddDays(-100), 400m));

        var ctx = Context();
        var p = Parameters();
        var r1 = await sut.ExecuteAsync(ctx, p);
        var r2 = await sut.ExecuteAsync(ctx, p);
        AssertEqual(r1, r2);
    }

    [Fact]
    public async Task ExecuteAsync_SameMarker_SameResult()
    {
        var (sut, invoices) = Build();
        var customer = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, AsOf.AddDays(-35), 200m));

        var p = Parameters();
        var ctx = Context();
        var r1 = await sut.ExecuteAsync(ctx, p);
        var r2 = await sut.ExecuteAsync(ctx, p);
        AssertEqual(r1, r2);
    }
}
