namespace Harborline.Foundation.DataExchange;

/// <summary>Admission for closed, exactly conserved run evidence. Durable stores must enforce this too.</summary>
public static class ExchangeRunClosure
{
    public static void ValidateOutcome(EffectTerminalOutcome outcome)
    {
        if (outcome is null || !Enum.IsDefined(outcome.Status))
            throw new DataExchangeCommitRefusedException("run.outcome_unknown", "Every effect requires a known terminal outcome.");
    }

    public static ExchangeCensus Census(IEnumerable<CommitEffectResult> effects)
    {
        var statuses = effects.Select(effect => effect.Outcome.Status).ToArray();
        return new(
            statuses.Count(status => status == ExchangeEffectStatus.Applied),
            statuses.Count(status => status == ExchangeEffectStatus.Skipped),
            statuses.Count(status => status == ExchangeEffectStatus.Conflicted),
            statuses.Count(status => status == ExchangeEffectStatus.Rejected),
            statuses.Count(status => status == ExchangeEffectStatus.Failed),
            statuses.Count(status => status == ExchangeEffectStatus.Halted));
    }

    public static void ValidateEffects(DryRunArtifact approved, BatchIdentity batch, IReadOnlyList<CommitEffectResult> effects)
    {
        foreach (var effect in effects) ValidateOutcome(effect.Outcome);
        var expected = approved.NormalizedEffects.Select(effect => ExchangeIdentity.DeriveEffect(batch,
            approved.Proposal.TargetContract, effect.SourceRecordIdentity, effect.SourceRecordVersion,
            effect.EffectDiscriminator)).ToHashSet();
        if (expected.Count != approved.NormalizedEffects.Count || effects.Count != expected.Count
            || effects.Select(effect => effect.EffectIdentity).Distinct().Count() != expected.Count
            || !expected.SetEquals(effects.Select(effect => effect.EffectIdentity)))
            throw new DataExchangeCommitRefusedException("run.census_incomplete", "Each reviewed effect must be accounted exactly once.");
        var approvedByIdentity = approved.NormalizedEffects.ToDictionary(
            effect => ExchangeIdentity.DeriveEffect(batch, approved.Proposal.TargetContract,
                effect.SourceRecordIdentity, effect.SourceRecordVersion, effect.EffectDiscriminator));
        if (effects.Any(result => !approvedByIdentity.TryGetValue(result.EffectIdentity, out var reviewed)
            || !EffectEquals(reviewed, result.Effect)))
            throw new DataExchangeCommitRefusedException("run.stale", "Commit effects must exactly match the reviewed effects.");
    }

    public static void Validate(DryRunArtifact approved, CommitRunArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(artifact);
        if (!Enum.IsDefined(artifact.TerminalStatus))
            throw new DataExchangeCommitRefusedException("run.terminal_unknown", "A run requires a known terminal state.");
        if (artifact.ApprovedDryRunId != approved.Id
            || artifact.BatchDerivation != BatchIdentityInputs.From(approved.TenantId, approved.Proposal)
            || artifact.BatchIdentity != ExchangeIdentity.DeriveBatch(artifact.BatchDerivation))
            throw new DataExchangeCommitRefusedException("run.stale", "Commit evidence must match the reviewed semantic inputs.");
        ValidateEffects(approved, artifact.BatchIdentity, artifact.Effects);
        var census = Census(artifact.Effects);
        if (artifact.Census != census || census.Accounted != approved.NormalizedEffects.Count)
            throw new DataExchangeCommitRefusedException("run.census_incomplete", "The census must conserve every reviewed effect.");
        var terminal = census.Halted > 0 ? ExchangeRunTerminalStatus.Halted
            : census.Applied == census.Accounted ? ExchangeRunTerminalStatus.Completed
            : ExchangeRunTerminalStatus.CompletedWithRefusals;
        if (artifact.TerminalStatus != terminal)
            throw new DataExchangeCommitRefusedException("run.terminal_inconsistent", "The terminal state must agree with the census.");
    }

    private static bool EffectEquals(ProposedEffect left, ProposedEffect right)
        => left.SourceOrdinal == right.SourceOrdinal
            && StringComparer.Ordinal.Equals(left.SourceRecordIdentity, right.SourceRecordIdentity)
            && StringComparer.Ordinal.Equals(left.SourceRecordVersion, right.SourceRecordVersion)
            && StringComparer.Ordinal.Equals(left.EffectDiscriminator, right.EffectDiscriminator)
            && StringComparer.Ordinal.Equals(left.BoundaryAfter, right.BoundaryAfter)
            && StringComparer.Ordinal.Equals(left.PayloadReference, right.PayloadReference)
            && left.Metadata.Count == right.Metadata.Count
            && left.Metadata.All(pair => right.Metadata.TryGetValue(pair.Key, out var value)
                && StringComparer.Ordinal.Equals(pair.Value, value));
}
