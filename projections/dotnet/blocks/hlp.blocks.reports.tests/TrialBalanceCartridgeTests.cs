using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.TrialBalance;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class TrialBalanceCartridgeTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-tb");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    private static ReportAccount Acct(string code, GLAccountType type, bool isActive = true)
        => new(GLAccountId.NewId(), Chart, code, $"Account {code}", type,
            type is GLAccountType.Asset or GLAccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit,
            isActive);

    private static ReportJournalEntry PostedEntry(DateOnly date, GLAccountId debit, GLAccountId credit, decimal amount)
        => new(Guid.NewGuid().ToString(), Tenant, Chart, date,
            [new(debit, amount, 0m, null), new(credit, 0m, amount, null)]);

    private static ReportChart MakeChart() => new(Chart, "Test", "USD", true);

    private static ReportExecutionContext Context()
        => new ReportExecutionContext(Tenant, "marker:1", new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero), Principal);

    private static (TrialBalanceCartridge Cartridge, InMemoryReportQuerySource Source)
        Build(IEnumerable<ReportAccount>? seedAccounts = null)
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(MakeChart());
        source.Accounts.AddRange(seedAccounts ?? []);
        return (new TrialBalanceCartridge(source), source);
    }

    [Fact]
    public async Task TrialBalance_EmptyChart_ReturnsZeroTotalsAndEmptyRows()
    {
        var (sut, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });
        Assert.Empty(result.Rows);
        Assert.Equal(0m, result.TotalDebit);
        Assert.Equal(0m, result.TotalCredit);
        Assert.True(result.IsBalanced);
    }
    [Fact]
    public async Task TrialBalance_ForwardsSnapshotMarkerVerbatim()
    {
        var (sut, source) = Build();
        await sut.ExecuteAsync(Context(), new TrialBalanceParameters
            { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });
        Assert.Equal(["marker:1"], source.ObservedJournalMarkers);
    }

    [Fact]
    public async Task TrialBalance_SingleAccountWithBalance_AppearsInDebitColumn()
    {
        var cash = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revenue });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        var cashRow = result.Rows.Single(r => r.AccountId == cash.Id);
        Assert.Equal(100m, cashRow.DebitBalance);
        Assert.Equal(0m, cashRow.CreditBalance);
        var revRow = result.Rows.Single(r => r.AccountId == revenue.Id);
        Assert.Equal(0m, revRow.DebitBalance);
        Assert.Equal(100m, revRow.CreditBalance);   // Revenue is credit-normal; raw is -100 → projects to credit 100
    }

    [Fact]
    public async Task TrialBalance_BalancedChart_IsBalancedTrue()
    {
        var cash = Acct("1000", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { cash, revenue });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), cash.Id, revenue.Id, 100m));
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 15), cash.Id, revenue.Id, 50m));

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });

        Assert.True(result.IsBalanced);
        Assert.Equal(result.TotalDebit, result.TotalCredit);
        Assert.Equal(150m, result.TotalDebit);
    }
    [Fact]
    public async Task TrialBalance_OneSidedEntry_IsUnbalancedWithWarning()
    {
        var cash = Acct("1000", GLAccountType.Asset);
        var (sut, source) = Build([cash]);
        source.JournalEntries.Add(new ReportJournalEntry("one-sided", Tenant, Chart,
            new DateOnly(2026, 5, 1), [new(cash.Id, 100m, 0m, null)]));
        var result = await sut.ExecuteAsync(Context(), new TrialBalanceParameters
            { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) });
        Assert.False(result.IsBalanced);
        Assert.Equal(100m, result.TotalDebit);
        Assert.Equal(0m, result.TotalCredit);
        Assert.Contains(result.Warnings, w => w.Contains("Chart is unbalanced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrialBalance_PeriodSoftClosed_IsProvisionalTrue_WithWarning()
    {
        var (sut, source) = Build();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), Chart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.SoftClosed);
        source.FiscalPeriods.Add(period);
        var result = await sut.ExecuteAsync(Context(), new TrialBalanceParameters
            { ChartId = Chart, FiscalPeriodId = period.Id });
        Assert.True(result.IsProvisional);
        Assert.Contains(result.Warnings, w => w.Contains("SoftClosed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrialBalance_NeitherPeriodNorAsOfDate_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(), new TrialBalanceParameters { ChartId = Chart }));
    }

    [Fact]
    public async Task TrialBalance_BothPeriodAndAsOfDate_ThrowsValidationException()
    {
        var (sut, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(), new TrialBalanceParameters
            {
                ChartId = Chart,
                FiscalPeriodId = FiscalPeriodId.NewId(),
                AsOfDate = new DateOnly(2026, 12, 31),
            }));
    }

    [Fact]
    public async Task TrialBalance_UnknownChart_ThrowsValidationException()
    {
        var (sut, source) = Build();
        source.Charts.Clear();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31) }));
    }

    [Fact]
    public async Task TrialBalance_IncludeInactiveFalse_OmitsInactiveAccounts()
    {
        var active = Acct("1000", GLAccountType.Asset);
        var inactive = Acct("1001", GLAccountType.Asset, isActive: false);
        var (sut, source) = Build(new[] { active, inactive });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), active.Id, inactive.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31), IncludeZeroBalanceAccounts = true });

        Assert.DoesNotContain(result.Rows, r => r.AccountId == inactive.Id);
    }

    [Fact]
    public async Task TrialBalance_IncludeInactiveTrue_IncludesInactiveAccounts()
    {
        var active = Acct("1000", GLAccountType.Asset);
        var inactive = Acct("1001", GLAccountType.Asset, isActive: false);
        var (sut, source) = Build(new[] { active, inactive });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), active.Id, inactive.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31), IncludeInactiveAccounts = true });

        Assert.Contains(result.Rows, r => r.AccountId == inactive.Id);
    }

    [Fact]
    public async Task TrialBalance_IncludeZeroFalse_OmitsZeroBalanceAccounts()
    {
        var withBalance = Acct("1000", GLAccountType.Asset);
        var zero = Acct("1001", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { withBalance, zero, revenue });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), withBalance.Id, revenue.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31), IncludeZeroBalanceAccounts = false });

        Assert.DoesNotContain(result.Rows, r => r.AccountId == zero.Id);
    }

    [Fact]
    public async Task TrialBalance_IncludeZeroTrue_IncludesZeroBalanceAccounts()
    {
        var withBalance = Acct("1000", GLAccountType.Asset);
        var zero = Acct("1001", GLAccountType.Asset);
        var revenue = Acct("4000", GLAccountType.Revenue);
        var (sut, source) = Build(new[] { withBalance, zero, revenue });
        source.JournalEntries.Add(PostedEntry(new DateOnly(2026, 5, 1), withBalance.Id, revenue.Id, 100m));

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31), IncludeZeroBalanceAccounts = true });

        Assert.Contains(result.Rows, r => r.AccountId == zero.Id);
    }

    [Fact]
    public async Task TrialBalance_PeriodLocked_IsProvisionalFalse()
    {
        var (sut, source) = Build();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), Chart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.Locked);
        source.FiscalPeriods.Add(period);

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, FiscalPeriodId = period.Id });

        Assert.False(result.IsProvisional);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task TrialBalance_PeriodOpen_IsProvisionalTrue_WithWarning()
    {
        var (sut, source) = Build();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), Chart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.Open);
        source.FiscalPeriods.Add(period);

        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, FiscalPeriodId = period.Id });

        Assert.True(result.IsProvisional);
        Assert.Contains(result.Warnings, w => w.Contains("Open"));
    }

    [Fact]
    public async Task TrialBalance_AsOfDateWithoutPeriod_IsProvisionalFalse()
    {
        var (sut, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 5, 31) });
        Assert.False(result.IsProvisional);
    }

    [Fact]
    public async Task TrialBalance_Rows_AreOrderedByCodeThenIdStable()
    {
        var a = Acct("1000", GLAccountType.Asset);
        var b = Acct("1000", GLAccountType.Asset);
        var c = Acct("4000", GLAccountType.Revenue);
        var seeded = new[] { c, b, a };
        var (sut, _) = Build(seeded);

        var result = await sut.ExecuteAsync(Context(), new TrialBalanceParameters
            { ChartId = Chart, AsOfDate = new DateOnly(2026, 12, 31), IncludeZeroBalanceAccounts = true });
        var expected = seeded.OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Id.ToString(), StringComparer.Ordinal).Select(x => x.Id).ToArray();
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(expected, result.Rows.Select(r => r.AccountId));
    }

    [Fact]
    public async Task TrialBalance_PeriodFromDifferentChart_ThrowsValidationException()
    {
        var (sut, source) = Build();
        var otherChart = ChartOfAccountsId.NewId();
        var period = new ReportFiscalPeriod(FiscalPeriodId.NewId(), otherChart, "2026-05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), FiscalPeriodStatus.Open);
        source.FiscalPeriods.Add(period);

        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new TrialBalanceParameters { ChartId = Chart, FiscalPeriodId = period.Id }));
    }
}
