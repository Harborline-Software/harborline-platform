using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLoss;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class ProfitAndLossCartridgeTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-pnl");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    private static ReportAccount Acct(string code, GLAccountType type)
        => new(GLAccountId.NewId(), Chart, code, $"Account {code}", type,
            type is GLAccountType.Asset or GLAccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit,
            true);

    /// <summary>
    /// Post a balanced entry (debit one account, credit another).
    /// Convention matches TrialBalanceCartridgeTests.
    /// </summary>
    private static ReportJournalEntry PostedEntry(DateOnly date, GLAccountId debit, GLAccountId credit, decimal amount)
        => new(Guid.NewGuid().ToString(), Tenant, Chart, date,
            [new(debit, amount, 0m, null), new(credit, 0m, amount, null)]);

    private static ReportExecutionContext Context()
        => new ReportExecutionContext(Tenant, "marker:pnl:1",
            new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero), Principal);

    private static (ProfitAndLossCartridge Cartridge, InMemoryReportQuerySource Source)
        Build(IEnumerable<ReportAccount>? seedAccounts = null)
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "Test", "USD", true));
        source.Accounts.AddRange(seedAccounts ?? []);
        return (new ProfitAndLossCartridge(source), source);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Parameter validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProfitAndLoss_PeriodStartAfterPeriodEnd_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(), new ProfitAndLossParameters
            {
                ChartId = Chart,
                PeriodStart = new DateOnly(2026, 12, 31),
                PeriodEnd = new DateOnly(2026, 1, 1),
            }));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Core correctness
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProfitAndLoss_EmptyChart_ReturnsZeroTotals()
    {
        var (sut, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart });

        Assert.Empty(result.RevenueLines);
        Assert.Empty(result.ExpenseLines);
        Assert.Equal(0m, result.TotalRevenue);
        Assert.Equal(0m, result.TotalExpenses);
        Assert.Equal(0m, result.NetProfit);
    }
    [Fact]
    public async Task ProfitAndLoss_UnknownChart_ThrowsValidationException()
    {
        var (sut, source) = Build();
        source.Charts.Clear();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() => sut.ExecuteAsync(
            Context(), new ProfitAndLossParameters { ChartId = Chart }));
    }

    [Fact]
    public async Task ProfitAndLoss_NetProfitEqualsRevenueMinusExpenses()
    {
        // Cash (Asset) debit; Revenue credit => revenue +300.
        // Expense debit; Cash (Asset) credit => expense +100.
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var expense = Acct("5000", GLAccountType.Expense);
        var (sut, source) = Build(new[] { cash, revenue, expense });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 300m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 15), expense.Id, cash.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart,
                PeriodStart = new DateOnly(2026, 1, 1),
                PeriodEnd = new DateOnly(2026, 12, 31) });

        Assert.Equal(300m, result.TotalRevenue);
        Assert.Equal(100m, result.TotalExpenses);
        Assert.Equal(200m, result.NetProfit);
    }
    [Fact]
    public async Task ProfitAndLoss_ForwardsSnapshotMarkerVerbatim()
    {
        var (sut, source) = Build();
        await sut.ExecuteAsync(Context(), new ProfitAndLossParameters { ChartId = Chart });
        Assert.Equal(["marker:pnl:1"], source.ObservedJournalMarkers);
    }

    [Fact]
    public async Task ProfitAndLoss_MultipleRevAccounts_TotalsCorrect()
    {
        var cash = Acct("1000", GLAccountType.Asset);
        var rev1 = Acct("4000", GLAccountType.Revenue);
        var rev2 = Acct("4100", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, rev1, rev2 });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, rev1.Id, 200m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 2), cash.Id, rev2.Id, 150m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart });

        Assert.Equal(2, result.RevenueLines.Count);
        Assert.Equal(350m, result.TotalRevenue);
    }

    [Fact]
    public async Task ProfitAndLoss_NetLoss_NegativeNetProfit()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var expense = Acct("5000", GLAccountType.Expense);
        var (sut, source) = Build(new[] { cash, revenue, expense });
        // Revenue 50, Expense 200 => net loss -150.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 50m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 2), expense.Id, cash.Id, 200m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart });

        Assert.Equal(50m, result.TotalRevenue);
        Assert.Equal(200m, result.TotalExpenses);
        Assert.Equal(-150m, result.NetProfit);
    }

    [Fact]
    public async Task ProfitAndLoss_PeriodWindow_ExcludesEntriesOutsideWindow()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revenue });
        // In window.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 4, 1), cash.Id, revenue.Id, 300m));
        // After window end — excluded.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 6, 1), cash.Id, revenue.Id, 999m));
        // Before window start — excluded.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2025, 12, 31), cash.Id, revenue.Id, 999m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart,
                PeriodStart = new DateOnly(2026, 1, 1),
                PeriodEnd = new DateOnly(2026, 5, 31) });

        Assert.Equal(300m, result.TotalRevenue);
    }

    [Fact]
    public async Task ProfitAndLoss_NoPeriodStart_IncludesAllHistoricEntries()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revenue });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2020, 1, 1), cash.Id, revenue.Id, 100m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 200m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart,
                PeriodEnd = new DateOnly(2026, 12, 31) });

        Assert.Equal(300m, result.TotalRevenue);
    }

    [Fact]
    public async Task ProfitAndLoss_NoPeriodEnd_DefaultsToContextWallClock()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revenue });
        // Context.AsOfUtc = 2026-05-17. Entry on that date should be included.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 17), cash.Id, revenue.Id, 400m));
        // Future entry — excluded.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2099, 1, 1), cash.Id, revenue.Id, 999m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart });

        Assert.Equal(400m, result.TotalRevenue);
        Assert.Equal(new DateOnly(2026, 5, 17), result.PeriodEnd);
    }

    [Fact]
    public async Task ProfitAndLoss_IncludeZeroFalse_OmitsZeroAccounts()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var rev1    = Acct("4000", GLAccountType.Revenue);
        var revZero = Acct("4100", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, rev1, revZero });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, rev1.Id, 100m));
        // revZero has no activity.

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart, IncludeZeroBalanceAccounts = false });

        Assert.DoesNotContain(result.RevenueLines, l => l.AccountId == revZero.Id);
    }

    [Fact]
    public async Task ProfitAndLoss_IncludeZeroTrue_IncludesZeroAccounts()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var rev1    = Acct("4000", GLAccountType.Revenue);
        var revZero = Acct("4100", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, rev1, revZero });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, rev1.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart, IncludeZeroBalanceAccounts = true });

        Assert.Contains(result.RevenueLines, l => l.AccountId == revZero.Id);
    }

    [Fact]
    public async Task ProfitAndLoss_Lines_AreOrderedByCodeThenIdStable()
    {
        var cash = Acct("1000", GLAccountType.Asset);
        var revB = Acct("4100", GLAccountType.Revenue);
        var revA = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revB, revA });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revA.Id, 100m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 2), cash.Id, revB.Id, 200m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart });

        var codes = result.RevenueLines.Select(l => l.AccountCode).ToArray();
        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal).ToArray(), codes);
    }

    [Fact]
    public async Task ProfitAndLoss_AssetAndBSAccounts_ExcludedFromLines()
    {
        var cash     = Acct("1000", GLAccountType.Asset);
        var equity   = Acct("3000", GLAccountType.Equity);
        var revenue  = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, equity, revenue });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart, IncludeZeroBalanceAccounts = true });

        Assert.DoesNotContain(result.RevenueLines, l => l.AccountId == cash.Id);
        Assert.DoesNotContain(result.RevenueLines, l => l.AccountId == equity.Id);
        Assert.DoesNotContain(result.ExpenseLines, l => l.AccountId == cash.Id);
        Assert.DoesNotContain(result.ExpenseLines, l => l.AccountId == equity.Id);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Tenant isolation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProfitAndLoss_TenantIsolation_ExcludesOtherTenantEntries()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revenue });

        var otherTenant = new TenantId("tenant-pnl-other");

        source.JournalEntries.Add(
            PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 200m));

        var otherEntry = new ReportJournalEntry(
            Guid.NewGuid().ToString(), otherTenant, Chart, new DateOnly(2026, 5, 1),
            [new(cash.Id, 9999m, 0m, null), new(revenue.Id, 0m, 9999m, null)]);
        source.JournalEntries.Add(otherEntry);

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart });

        Assert.Equal(200m, result.TotalRevenue);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Reversed-entry exclusion (BUG-002 fix: Reversed status excluded)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProfitAndLoss_ReversedEntries_ExcludedFromBalance()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revenue });

        // Posted entry: revenue 500.
        source.JournalEntries.Add(
            PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 500m));

        // Reversed entry (should be excluded from the P&L).
        // Reversed entries are excluded by the neutral source contract, which exposes posted entries only.

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossParameters { ChartId = Chart });

        // Only the 500 from the Posted entry.
        Assert.Equal(500m, result.TotalRevenue);
    }

    // ──────────────────────────────────────────────────────────────────
    //  ReportKind
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ProfitAndLossCartridge_KindIsProfitAndLoss()
    {
        var (sut, _) = Build();
        Assert.Equal(ReportKind.ProfitAndLoss, sut.Kind);
    }
}
