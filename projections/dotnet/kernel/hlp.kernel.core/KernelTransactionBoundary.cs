namespace Harborline.Kernel.Core;

/// <summary>Refusal codes returned by <see cref="KernelTransactionBoundary"/>.</summary>
public static class KernelTransactionErrors
{
    /// <summary>The batch did not hold exactly one command; the boundary commits one command per transaction.</summary>
    public const string MultiCommandBatch = "kernel.multi-command-batch";
}

/// <summary>Identifies one command for idempotent replay and duplicate detection.</summary>
/// <param name="CommandId">The caller's identifier for this command.</param>
/// <param name="IdempotencyKey">The key a host uses to recognise a repeated submission of the same command.</param>
/// <param name="Fingerprint">A digest of the command's content; the same key with a different fingerprint is a conflicting reuse.</param>
public sealed record KernelOperationIdentity(
    string CommandId,
    string IdempotencyKey,
    string Fingerprint)
{
    /// <summary>Returns this identity; throws <see cref="ArgumentException"/> when any field is null or blank.</summary>
    public KernelOperationIdentity Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Fingerprint);
        return this;
    }
}

/// <summary>The audit record committed atomically with a command's record.</summary>
/// <param name="AuditId">The unique identifier of this audit entry.</param>
/// <param name="ActorId">The actor the command is attributed to.</param>
/// <param name="RecordedAt">When the audited action was recorded.</param>
/// <param name="Payload">The opaque audit body; may be empty.</param>
public sealed record KernelAuditEvidence(
    string AuditId,
    string ActorId,
    DateTimeOffset RecordedAt,
    ReadOnlyMemory<byte> Payload)
{
    /// <summary>Returns this evidence; throws <see cref="ArgumentException"/> when the audit or actor id is null or blank.</summary>
    public KernelAuditEvidence Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AuditId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActorId);
        return this;
    }
}

/// <summary>One command: its operation identity, the record it writes, and the audit that must commit with it.</summary>
/// <typeparam name="TRecord">The host's record type.</typeparam>
/// <param name="Operation">The command's idempotency identity.</param>
/// <param name="Record">The record staged into the host transaction.</param>
/// <param name="Audit">The audit evidence staged into the same transaction.</param>
public sealed record KernelCommand<TRecord>(
    KernelOperationIdentity Operation,
    TRecord Record,
    KernelAuditEvidence Audit);

/// <summary>Why the boundary refused a batch without opening a transaction.</summary>
/// <param name="Code">The refusal code, one of <see cref="KernelTransactionErrors"/>.</param>
/// <param name="ObservedCommandCount">How many commands the refused batch held.</param>
public sealed record KernelTransactionRefusal(string Code, int ObservedCommandCount);

/// <summary>The outcome of a boundary call: the committed value, or a refusal and no value.</summary>
/// <typeparam name="TResult">The host's commit result type.</typeparam>
/// <param name="Value">The host's commit result; default when refused.</param>
/// <param name="Refusal">Null when the command committed.</param>
public sealed record KernelTransactionResult<TResult>(
    TResult? Value,
    KernelTransactionRefusal? Refusal)
{
    /// <summary>True when the command committed, that is when <see cref="Refusal"/> is null.</summary>
    public bool Committed => Refusal is null;
}

