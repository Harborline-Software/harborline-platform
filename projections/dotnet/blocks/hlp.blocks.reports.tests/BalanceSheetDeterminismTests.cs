using Harborline.Blocks.Reports.Cartridges.BalanceSheet;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class BalanceSheetDeterminismTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("bs-det");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    private static (BalanceSheetCartridge Sut, BalanceSheetParameters P, ReportExecutionContext C) Build()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "BS determinism", "USD", true));
        var cash = new ReportAccount(GLAccountId.NewId(), Chart, "1000", "Cash", GLAccountType.Asset, NormalBalance.Debit, true);
        var equity = new ReportAccount(GLAccountId.NewId(), Chart, "3000", "Equity", GLAccountType.Equity, NormalBalance.Credit, true);
        source.Accounts.AddRange([equity, cash]);
        source.JournalEntries.Add(new ReportJournalEntry("bs-det-1", Tenant, Chart, new DateOnly(2026, 5, 1),
            [new(cash.Id, 200m, 0m, null), new(equity.Id, 0m, 200m, null)]));
        return (new BalanceSheetCartridge(source),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 5, 31) },
            new ReportExecutionContext(Tenant, "marker:bs-det:1", new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero), Principal));
    }

    private static void AssertEqual(BalanceSheetResult a, BalanceSheetResult b)
    {
        Assert.NotEmpty(a.Assets); Assert.Equal(a.ChartId, b.ChartId); Assert.Equal(a.AsOf, b.AsOf);
        Assert.Equal(a.PeriodId, b.PeriodId); Assert.Equal(a.Assets, b.Assets);
        Assert.Equal(a.Liabilities, b.Liabilities); Assert.Equal(a.Equity, b.Equity);
        Assert.Equal(a.TotalAssets, b.TotalAssets); Assert.Equal(a.TotalLiabilities, b.TotalLiabilities);
        Assert.Equal(a.TotalEquity, b.TotalEquity); Assert.Equal(a.IsBalanced, b.IsBalanced);
        Assert.Equal(a.IsProvisional, b.IsProvisional); Assert.Equal(a.Warnings, b.Warnings);
    }

    [Fact] public async Task ExecuteAsync_IsDeterministic_AcrossRepeatedRuns()
    { var (s, p, c) = Build(); AssertEqual(await s.ExecuteAsync(c, p), await s.ExecuteAsync(c, p)); }
    [Fact] public async Task ExecuteAsync_SameMarker_SameResult()
    { var (s, p, c) = Build(); var c2 = c with { SnapshotMarker = c.SnapshotMarker }; AssertEqual(await s.ExecuteAsync(c, p), await s.ExecuteAsync(c2, p)); }
}
