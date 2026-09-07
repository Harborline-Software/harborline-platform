using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.TrialBalance;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 2 — determinism assertions for the Trial Balance cartridge.
/// Implements the report-cartridge determinism convention directly because <see cref="TrialBalanceResult"/>
/// carries <c>IReadOnlyList&lt;TrialBalanceRow&gt;</c> and
/// <c>IReadOnlyList&lt;string&gt;</c> properties whose reference-based
/// equality breaks C# record structural equality. Per-field assertions
/// preserve the invariant without modifying the result type.
/// </summary>
public sealed class TrialBalanceDeterminismTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-det");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);

    private static ReportChart MakeChart()
        => new(Chart, "Det", "USD", true);

    private static (TrialBalanceCartridge Cartridge, InMemoryReportQuerySource Source) Build()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(MakeChart());
        var cashA = new ReportAccount(GLAccountId.NewId(), Chart, "1000", "Cash A",
            GLAccountType.Asset, NormalBalance.Debit, true);
        var cashB = new ReportAccount(GLAccountId.NewId(), Chart, "1000", "Cash B",
            GLAccountType.Asset, NormalBalance.Debit, true);
        var revenue = new ReportAccount(GLAccountId.NewId(), Chart, "4000", "Revenue",
            GLAccountType.Revenue, NormalBalance.Credit, true);
        source.Accounts.AddRange([cashB, revenue, cashA]);
        source.JournalEntries.AddRange([
            new ReportJournalEntry("det-1", Tenant, Chart, new System.DateOnly(2026, 5, 1),
                [new(cashA.Id, 100m, 0m, null), new(revenue.Id, 0m, 100m, null)]),
            new ReportJournalEntry("det-2", Tenant, Chart, new System.DateOnly(2026, 5, 2),
                [new(cashB.Id, 50m, 0m, null), new(revenue.Id, 0m, 50m, null)]),
        ]);
        return (new TrialBalanceCartridge(source), source);
    }

    private static TrialBalanceParameters Parameters()
        => new TrialBalanceParameters
        {
            ChartId = Chart,
            AsOfDate = new System.DateOnly(2026, 12, 31),
            IncludeZeroBalanceAccounts = true,
        };

    private static ReportExecutionContext Context()
        => new ReportExecutionContext(Tenant, "marker:det:1",
            new System.DateTimeOffset(2026, 5, 17, 12, 0, 0, System.TimeSpan.Zero), Principal);

    private static void AssertResultsEqual(TrialBalanceResult r1, TrialBalanceResult r2)
    {
        Assert.Equal(r1.ChartId, r2.ChartId);        Assert.NotEmpty(r1.Rows);
        Assert.Equal(r1.AsOf, r2.AsOf);
        Assert.Equal(r1.PeriodId, r2.PeriodId);
        Assert.Equal(r1.TotalDebit, r2.TotalDebit);
        Assert.Equal(r1.TotalCredit, r2.TotalCredit);
        Assert.Equal(r1.IsBalanced, r2.IsBalanced);
        Assert.Equal(r1.IsProvisional, r2.IsProvisional);
        Assert.Equal(r1.Warnings, r2.Warnings, System.StringComparer.Ordinal);
        Assert.Equal(r1.Rows.Count, r2.Rows.Count);
        for (var i = 0; i < r1.Rows.Count; i++)
            Assert.Equal(r1.Rows[i], r2.Rows[i]);  // TrialBalanceRow is a simple record (no collection props)
    }

    [Fact]
    public async Task ExecuteAsync_IsDeterministic_AcrossRepeatedRuns()
    {
        var (sut, _) = Build();
        var ctx = Context();
        var p = Parameters();
        var r1 = await sut.ExecuteAsync(ctx, p);
        var r2 = await sut.ExecuteAsync(ctx, p);
        AssertResultsEqual(r1, r2);
    }

    [Fact]
    public async Task ExecuteAsync_SameMarker_SameResult()
    {
        // Documentation test: two *distinct* contexts that share the same snapshot marker
        // MUST produce equal results (the marker is the sole upstream-state input).
        var (sut, source) = Build();
        var p = Parameters();
        var ctx1 = Context();
        // ctx2 is a fresh instance but carries the same snapshot marker value.
        var ctx2 = new ReportExecutionContext(Tenant, ctx1.SnapshotMarker,
            new System.DateTimeOffset(2026, 5, 17, 12, 0, 0, System.TimeSpan.Zero), Principal);
        var r1 = await sut.ExecuteAsync(ctx1, p);
        var r2 = await sut.ExecuteAsync(ctx2, p);
        AssertResultsEqual(r1, r2);        Assert.Equal([ctx1.SnapshotMarker, ctx1.SnapshotMarker], source.ObservedJournalMarkers);
    }
}