/// <summary>
/// Host-backed transaction staged by the kernel boundary. Implementations must publish none of
/// the staged values before <see cref="CommitAsync"/> completes successfully.
/// </summary>
public interface IKernelTransaction<TRecord, TResult> : IAsyncDisposable
{
    /// <summary>Stages the command's record; it stays invisible until commit.</summary>
    ValueTask StageRecordAsync(TRecord record, CancellationToken cancellationToken = default);
    /// <summary>Stages the audit evidence that commits or rolls back with the record.</summary>
    ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default);
    /// <summary>Atomically publishes every staged value and returns the host's result.</summary>
    ValueTask<TResult> CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>Discards every staged value; the boundary calls it with <see cref="CancellationToken.None"/> on any failure.</summary>
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>The host seam that opens a transaction for a command whose identity is known up front.</summary>
public interface IKernelTransactionPort<TRecord, TResult>
{
    /// <summary>Opens a transaction bound to <paramref name="operation"/>; the host enforces idempotency on it.</summary>
    ValueTask<IKernelTransaction<TRecord, TResult>> BeginAsync(
        KernelOperationIdentity operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A transaction that is opened before its command can be prepared. The kernel still owns the
/// operation identity and the record/audit staging sequence once the producer returns a command.
/// </summary>
public interface IKernelPreparedTransaction<TRecord, TResult> : IAsyncDisposable
{
    /// <summary>Stages the prepared command's operation identity; the host enforces idempotency on it before commit.</summary>
    ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default);
    /// <summary>Stages the command's record; it stays invisible until commit.</summary>
    ValueTask StageRecordAsync(TRecord record, CancellationToken cancellationToken = default);
    /// <summary>Stages the audit evidence that commits or rolls back with the record.</summary>
    ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default);
    /// <summary>Atomically publishes every staged value and returns the host's result.</summary>
    ValueTask<TResult> CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>Discards every staged value; the boundary calls it with <see cref="CancellationToken.None"/> on any failure.</summary>
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>The host seam that opens a transaction before its command's identity is known.</summary>
public interface IKernelPreparedTransactionPort<TRecord, TResult>
{
    /// <summary>Opens a transaction before the command is prepared, so preparation reads inside it.</summary>
    ValueTask<IKernelPreparedTransaction<TRecord, TResult>> BeginAsync(CancellationToken cancellationToken = default);
}

/// <summary>Owns one command's record, operation identity and audit commit or rollback.</summary>
public static class KernelTransactionBoundary
{
    /// <summary>
    /// Opens the host transaction before preparing its one command, then stages that command's
    /// operation, record, and audit before committing. The producer is never called after commit.
    /// </summary>
    public static async ValueTask<KernelTransactionResult<TResult>> ExecutePreparedAsync<TRecord, TResult>(
        Func<CancellationToken, ValueTask<KernelCommand<TRecord>>> prepare,
        IKernelPreparedTransactionPort<TRecord, TResult> port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(port);

        await using var transaction = await port.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = await prepare(cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(command);
            command.Operation.Validate();
            command.Audit.Validate();
            await transaction.StageOperationAsync(command.Operation, cancellationToken).ConfigureAwait(false);
            await transaction.StageRecordAsync(command.Record, cancellationToken).ConfigureAwait(false);
            await transaction.StageAuditAsync(command.Audit, cancellationToken).ConfigureAwait(false);
            var value = await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(value, null);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Commits exactly one command: stages its record and audit in one host transaction, then commits, rolling back on any
    /// failure. A batch of any other size is refused with <see cref="KernelTransactionErrors.MultiCommandBatch"/> before a
    /// transaction opens.
    /// </summary>
    public static async ValueTask<KernelTransactionResult<TResult>> ExecuteAsync<TRecord, TResult>(
        IEnumerable<KernelCommand<TRecord>> commands,
        IKernelTransactionPort<TRecord, TResult> port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(port);

        IReadOnlyList<KernelCommand<TRecord>> materialized = commands as IReadOnlyList<KernelCommand<TRecord>> ?? commands.ToArray();
        if (materialized.Count != 1)
            return new(default, new(KernelTransactionErrors.MultiCommandBatch, materialized.Count));

        var command = materialized[0];
        ArgumentNullException.ThrowIfNull(command);
        command.Operation.Validate();
        command.Audit.Validate();

        await using var transaction = await port.BeginAsync(command.Operation, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await transaction.StageRecordAsync(command.Record, cancellationToken).ConfigureAwait(false);
            await transaction.StageAuditAsync(command.Audit, cancellationToken).ConfigureAwait(false);
            var value = await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(value, null);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
