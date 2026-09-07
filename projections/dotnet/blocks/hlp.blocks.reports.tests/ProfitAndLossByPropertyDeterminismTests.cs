using System;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLossByProperty;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 5 — determinism assertions for
/// <see cref="ProfitAndLossByPropertyCartridge"/>.
/// Per-field assertions because <see cref="ProfitAndLossByPropertyResult"/>
/// carries <c>IReadOnlyList</c> properties whose reference-based equality
/// would break standard record structural equality.
/// </summary>
public sealed class ProfitAndLossByPropertyDeterminismTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-pnl-det");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);
    private static readonly DateOnly AsOf = new(2026, 5, 17);

    private static readonly GLAccountId ClearingId = GLAccountId.NewId();

    private static ReportAccount RevAcct(string code)
        => new(GLAccountId.NewId(), Chart, code, $"Rev {code}",
            GLAccountType.Revenue, NormalBalance.Credit, true);

    private static ReportAccount ExpAcct(string code)
        => new(GLAccountId.NewId(), Chart, code, $"Exp {code}",
            GLAccountType.Expense, NormalBalance.Debit, true);

    private static (ProfitAndLossByPropertyCartridge Cartridge, InMemoryReportQuerySource Source)
        Build(System.Collections.Generic.IEnumerable<ReportAccount>? seed = null)
    {
        var source = new InMemoryReportQuerySource();        source.Charts.Add(new ReportChart(Chart, "P&L property determinism", "USD", true));
        source.Accounts.AddRange(seed ?? Array.Empty<ReportAccount>());
        return (new ProfitAndLossByPropertyCartridge(source), source);
    }

    private static ProfitAndLossByPropertyParameters Parameters()
        => new ProfitAndLossByPropertyParameters { ChartId = Chart, PeriodEnd = AsOf };

    private static ReportExecutionContext Context()
        => new ReportExecutionContext(
            Tenant,
            "marker:pnl:det:1",
            new DateTimeOffset(AsOf.Year, AsOf.Month, AsOf.Day, 12, 0, 0, TimeSpan.Zero),
            Principal);

    private static ReportJournalEntry PostedEntry(GLAccountId accountId, bool isCredit, decimal amount, string? propId)
    {
        var lines = isCredit
            ? new ReportJournalLine[] { new(ClearingId, amount, 0m, propId), new(accountId, 0m, amount, propId) }
            : new ReportJournalLine[] { new(accountId, amount, 0m, propId), new(ClearingId, 0m, amount, propId) };
        return new ReportJournalEntry(Guid.NewGuid().ToString(), Tenant, Chart, AsOf, lines);
    }

    private static void AssertEqual(ProfitAndLossByPropertyResult r1, ProfitAndLossByPropertyResult r2)
    {
        Assert.Equal(r1.ChartId, r2.ChartId);
        Assert.Equal(r1.PeriodEnd, r2.PeriodEnd);
        Assert.Equal(r1.PeriodStart, r2.PeriodStart);
        Assert.Equal(r1.Totals.TotalRevenue, r2.Totals.TotalRevenue);
        Assert.Equal(r1.Totals.TotalExpenses, r2.Totals.TotalExpenses);
        Assert.Equal(r1.Totals.NetIncome, r2.Totals.NetIncome);
        Assert.Equal(r1.ByProperty.Count, r2.ByProperty.Count);
        for (var i = 0; i < r1.ByProperty.Count; i++)
        {
            Assert.Equal(r1.ByProperty[i].PropertyKey, r2.ByProperty[i].PropertyKey);
            Assert.Equal(r1.ByProperty[i].TotalRevenue, r2.ByProperty[i].TotalRevenue);
            Assert.Equal(r1.ByProperty[i].TotalExpenses, r2.ByProperty[i].TotalExpenses);
            Assert.Equal(r1.ByProperty[i].NetIncome, r2.ByProperty[i].NetIncome);
            Assert.Equal(r1.ByProperty[i].RevenueLines.Count, r2.ByProperty[i].RevenueLines.Count);
            Assert.Equal(r1.ByProperty[i].ExpenseLines.Count, r2.ByProperty[i].ExpenseLines.Count);
        }
    }

    [Fact]
    public async Task ExecuteAsync_IsDeterministic_AcrossRepeatedRuns()
    {
        var rev = RevAcct("4000");
        var exp = ExpAcct("5000");
        var (sut, source) = Build(new[] { rev, exp });

        source.JournalEntries.Add(PostedEntry(rev.Id, isCredit: true, 1500m, "prop-A"));
        source.JournalEntries.Add(PostedEntry(rev.Id, isCredit: true, 800m, "prop-B"));
        source.JournalEntries.Add(PostedEntry(exp.Id, isCredit: false, 300m, "prop-A"));

        var ctx = Context();
        var p = Parameters();
        var r1 = await sut.ExecuteAsync(ctx, p);
        var r2 = await sut.ExecuteAsync(ctx, p);
        AssertEqual(r1, r2);
    }

    [Fact]
    public async Task ExecuteAsync_SameMarker_SameResult()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(PostedEntry(rev.Id, isCredit: true, 600m, "prop-A"));

        var p = Parameters();
        var ctx = Context();
        var r1 = await sut.ExecuteAsync(ctx, p);
        var r2 = await sut.ExecuteAsync(ctx, p);
        AssertEqual(r1, r2);
    }
}
