using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0135 slice 2 — D5/D7 effective-dated threshold decision table. Asserts row matching, as-of-business-
/// time version resolution (used at instantiation to choose the pinned version), and pinned-version
/// evaluation (replay determinism — D7).
/// </summary>
public sealed class ThresholdDecisionTableTests
{
    private static ThresholdDecisionTable Table() => new(new[]
    {
        new ThresholdDecisionTableVersion
        {
            Version = "2026-06-23.1",
            EffectiveFrom = new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero),
            Rows = new[]
            {
                new ThresholdDecisionRow("under-5k", 0m, ApprovalDecision.AutoApprove),
                new ThresholdDecisionRow("over-5k", 5000.01m, ApprovalDecision.RequireApproval),
            },
        },
        new ThresholdDecisionTableVersion
        {
            Version = "2026-09-01.1",
            EffectiveFrom = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Rows = new[]
            {
                new ThresholdDecisionRow("under-10k", 0m, ApprovalDecision.AutoApprove),
                new ThresholdDecisionRow("over-10k", 10000.01m, ApprovalDecision.RequireApproval),
            },
        },
    });

    [Theory(DisplayName = "Decision table: highest-floor matching row fires; the exact boundary auto-approves")]
    [InlineData(1000, "under-5k", ApprovalDecision.AutoApprove)]
    [InlineData(5000.00, "under-5k", ApprovalDecision.AutoApprove)]  // $5000 exactly is under the > $5k gate
    [InlineData(5000.01, "over-5k", ApprovalDecision.RequireApproval)]
    [InlineData(7500, "over-5k", ApprovalDecision.RequireApproval)]
    public void Evaluate_MatchesHighestFloorRow(decimal amount, string expectedRow, ApprovalDecision expected)
    {
        var result = Table().EvaluatePinned("2026-06-23.1", amount);
        Assert.Equal(expectedRow, result.RowId);
        Assert.Equal(expected, result.Decision);
        Assert.Equal("2026-06-23.1", result.Version);
        Assert.Equal(amount, result.EvaluatedAmount);
    }

    [Fact(DisplayName = "Decision table: ResolveVersion picks the version effective as-of the business time (used at instantiation to choose the pinned version)")]
    public void ResolveVersion_PicksEffectiveAsOf()
    {
        var table = Table();
        Assert.Equal("2026-06-23.1", table.ResolveVersion(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)).Version);
        Assert.Equal("2026-09-01.1", table.ResolveVersion(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)).Version);
    }

    [Fact(DisplayName = "Decision table (D7): EvaluatePinned resolves against the PINNED version, not the newest — $7.5k auto-approves under the pinned 10k version")]
    public void EvaluatePinned_UsesPinnedVersion_NotNewest()
    {
        var table = Table();
        // $7.5k requires approval under .1 (5k) but auto-approves under the 10k version. Pinning to the 10k
        // version makes it auto-approve — deterministic replay against the pinned version (D7).
        Assert.Equal(ApprovalDecision.RequireApproval, table.EvaluatePinned("2026-06-23.1", 7500m).Decision);
        Assert.Equal(ApprovalDecision.AutoApprove, table.EvaluatePinned("2026-09-01.1", 7500m).Decision);
    }

    [Fact(DisplayName = "Decision table (D7): evaluating an UNKNOWN pinned version throws — a replay cannot deterministically re-resolve against a missing version")]
    public void EvaluatePinned_UnknownVersion_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Table().EvaluatePinned("nope.9", 1000m));
        Assert.Contains("nope.9", ex.Message);
    }

    [Fact(DisplayName = "Decision table: ResolveVersion before the earliest EffectiveFrom throws (no version effective yet)")]
    public void ResolveVersion_BeforeEarliest_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => Table().ResolveVersion(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
}
