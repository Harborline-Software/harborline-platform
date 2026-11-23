using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class ExchangeTerminalCensusProofTests
{
    [Theory(DisplayName = "data-exchange-eng-8 data-exchange-eng-9 data-exchange-run-2 data-exchange-run-3 census counts each terminal arm")]
    [InlineData(false)]
    [InlineData(true)]
    public void Census_counts_one_of_each_terminal_arm(bool reverse)
    {
        CommitEffectResult[] effects =
        [
            Effect(0, new(ExchangeEffectStatus.Applied, "effect.applied")),
            Effect(1, new(ExchangeEffectStatus.Skipped, "effect.skipped")),
            Effect(2, new(ExchangeEffectStatus.Conflicted, "effect.conflicted")),
            Effect(3, new(ExchangeEffectStatus.Rejected, "effect.rejected")),
            Effect(4, new(ExchangeEffectStatus.Failed, "effect.failed")),
            Effect(5, new(ExchangeEffectStatus.Halted, "effect.halted")),
        ];

        var census = ExchangeRunClosure.Census(reverse ? effects.Reverse() : effects);

        Assert.Equal(1, census.Applied);
        Assert.Equal(1, census.Skipped);
        Assert.Equal(1, census.Conflicted);
        Assert.Equal(1, census.Rejected);
        Assert.Equal(1, census.Failed);
        Assert.Equal(1, census.Halted);
        Assert.Equal(6, census.Accounted);
    }

    [Theory(DisplayName = "data-exchange-eng-8 data-exchange-eng-9 data-exchange-run-2 data-exchange-run-3 census preserves distinct asymmetric counts")]
    [InlineData(false)]
    [InlineData(true)]
    public void Census_preserves_asymmetric_named_counts(bool reverse)
    {
        CommitEffectResult[] effects =
        [
            Effect(0, new(ExchangeEffectStatus.Applied, "effect.applied")),
            Effect(1, new(ExchangeEffectStatus.Applied, "effect.applied")),
            Effect(2, new(ExchangeEffectStatus.Skipped, "effect.skipped")),
            Effect(3, new(ExchangeEffectStatus.Conflicted, "effect.conflicted")),
            Effect(4, new(ExchangeEffectStatus.Conflicted, "effect.conflicted")),
            Effect(5, new(ExchangeEffectStatus.Conflicted, "effect.conflicted")),
            Effect(6, new(ExchangeEffectStatus.Failed, "effect.failed")),
            Effect(7, new(ExchangeEffectStatus.Failed, "effect.failed")),
            Effect(8, new(ExchangeEffectStatus.Failed, "effect.failed")),
            Effect(9, new(ExchangeEffectStatus.Failed, "effect.failed")),
            Effect(10, new(ExchangeEffectStatus.Halted, "effect.halted")),
            Effect(11, new(ExchangeEffectStatus.Halted, "effect.halted")),
            Effect(12, new(ExchangeEffectStatus.Halted, "effect.halted")),
            Effect(13, new(ExchangeEffectStatus.Halted, "effect.halted")),
            Effect(14, new(ExchangeEffectStatus.Halted, "effect.halted")),
        ];

        var census = ExchangeRunClosure.Census(reverse ? effects.Reverse() : effects);

        Assert.Equal(2, census.Applied);
        Assert.Equal(1, census.Skipped);
        Assert.Equal(3, census.Conflicted);
        Assert.Equal(0, census.Rejected);
        Assert.Equal(4, census.Failed);
        Assert.Equal(5, census.Halted);
        Assert.Equal(15, census.Accounted);
    }

    [Theory(DisplayName = "data-exchange-eng-9 data-exchange-run-3 census distinguishes completion, partial completion and halt")]
    [InlineData(3, 0, 0, 0, 0, 0, ExchangeRunTerminalStatus.Completed)]
    [InlineData(2, 0, 0, 1, 0, 0, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(0, 0, 1, 0, 0, 0, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(0, 0, 0, 1, 0, 0, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(0, 0, 0, 0, 1, 0, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(2, 0, 0, 0, 0, 1, ExchangeRunTerminalStatus.Halted)]
    [InlineData(1, 1, 1, 1, 1, 1, ExchangeRunTerminalStatus.Halted)]
    public void Census_terminal_status_preserves_partial_completion_and_halt(
        int applied, int skipped, int conflicted, int rejected, int failed, int halted,
        ExchangeRunTerminalStatus expected)
    {
        var census = new ExchangeCensus(applied, skipped, conflicted, rejected, failed, halted);

        Assert.Equal(expected, ExchangeRunClosure.TerminalStatus(census));
    }

    [Theory(DisplayName = "data-exchange-eng-8 data-exchange-run-2 census outcome validation accepts each known arm")]
    [InlineData(ExchangeEffectStatus.Applied)]
    [InlineData(ExchangeEffectStatus.Skipped)]
    [InlineData(ExchangeEffectStatus.Conflicted)]
    [InlineData(ExchangeEffectStatus.Rejected)]
    [InlineData(ExchangeEffectStatus.Failed)]
    [InlineData(ExchangeEffectStatus.Halted)]
    public void Census_outcome_validation_accepts_each_known_arm(ExchangeEffectStatus status)
    {
        Assert.Null(Record.Exception(() => ExchangeRunClosure.ValidateOutcome(new(status, "literal.outcome"))));
    }

    [Fact(DisplayName = "data-exchange-eng-8 data-exchange-run-2 census outcome validation refuses null")]
    public void Census_outcome_validation_refuses_null()
    {
        var refusal = Assert.Throws<DataExchangeCommitRefusedException>(
            () => ExchangeRunClosure.ValidateOutcome(null!));

        Assert.Equal("run.outcome_unknown", refusal.Code);
        Assert.Null(Record.Exception(() => ExchangeRunClosure.ValidateOutcome(
            new(ExchangeEffectStatus.Applied, "literal.outcome"))));
    }

    [Fact(DisplayName = "data-exchange-eng-8 data-exchange-run-2 census outcome validation refuses unknown numeric status until corrected")]
    public void Census_outcome_validation_refuses_unknown_numeric_status_until_corrected()
    {
        var outcome = new EffectTerminalOutcome((ExchangeEffectStatus)999, "literal.outcome");

        var refusal = Assert.Throws<DataExchangeCommitRefusedException>(
            () => ExchangeRunClosure.ValidateOutcome(outcome));

        Assert.Equal("run.outcome_unknown", refusal.Code);
        Assert.Null(Record.Exception(() => ExchangeRunClosure.ValidateOutcome(
            outcome with { Status = ExchangeEffectStatus.Applied })));
    }

    private static CommitEffectResult Effect(int ordinal, EffectTerminalOutcome outcome)
        => new(
            new ProposedEffect(ordinal, $"source-{ordinal}", "v1", "canonical-record",
                $"after-{ordinal}", new Dictionary<string, string>()),
            new EffectIdempotencyIdentity($"census-effect-{ordinal}"), outcome, false);
}
