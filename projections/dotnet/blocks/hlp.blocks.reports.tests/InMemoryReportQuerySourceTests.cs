using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class InMemoryReportQuerySourceTests
{
    [Fact]
    public async Task OpenItemReads_ClassifyFromRequestedAsOf()
    {
        var source = new InMemoryReportQuerySource();
        var tenant = new TenantId("fake-asof");
        var chart = ChartOfAccountsId.NewId();
        var party = new PartyId(Guid.NewGuid().ToString());
        var item = new ReportOpenItem("item-1", tenant, chart, party, null,
            new DateOnly(2026, 3, 1), 10m, AgingBucket.Days31To60);
        source.Receivables.Add(item);
        source.Payables.Add(item with { Id = "item-2" });

        var arCurrent = await source.GetOpenReceivablesAsync(tenant, chart, new DateOnly(2026, 2, 28));
        var arOld = await source.GetOpenReceivablesAsync(tenant, chart, new DateOnly(2026, 7, 1));
        var apCurrent = await source.GetOpenPayablesAsync(tenant, chart, new DateOnly(2026, 2, 28));
        var apOld = await source.GetOpenPayablesAsync(tenant, chart, new DateOnly(2026, 7, 1));

        Assert.Equal(AgingBucket.Current, Assert.Single(arCurrent).Bucket);
        Assert.Equal(AgingBucket.Days90Plus, Assert.Single(arOld).Bucket);
        Assert.Equal(AgingBucket.Current, Assert.Single(apCurrent).Bucket);
        Assert.Equal(AgingBucket.Days90Plus, Assert.Single(apOld).Bucket);
    }
}
