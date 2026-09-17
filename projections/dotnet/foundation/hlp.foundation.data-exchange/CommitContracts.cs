using System.Text.Json;

namespace Harborline.Foundation.DataExchange;

public enum ExchangeEffectStatus
{
    Applied,
    Skipped,
    Conflicted,
    Rejected,
    Failed,
    Halted,
}

public sealed record EffectTerminalOutcome(ExchangeEffectStatus Status, string Code);

public sealed record EffectLedgerEntry(
    BatchIdentity BatchIdentity,
    EffectIdempotencyIdentity EffectIdentity,
    AttemptId AttemptId,
    EffectTerminalOutcome Outcome,
    DateTimeOffset RecordedAt);

public enum ExchangeRunTerminalStatus
{
    Completed,
    CompletedWithRefusals,
    Halted,
}

public sealed record ExchangeCensus(
    int Applied,
    int Skipped,
    int Conflicted,
    int Rejected,
    int Failed,
    int Halted)
{
    public int Accounted => Applied + Skipped + Conflicted + Rejected + Failed + Halted;
}

public sealed record CommitEffectResult(
    ProposedEffect Effect,
    EffectIdempotencyIdentity EffectIdentity,
    EffectTerminalOutcome Outcome,
    bool ReplayedFromLedger);

public sealed record CommitRunArtifact(
    CommitRunId Id,
    DryRunId ApprovedDryRunId,
    BatchIdentity BatchIdentity,
    BatchIdentityInputs BatchDerivation,
    DateTimeOffset RequestedAt,
    IReadOnlyList<CommitEffectResult> Effects,
    ExchangeCensus Census,
    ExchangeRunTerminalStatus TerminalStatus,
    string? DurableCheckpoint,
    string RetentionClass,
    DateTimeOffset RetainUntil,
    bool LegalHold);

public sealed record AcknowledgementPolicy(IReadOnlySet<ExchangeEffectStatus> SafeOutcomes);

public sealed record CommitBounds
{
    public CommitBounds(int maxWindowSize, int maxConcurrency, int maxCommandBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWindowSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCommandBytes);
        MaxWindowSize = maxWindowSize;
        MaxConcurrency = maxConcurrency;
        MaxCommandBytes = maxCommandBytes;
    }

    public int MaxWindowSize { get; }

    public int MaxConcurrency { get; }

    public int MaxCommandBytes { get; }
}

public sealed record CanonicalRecordsCommand(
    CommitRunId CommitRunId,
    BatchIdentity BatchIdentity,
    EffectIdempotencyIdentity EffectIdentity,
    AttemptId AttemptId,
    string TargetContract,
    ProposedEffect Effect);

public sealed class DataExchangeCommitRefusedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IExchangeCommitAuthority
{
    ValueTask<bool> CanCommitAsync(
        DryRunArtifact dryRun,
        CancellationToken cancellationToken = default);
}

public interface ITargetAccessGate
{
    ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default);
}

public interface ICanonicalRecordsCommandPort
{
    ValueTask<EffectTerminalOutcome> ApplyAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default);
}

public interface IEffectOutcomeStore
{
    ValueTask<EffectLedgerEntry?> GetAsync(
        EffectIdempotencyIdentity identity,
        CancellationToken cancellationToken = default);

    ValueTask RecordAsync(EffectLedgerEntry entry, CancellationToken cancellationToken = default);
}

