using Harborline.Blocks.Reports.Cartridges.ProfitAndLoss;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class ProfitAndLossDeterminismTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("pnl-det");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    private static (ProfitAndLossCartridge Sut, ProfitAndLossParameters P, ReportExecutionContext C) Build()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "P&L determinism", "USD", true));
        var cash = new ReportAccount(GLAccountId.NewId(), Chart, "1000", "Cash", GLAccountType.Asset, NormalBalance.Debit, true);
        var revenue = new ReportAccount(GLAccountId.NewId(), Chart, "4000", "Revenue", GLAccountType.Revenue, NormalBalance.Credit, true);
        source.Accounts.AddRange([revenue, cash]);
        source.JournalEntries.Add(new ReportJournalEntry("pnl-det-1", Tenant, Chart, new DateOnly(2026, 5, 1),
            [new(cash.Id, 300m, 0m, null), new(revenue.Id, 0m, 300m, null)]));
        return (new ProfitAndLossCartridge(source), new ProfitAndLossParameters
            { ChartId = Chart, PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31) },
            new ReportExecutionContext(Tenant, "marker:pnl-det:1", new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero), Principal));
    }

    private static void AssertEqual(ProfitAndLossResult a, ProfitAndLossResult b)
    {
        Assert.NotEmpty(a.RevenueLines); Assert.Equal(a.ChartId, b.ChartId);
        Assert.Equal(a.PeriodStart, b.PeriodStart); Assert.Equal(a.PeriodEnd, b.PeriodEnd);
        Assert.Equal(a.RevenueLines, b.RevenueLines); Assert.Equal(a.ExpenseLines, b.ExpenseLines);
        Assert.Equal(a.TotalRevenue, b.TotalRevenue); Assert.Equal(a.TotalExpenses, b.TotalExpenses);
        Assert.Equal(a.NetProfit, b.NetProfit);
    }

    [Fact] public async Task ExecuteAsync_IsDeterministic_AcrossRepeatedRuns()
    { var (s, p, c) = Build(); AssertEqual(await s.ExecuteAsync(c, p), await s.ExecuteAsync(c, p)); }
    [Fact] public async Task ExecuteAsync_SameMarker_SameResult()
    { var (s, p, c) = Build(); var c2 = c with { SnapshotMarker = c.SnapshotMarker }; AssertEqual(await s.ExecuteAsync(c, p), await s.ExecuteAsync(c2, p)); }
}
