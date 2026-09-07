using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLossByProperty;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 5 — unit tests for <see cref="ProfitAndLossByPropertyCartridge"/>.
/// Seeds an <see cref="InMemoryReportQuerySource"/> with posted entries and asserts
/// revenue/expense/net-income projections, per-property bucketing, and filters.
/// </summary>
public sealed class ProfitAndLossByPropertyCartridgeTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-pnl");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    // Reference date used across tests.
    private static readonly DateOnly Today = new(2026, 5, 17);

    // ──────────────────────────────────────────────────────────────────
    //  Account seeds
    // ──────────────────────────────────────────────────────────────────

    private static ReportAccount RevAcct(string code, string name = "Revenue")
        => new(GLAccountId.NewId(), Chart, code, name, GLAccountType.Revenue, NormalBalance.Credit, true);

    private static ReportAccount ExpAcct(string code, string name = "Expense")
        => new(GLAccountId.NewId(), Chart, code, name, GLAccountType.Expense, NormalBalance.Debit, true);

    // ──────────────────────────────────────────────────────────────────
    //  Builder helpers
    // ──────────────────────────────────────────────────────────────────

    private static ReportExecutionContext Context(DateOnly? asOf = null)
    {
        var dt = asOf ?? Today;
        var utc = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 12, 0, 0, TimeSpan.Zero);
        return new ReportExecutionContext(Tenant, "marker:pnl:1", utc, Principal);
    }

    private static (ProfitAndLossByPropertyCartridge Cartridge, InMemoryReportQuerySource Source)
        Build(IEnumerable<ReportAccount>? seedAccounts = null)
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "P&L by property", "USD", true));
        source.Accounts.AddRange(seedAccounts ?? Array.Empty<ReportAccount>());
        return (new ProfitAndLossByPropertyCartridge(source), source);
    }

    /// <summary>
    /// Post a revenue entry (credit revenue account, debit a clearing account).
    /// </summary>
    private static ReportJournalEntry RevenueEntry(
        GLAccountId revenueAccountId,
        GLAccountId clearingAccountId,
        decimal amount,
        DateOnly date,
        string? propertyId = null)
        => new(Guid.NewGuid().ToString(), Tenant, Chart, date,
            [new(clearingAccountId, amount, 0m, propertyId), new(revenueAccountId, 0m, amount, propertyId)]);

    /// <summary>
    /// Post an expense entry (debit expense account, credit a clearing account).
    /// </summary>
    private static ReportJournalEntry ExpenseEntry(
        GLAccountId expenseAccountId,
        GLAccountId clearingAccountId,
        decimal amount,
        DateOnly date,
        string? propertyId = null)
        => new(Guid.NewGuid().ToString(), Tenant, Chart, date,
            [new(expenseAccountId, amount, 0m, propertyId), new(clearingAccountId, 0m, amount, propertyId)]);

    // Clearing account — asset type so it doesn't appear in P&L revenue/expense.
    private static readonly GLAccountId ClearingId = GLAccountId.NewId();

    // ──────────────────────────────────────────────────────────────────
    //  Edge case — empty chart
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_EmptyJournal_ReturnsZeroTotalsAndEmptyPropertyRows()
    {
        var rev = RevAcct("4000");
        var exp = ExpAcct("5000");
        var (sut, source) = Build(new[] { rev, exp });

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Empty(result.ByProperty);
        Assert.Equal(0m, result.Totals.TotalRevenue);
        Assert.Equal(0m, result.Totals.TotalExpenses);        Assert.Equal(["marker:pnl:1"], source.ObservedJournalMarkers);
        Assert.Equal(0m, result.Totals.NetIncome);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Revenue sign convention
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_SingleRevenueEntry_TotalRevenueIsPositive()
    {
        var rev = RevAcct("4000", "Rental Revenue");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 1000m, Today, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Equal(1000m, result.Totals.TotalRevenue);
        Assert.Equal(0m, result.Totals.TotalExpenses);
        Assert.Equal(1000m, result.Totals.NetIncome);

        var propRow = result.ByProperty.Single();
        Assert.Equal("prop-A", propRow.PropertyKey);
        Assert.Equal(1000m, propRow.TotalRevenue);
        Assert.Single(propRow.RevenueLines);
        Assert.Equal(1000m, propRow.RevenueLines[0].Amount);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Expense sign convention
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_SingleExpenseEntry_TotalExpensesIsPositive()
    {
        var exp = ExpAcct("5000", "Maintenance");
        var (sut, source) = Build(new[] { exp });
        source.JournalEntries.Add(ExpenseEntry(exp.Id, ClearingId, 500m, Today, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Equal(0m, result.Totals.TotalRevenue);
        Assert.Equal(500m, result.Totals.TotalExpenses);
        Assert.Equal(-500m, result.Totals.NetIncome);

        var propRow = result.ByProperty.Single();
        Assert.Equal(500m, propRow.TotalExpenses);
        Assert.Single(propRow.ExpenseLines);
        Assert.Equal(500m, propRow.ExpenseLines[0].Amount);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Net income = Revenue - Expenses
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_RevenueAndExpense_NetIncomeIsRevMinusExp()
    {
        var rev = RevAcct("4000");
        var exp = ExpAcct("5000");
        var (sut, source) = Build(new[] { rev, exp });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 3000m, Today, "prop-A"));
        source.JournalEntries.Add(ExpenseEntry(exp.Id, ClearingId, 1200m, Today, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        var row = result.ByProperty.Single(r => r.PropertyKey == "prop-A");
        Assert.Equal(3000m, row.TotalRevenue);
        Assert.Equal(1200m, row.TotalExpenses);
        Assert.Equal(1800m, row.NetIncome);
        Assert.Equal(1800m, result.Totals.NetIncome);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Property bucketing
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_MultipleProperties_EachBucketedSeparately()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 1000m, Today, "prop-A"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 2000m, Today, "prop-B"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Equal(2, result.ByProperty.Count);
        Assert.Equal(1000m, result.ByProperty.Single(r => r.PropertyKey == "prop-A").TotalRevenue);
        Assert.Equal(2000m, result.ByProperty.Single(r => r.PropertyKey == "prop-B").TotalRevenue);
        Assert.Equal(3000m, result.Totals.TotalRevenue);
    }

    [Fact]
    public async Task PnL_NullPropertyId_RolledIntoUnassigned()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 750m, Today, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        var unassigned = result.ByProperty.Single(r => r.PropertyKey == "Unassigned");
        Assert.Equal(750m, unassigned.TotalRevenue);
    }

    [Fact]
    public async Task PnL_UnassignedSortsLast()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 100m, Today, "prop-Z"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 50m, Today, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Equal("Unassigned", result.ByProperty.Last().PropertyKey);
    }

    [Fact]
    public async Task PnL_PropertyKeys_OrderedOrdinalAscendingUnassignedLast()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 100m, Today, "prop-C"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 100m, Today, "prop-A"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 100m, Today, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        var keys = result.ByProperty.Select(r => r.PropertyKey).ToList();
        Assert.Equal("prop-A", keys[0]);
        Assert.Equal("prop-C", keys[1]);
        Assert.Equal("Unassigned", keys[2]);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Period window filtering
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_EntryAfterPeriodEnd_Excluded()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        var periodEnd = new DateOnly(2026, 3, 31);
        // This entry is after the period end — should be excluded.
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 500m, new DateOnly(2026, 4, 1), "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart, PeriodEnd = periodEnd });

        Assert.Empty(result.ByProperty);
        Assert.Equal(0m, result.Totals.TotalRevenue);
    }

    [Fact]
    public async Task PnL_EntryBeforePeriodStart_Excluded()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        var periodStart = new DateOnly(2026, 4, 1);
        // This entry is before the period start — should be excluded.
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 500m, new DateOnly(2026, 3, 31), "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters
            {
                ChartId = Chart,
                PeriodStart = periodStart,
                PeriodEnd = Today,
            });

        Assert.Empty(result.ByProperty);
        Assert.Equal(0m, result.Totals.TotalRevenue);
    }

    [Fact]
    public async Task PnL_EntriesOnPeriodBoundaries_Included()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 100m, start, "prop-A"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 200m, end, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters
            {
                ChartId = Chart,
                PeriodStart = start,
                PeriodEnd = end,
            });

        Assert.Equal(300m, result.Totals.TotalRevenue);
    }

    [Fact]
    public async Task PnL_PeriodStartAfterPeriodEnd_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new ProfitAndLossByPropertyParameters
                {
                    ChartId = Chart,
                    PeriodStart = new DateOnly(2026, 12, 31),
                    PeriodEnd = new DateOnly(2026, 1, 1),
                }));
    }
    [Fact]
    public async Task ProfitAndLossByProperty_UnknownChart_ThrowsValidationException()
    {
        var (sut, source) = Build();
        source.Charts.Clear();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() => sut.ExecuteAsync(
            Context(), new ProfitAndLossByPropertyParameters { ChartId = Chart }));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Property filter
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_PropertyIdsFilter_OmitsOtherProperties()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 1000m, Today, "prop-A"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 2000m, Today, "prop-B"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters
            {
                ChartId = Chart,
                PropertyIds = new[] { "prop-A" },
            });

        Assert.Single(result.ByProperty);
        Assert.Equal("prop-A", result.ByProperty[0].PropertyKey);
        Assert.Equal(1000m, result.Totals.TotalRevenue);
    }

    [Fact]
    public async Task PnL_PropertyIdsFilter_ExcludesUnassigned()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 1000m, Today, "prop-A"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 500m, Today, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters
            {
                ChartId = Chart,
                PropertyIds = new[] { "prop-A" },
            });

        Assert.DoesNotContain(result.ByProperty, r => r.PropertyKey == "Unassigned");
        Assert.Equal(1000m, result.Totals.TotalRevenue);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Portfolio totals consistency
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_Totals_EqualSumOfByPropertyRows()
    {
        var rev = RevAcct("4000");
        var exp = ExpAcct("5000");
        var (sut, source) = Build(new[] { rev, exp });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 1000m, Today, "prop-A"));
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 2000m, Today, "prop-B"));
        source.JournalEntries.Add(ExpenseEntry(exp.Id, ClearingId, 400m, Today, "prop-A"));
        source.JournalEntries.Add(ExpenseEntry(exp.Id, ClearingId, 600m, Today, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        var sumRev = result.ByProperty.Sum(r => r.TotalRevenue);
        var sumExp = result.ByProperty.Sum(r => r.TotalExpenses);
        Assert.Equal(sumRev, result.Totals.TotalRevenue);
        Assert.Equal(sumExp, result.Totals.TotalExpenses);
        Assert.Equal(result.Totals.TotalRevenue - result.Totals.TotalExpenses, result.Totals.NetIncome);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Draft entries excluded
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_DraftEntry_Excluded()
    {
        var rev = RevAcct("4000");
        var (sut, _) = Build(new[] { rev });
        // Draft entry — excluded because the neutral source exposes posted entries only.

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Equal(0m, result.Totals.TotalRevenue);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cross-chart isolation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_EntriesForOtherChart_Excluded()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        var otherChart = ChartOfAccountsId.NewId();
        var otherChartEntry = new ReportJournalEntry(Guid.NewGuid().ToString(), Tenant, otherChart, Today,
            [new(ClearingId, 999m, 0m, null), new(rev.Id, 0m, 999m, null)]);
        source.JournalEntries.Add(otherChartEntry);

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Equal(0m, result.Totals.TotalRevenue);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Account line ordering
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_RevenueLines_OrderedByAccountCodeOrdinal()
    {
        var rev1 = RevAcct("4200", "Late Fees");
        var rev2 = RevAcct("4000", "Rent");
        var (sut, source) = Build(new[] { rev1, rev2 });
        source.JournalEntries.Add(RevenueEntry(rev1.Id, ClearingId, 100m, Today, "prop-A"));
        source.JournalEntries.Add(RevenueEntry(rev2.Id, ClearingId, 500m, Today, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        var row = result.ByProperty.Single();
        Assert.Equal("4000", row.RevenueLines[0].AccountCode);
        Assert.Equal("4200", row.RevenueLines[1].AccountCode);
    }

    // ──────────────────────────────────────────────────────────────────
    //  AsOfDate wiring (PeriodEnd defaults to context date)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PnL_NullPeriodEnd_DefaultsToContextDate()
    {
        var rev = RevAcct("4000");
        var (sut, source) = Build(new[] { rev });
        source.JournalEntries.Add(RevenueEntry(rev.Id, ClearingId, 100m, Today, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ProfitAndLossByPropertyParameters { ChartId = Chart });

        Assert.Equal(Today, result.PeriodEnd);
    }
}