public interface IAcquisitionCheckpointStore
{
    ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default);

    ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string checkpoint,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryEffectOutcomeStore : IEffectOutcomeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<EffectIdempotencyIdentity, EffectLedgerEntry> _entries = [];

    public ValueTask<EffectLedgerEntry?> GetAsync(
        EffectIdempotencyIdentity identity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_entries.GetValueOrDefault(identity));
        }
    }

    public ValueTask RecordAsync(EffectLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _entries[entry.EffectIdentity] = entry;
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class InMemoryAcquisitionCheckpointStore : IAcquisitionCheckpointStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string TenantId, string DefinitionId), string> _checkpoints = [];

    public ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_checkpoints.GetValueOrDefault((tenantId, definitionId)));
        }
    }

    public ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string checkpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _checkpoints[(tenantId, definitionId)] = checkpoint;
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Promotes one reviewed proposal through bounded canonical Records commands.</summary>
public sealed class DataExchangeCommitter(
    IExchangeRunStore runs,
    IExchangeCommitAuthority authority,
    ITargetAccessGate access,
    ICanonicalRecordsCommandPort commands,
    IEffectOutcomeStore outcomes,
    IAcquisitionCheckpointStore checkpoints,
    TimeProvider clock,
    CommitBounds bounds)
{
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IExchangeCommitAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    private readonly ITargetAccessGate _access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly ICanonicalRecordsCommandPort _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    private readonly IEffectOutcomeStore _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
    private readonly IAcquisitionCheckpointStore _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly CommitBounds _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));

    public async ValueTask<CommitRunArtifact> CommitAsync(
        DryRunId dryRunId,
        ProposalFingerprint currentProposal,
        AcknowledgementPolicy? acknowledgementPolicy = null,
        CancellationToken cancellationToken = default)
    {
        var dryRun = await _runs.GetDryRunAsync(dryRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new DataExchangeCommitRefusedException("run.not_found", "The dry run does not exist.");
        if (dryRun.Proposal != currentProposal)
        {
            throw new DataExchangeCommitRefusedException(
                "run.stale",
                "Proposal-affecting inputs changed; create a new dry run.");
        }
        if (!await _authority.CanCommitAsync(dryRun, cancellationToken).ConfigureAwait(false))
        {
            throw new DataExchangeCommitRefusedException(
                "commit.forbidden",
                "Data Exchange commit authority was refused.");
        }
        if (dryRun.NormalizedEffects.Count > _bounds.MaxWindowSize)
        {
            throw new DataExchangeCommitRefusedException(
                "commit.window_exceeded",
                "The reviewed effect window exceeds the bounded commit size.");
        }

        var policy = acknowledgementPolicy ?? new AcknowledgementPolicy(
            new HashSet<ExchangeEffectStatus>
            {
                ExchangeEffectStatus.Applied,
                ExchangeEffectStatus.Skipped,
                ExchangeEffectStatus.Conflicted,
                ExchangeEffectStatus.Rejected,
            });
        var commitRunId = new CommitRunId(Guid.NewGuid().ToString("N"));
        var batchDerivation = new BatchIdentityInputs(
            dryRun.TenantId,
            dryRun.Proposal.DefinitionId,
            dryRun.Proposal.DefinitionVersion,
            dryRun.Proposal.MappingId,
            dryRun.Proposal.MappingVersion,
            dryRun.Proposal.MappingDigest,
            dryRun.Proposal.ConnectorId,
            dryRun.Proposal.ConnectorVersion,
            dryRun.Proposal.SourceFingerprint,
            dryRun.Proposal.InputBoundary,
            dryRun.Proposal.TargetContract);
        var batch = ExchangeIdentity.DeriveBatch(batchDerivation);
        var results = new List<CommitEffectResult>(dryRun.NormalizedEffects.Count);
        foreach (var effect in dryRun.NormalizedEffects.OrderBy(item => item.SourceOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectIdentity = ExchangeIdentity.DeriveEffect(
                batch,
                dryRun.Proposal.TargetContract,
                effect.SourceRecordIdentity,
                effect.SourceRecordVersion,
                effect.EffectDiscriminator);
            var recorded = await _outcomes.GetAsync(effectIdentity, cancellationToken).ConfigureAwait(false);
            if (recorded is not null && !IsRetryable(recorded.Outcome.Status))
            {
                results.Add(new(effect, effectIdentity, recorded.Outcome, true));
                continue;
            }

            var attempt = AttemptId.New();
            var command = new CanonicalRecordsCommand(
                commitRunId,
                batch,
                effectIdentity,
                attempt,
                dryRun.Proposal.TargetContract,
                effect);
            if (JsonSerializer.SerializeToUtf8Bytes(command).Length > _bounds.MaxCommandBytes)
            {
                throw new DataExchangeCommitRefusedException(
                    "commit.command_payload_exceeded",
                    "A canonical Records command exceeds the bounded payload size.");
            }

            EffectTerminalOutcome outcome;
            if (!await _access.CanApplyAsync(effect, cancellationToken).ConfigureAwait(false))
            {
                outcome = new(ExchangeEffectStatus.Rejected, "target.access_refused");
            }
            else
            {
                try
                {
                    outcome = await _commands.ApplyAsync(command, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    outcome = new(ExchangeEffectStatus.Failed, "records.command_failed");
                }
            }
            await _outcomes.RecordAsync(
                new(batch, effectIdentity, attempt, outcome, _clock.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
            results.Add(new(effect, effectIdentity, outcome, false));
        }

        string? durableCheckpoint = null;
        foreach (var result in results.OrderBy(item => item.Effect.SourceOrdinal))
        {
            if (!policy.SafeOutcomes.Contains(result.Outcome.Status))
            {
                break;
            }
            durableCheckpoint = result.Effect.BoundaryAfter;
        }
        if (durableCheckpoint is not null)
        {
            await _checkpoints.PromoteAsync(
                dryRun.TenantId,
                dryRun.Proposal.DefinitionId,
                durableCheckpoint,
                cancellationToken).ConfigureAwait(false);
        }

        var census = Census(results);
        var terminalStatus = census.Halted > 0
            ? ExchangeRunTerminalStatus.Halted
            : census.Skipped + census.Conflicted + census.Rejected + census.Failed > 0
                ? ExchangeRunTerminalStatus.CompletedWithRefusals
                : ExchangeRunTerminalStatus.Completed;
        var commitRun = new CommitRunArtifact(
            commitRunId,
            dryRun.Id,
            batch,
            batchDerivation,
            _clock.GetUtcNow(),
            results,
            census,
            terminalStatus,
            durableCheckpoint,
            dryRun.RetentionClass,
            dryRun.RetainUntil,
            dryRun.LegalHold);
        await _runs.SaveCommitRunAsync(commitRun, cancellationToken).ConfigureAwait(false);
        return commitRun;
    }

    private static bool IsRetryable(ExchangeEffectStatus status)
        => status is ExchangeEffectStatus.Failed or ExchangeEffectStatus.Halted;

    private static ExchangeCensus Census(IEnumerable<CommitEffectResult> effects)
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
}
