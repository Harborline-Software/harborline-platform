using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.BalanceSheet;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class BalanceSheetCartridgeTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-bs");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    private static ReportAccount Acct(string code, GLAccountType type, bool isActive = true)
        => new(GLAccountId.NewId(), Chart, code, $"Account {code}", type,
            type is GLAccountType.Asset or GLAccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit,
            isActive);

    /// <summary>
    /// Post a balanced journal entry (debit one account, credit another).
    /// Raw convention: debit = positive, credit = negative per IGeneralLedgerReadModel.
    /// </summary>
    private static ReportJournalEntry PostedEntry(DateOnly date, GLAccountId debit, GLAccountId credit, decimal amount)
        => new(Guid.NewGuid().ToString(), Tenant, Chart, date,
            [new(debit, amount, 0m, null), new(credit, 0m, amount, null)]);

    private static ReportChart MakeChart() => new(Chart, "Test", "USD", true);

    private static ReportExecutionContext Context()
        => new ReportExecutionContext(Tenant, "marker:bs:1", new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero), Principal);

    private static (BalanceSheetCartridge Cartridge, InMemoryReportQuerySource Source)
        Build(IEnumerable<ReportAccount>? seedAccounts = null)
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(MakeChart());
        source.Accounts.AddRange(seedAccounts ?? []);
        return (new BalanceSheetCartridge(source), source);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Parameter validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BalanceSheet_NeitherPeriodNorAsOfDate_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(), new BalanceSheetParameters { ChartId = Chart }));
    }

    [Fact]
    public async Task BalanceSheet_BothPeriodAndAsOfDate_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(), new BalanceSheetParameters
            {
                ChartId = Chart,
                FiscalPeriodId = FiscalPeriodId.NewId(),
                AsOfDate = new DateOnly(2026, 12, 31),
            }));
    }

    [Fact]
    public async Task BalanceSheet_UnknownChart_ThrowsValidationException()
    {
        var (sut, source) = Build();
        source.Charts.Clear();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) }));
    }

    [Fact]
    public async Task BalanceSheet_PeriodFromDifferentChart_ThrowsValidationException()
    {
        var (sut, source) = Build();
        var otherChart = ChartOfAccountsId.NewId();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), otherChart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.Open);
        source.FiscalPeriods.Add(period);

        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new BalanceSheetParameters { ChartId = Chart, FiscalPeriodId = period.Id }));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Core correctness
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BalanceSheet_EmptyChart_ReturnsZeroTotalsAndIsBalanced()
    {
        var (sut, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        Assert.Empty(result.Assets);
        Assert.Empty(result.Liabilities);
        Assert.Empty(result.Equity);
        Assert.Equal(0m, result.TotalAssets);
        Assert.Equal(0m, result.TotalLiabilities);
        Assert.Equal(0m, result.TotalEquity);
        Assert.True(result.IsBalanced, "Empty chart should satisfy A = L + E (all zero).");
    }

    [Fact]
    public async Task BalanceSheet_AccountingEquationBalances_WithTypicalEntry()
    {
        // Cash (Asset) debit, Equity credit. Classic initial capital contribution.
        var cash = Acct("1000", GLAccountType.Asset);
        var equity = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { cash, equity });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, equity.Id, 1000m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        Assert.Equal(1000m, result.TotalAssets);
        Assert.Equal(0m, result.TotalLiabilities);
        Assert.Equal(1000m, result.TotalEquity);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task BalanceSheet_AccountingEquationBalances_AssetsEqualLiabilitiesPlusEquity()
    {
        // Cash (Asset) +500, Loan (Liability) +500: Assets = Liabilities + Equity (0).
        var cash = Acct("1000", GLAccountType.Asset);
        var loan = Acct("2000", GLAccountType.Liability);
        var (sut, source) = Build(new[] { cash, loan });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, loan.Id, 500m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        Assert.Equal(500m, result.TotalAssets);
        Assert.Equal(500m, result.TotalLiabilities);
        Assert.Equal(0m, result.TotalEquity);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task BalanceSheet_ThreeSectionEquation_FullBalance()
    {
        // Cash (Asset) 1000; Loan (Liability) 600; Equity 400.
        // Debit Cash 1000, Credit Loan 600 + Credit Equity 400.
        var cash   = Acct("1000", GLAccountType.Asset);
        var loan   = Acct("2000", GLAccountType.Liability);
        var equity = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { cash, loan, equity });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, loan.Id, 600m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 2), cash.Id, equity.Id, 400m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        Assert.Equal(1000m, result.TotalAssets);
        Assert.Equal(600m, result.TotalLiabilities);
        Assert.Equal(400m, result.TotalEquity);
        Assert.True(result.IsBalanced);
        Assert.Equal(result.TotalAssets, result.TotalLiabilities + result.TotalEquity);
    }

    [Fact]
    public async Task BalanceSheet_AsOfDate_ExcludesEntriesAfterCutoff()
    {
        var cash   = Acct("1000", GLAccountType.Asset);
        var equity = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { cash, equity });
        // Before as-of: included.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, equity.Id, 500m));
        // After as-of: excluded.
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 7, 1), cash.Id, equity.Id, 999m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 6, 30) });

        Assert.Equal(500m, result.TotalAssets);
    }

    [Fact]
    public async Task BalanceSheet_RevenueAndExpenseAccounts_ExcludedFromSections()
    {
        // Revenue/Expense accounts must NOT appear in balance sheet sections.
        var cash    = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var expense = Acct("5000", GLAccountType.Expense);
        var (sut, source) = Build(new[] { cash, revenue, expense });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31),
                IncludeZeroBalanceAccounts = true });

        Assert.DoesNotContain(result.Assets, l => l.AccountId == revenue.Id);
        Assert.DoesNotContain(result.Assets, l => l.AccountId == expense.Id);
        Assert.DoesNotContain(result.Liabilities, l => l.AccountId == revenue.Id);
        Assert.DoesNotContain(result.Equity, l => l.AccountId == revenue.Id);
    }

    [Fact]
    public async Task BalanceSheet_IncludeZeroFalse_OmitsZeroBalanceAccounts()
    {
        var cash   = Acct("1000", GLAccountType.Asset);
        var zero   = Acct("1001", GLAccountType.Asset);
        var equity = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { cash, zero, equity });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, equity.Id, 200m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31),
                IncludeZeroBalanceAccounts = false });

        Assert.DoesNotContain(result.Assets, l => l.AccountId == zero.Id);
    }

    [Fact]
    public async Task BalanceSheet_IncludeZeroTrue_IncludesZeroBalanceAccounts()
    {
        var cash   = Acct("1000", GLAccountType.Asset);
        var zero   = Acct("1001", GLAccountType.Asset);
        var equity = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { cash, zero, equity });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, equity.Id, 200m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31),
                IncludeZeroBalanceAccounts = true });

        Assert.Contains(result.Assets, l => l.AccountId == zero.Id);
    }

    [Fact]
    public async Task BalanceSheet_IncludeInactiveFalse_OmitsInactiveAccounts()
    {
        var active   = Acct("1000", GLAccountType.Asset);
        var inactive = Acct("1001", GLAccountType.Asset, isActive: false);
        var equity   = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { active, inactive, equity });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), active.Id, equity.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31),
                IncludeZeroBalanceAccounts = true });

        Assert.DoesNotContain(result.Assets, l => l.AccountId == inactive.Id);
    }

    [Fact]
    public async Task BalanceSheet_IncludeInactiveTrue_IncludesInactiveAccounts()
    {
        var active   = Acct("1000", GLAccountType.Asset);
        var inactive = Acct("1001", GLAccountType.Asset, isActive: false);
        var equity   = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { active, inactive, equity });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), active.Id, equity.Id, 100m));

        // IncludeZeroBalanceAccounts = true required because 1001 has no entries;
        // without it the cartridge filters it out before the active/inactive check applies.
        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31),
                IncludeInactiveAccounts = true, IncludeZeroBalanceAccounts = true });

        Assert.Contains(result.Assets, l => l.AccountId == inactive.Id);
    }

    [Fact]
    public async Task BalanceSheet_Lines_AreOrderedByCodeThenIdStable()
    {
        var a2 = Acct("2000", GLAccountType.Asset);
        var a1 = Acct("1000", GLAccountType.Asset);
        var equity = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { a2, a1, equity });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), a1.Id, equity.Id, 100m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 2), a2.Id, equity.Id, 200m));

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        var codes = result.Assets.Select(l => l.AccountCode).ToArray();
        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal).ToArray(), codes);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Provisionality
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BalanceSheet_PeriodLocked_IsProvisionalFalse()
    {
        var (sut, source) = Build();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), Chart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.Locked);
        source.FiscalPeriods.Add(period);

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, FiscalPeriodId = period.Id });

        Assert.False(result.IsProvisional);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task BalanceSheet_PeriodOpen_IsProvisionalTrue_WithWarning()
    {
        var (sut, source) = Build();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), Chart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.Open);
        source.FiscalPeriods.Add(period);

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, FiscalPeriodId = period.Id });

        Assert.True(result.IsProvisional);
        Assert.Contains(result.Warnings, w => w.Contains("Open"));
    }
    [Fact]
    public async Task BalanceSheet_PeriodSoftClosed_IsProvisionalTrue_WithWarning()
    {
        var (sut, source) = Build();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), Chart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.SoftClosed);
        source.FiscalPeriods.Add(period);
        var result = await sut.ExecuteAsync(Context(), new BalanceSheetParameters
            { ChartId = Chart, FiscalPeriodId = period.Id });
        Assert.True(result.IsProvisional);
        Assert.Contains(result.Warnings, w => w.Contains("SoftClosed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BalanceSheet_AsOfDateWithoutPeriod_IsProvisionalFalse()
    {
        var (sut, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 5, 31) });
        Assert.False(result.IsProvisional);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Unbalanced chart detection
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BalanceSheet_UnbalancedChart_IsBalancedFalseWithWarning()
    {
        // Force imbalance: one-sided debit to Asset with no offsetting credit entry.
        // We do this by posting to an account that is in our chart but whose
        // counterpart is NOT in the chart (so only one side shows up).
        var cash     = Acct("1000", GLAccountType.Asset);
        var external = GLAccountId.NewId(); // not in chart's account list
        var (sut, source) = Build(new[] { cash });

        // Build a "fake" entry that debits cash but credits an account not in this chart.
        var entry = new ReportJournalEntry(Guid.NewGuid().ToString(), Tenant, Chart,
            new DateOnly(2026, 5, 1),
            [new(cash.Id, 100m, 0m, null), new(external, 0m, 100m, null)]);
        source.JournalEntries.Add(entry);

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        // Assets = 100; Liabilities = 0; Equity = 0 => unbalanced.
        Assert.False(result.IsBalanced);
        Assert.Contains(result.Warnings, w => w.Contains("unbalanced") || w.Contains("Accounting equation"));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Tenant isolation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BalanceSheet_TenantIsolation_ExcludesOtherTenantEntries()
    {
        var cash    = Acct("1000", GLAccountType.Asset);
        var equity  = Acct("3000", GLAccountType.Equity);
        var (sut, source) = Build(new[] { cash, equity });

        var otherTenant = new TenantId("tenant-other");

        // Our tenant's entry.
        source.JournalEntries.Add(
            PostedEntry(new DateOnly(2026, 5, 1), cash.Id, equity.Id, 500m));

        // Other tenant's entry (same account IDs by coincidence).
        var otherEntry = new ReportJournalEntry(Guid.NewGuid().ToString(), otherTenant, Chart,
            new DateOnly(2026, 5, 1),
            [new(cash.Id, 9999m, 0m, null), new(equity.Id, 0m, 9999m, null)]);
        source.JournalEntries.Add(otherEntry);

        var result = await sut.ExecuteAsync(Context(),
            new BalanceSheetParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        // Only our tenant's 500 should appear.
        Assert.Equal(500m, result.TotalAssets);
        Assert.Equal(500m, result.TotalEquity);
    }

    // ──────────────────────────────────────────────────────────────────
    //  ReportKind
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BalanceSheetCartridge_KindIsBalanceSheet()
    {
        var (sut, _) = Build();
        Assert.Equal(ReportKind.BalanceSheet, sut.Kind);
    }
}
