using Harborline.Blocks.Reports.Cartridges.ApAgingSummary;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class ApAgingSummaryDeterminismTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("ap-det");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);
    private static readonly DateOnly AsOf = new(2026, 5, 17);

    private static (ApAgingSummaryCartridge Sut, ApAgingSummaryParameters P, ReportExecutionContext C) Build()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "AP determinism", "USD", true));
        var vendor = new PartyId(Guid.NewGuid().ToString());
        source.Parties.Add(new ReportParty(vendor, "Vendor"));
        source.Payables.Add(new ReportOpenItem("bill-det", Tenant, Chart, vendor, "p1",
            AsOf.AddDays(-31), 125m, AgingBucket.Current));
        return (new ApAgingSummaryCartridge(source),
            new ApAgingSummaryParameters { ChartId = Chart, AsOfDate = AsOf },
            new ReportExecutionContext(Tenant, "marker:ap-det:1",
                new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero), Principal));
    }

    private static void AssertEqual(ApAgingSummaryResult a, ApAgingSummaryResult b)
    {
        Assert.NotEmpty(a.ByVendor);
        Assert.Equal(a.ChartId, b.ChartId); Assert.Equal(a.AsOf, b.AsOf);
        Assert.Equal(a.Totals, b.Totals);
        Assert.Equal(a.ByVendor, b.ByVendor); Assert.Equal(a.ByProperty, b.ByProperty);
        Assert.Equal(a.TopOverdue, b.TopOverdue);
    }

    [Fact] public async Task ExecuteAsync_IsDeterministic_AcrossRepeatedRuns()
    { var (s, p, c) = Build(); AssertEqual(await s.ExecuteAsync(c, p), await s.ExecuteAsync(c, p)); }

    [Fact] public async Task ExecuteAsync_SameMarker_SameResult()
    { var (s, p, c) = Build(); var c2 = c with { SnapshotMarker = c.SnapshotMarker }; AssertEqual(await s.ExecuteAsync(c, p), await s.ExecuteAsync(c2, p)); }
}
