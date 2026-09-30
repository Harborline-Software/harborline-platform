namespace Harborline.Kernel.Core;

/// <summary>Refusal codes returned by <see cref="KernelTransactionBoundary"/>.</summary>
public static class KernelTransactionErrors
{
    /// <summary>The batch did not hold exactly one command; the boundary commits one command per transaction.</summary>
    public const string MultiCommandBatch = "kernel.multi-command-batch";

    /// <summary>A participant tried to join, but no boundary execution was open on its flow to own the commit.</summary>
    public const string JoinWithoutEnclosingExecution = "kernel.join-without-enclosing-execution";

    /// <summary>A boundary execution began inside another; a second commit would escape the enclosing rollback.</summary>
    public const string NestedExecution = "kernel.nested-execution";

    /// <summary>A joined participant failed, so the enclosing execution rolled back instead of committing.</summary>
    public const string EnclosingExecutionDoomed = "kernel.enclosing-execution-doomed";
}

/// <summary>Thrown when a boundary call is made in a transaction state that cannot commit it safely.</summary>
public sealed class KernelTransactionStateException : InvalidOperationException
{
    /// <summary>Creates the exception for one of the <see cref="KernelTransactionErrors"/> codes.</summary>
    /// <param name="code">The refusal code.</param>
    public KernelTransactionStateException(string code)
        : base(code) => Code = code;

    /// <summary>The refusal code, one of <see cref="KernelTransactionErrors"/>.</summary>
    public string Code { get; }
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

/// <summary>
/// A participant that stages one command into an enclosing boundary execution's transaction. It has no commit or
/// rollback: the enclosing execution owns the single commit, and a participant failure dooms it.
/// </summary>
public interface IKernelTransactionParticipant<TRecord>
{
    /// <summary>Stages the joined command's operation identity; the host enforces idempotency on it before the enclosing commit.</summary>
    ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default);
    /// <summary>Stages the joined command's record into the enclosing transaction; it stays invisible until that commit.</summary>
    ValueTask StageRecordAsync(TRecord record, CancellationToken cancellationToken = default);
    /// <summary>Stages the audit evidence that commits or rolls back with the joined record.</summary>
    ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns one command's record, operation identity and audit commit or rollback. An execution is ambient on its
/// async flow while it runs, so a nested participant can <see cref="JoinAsync{TRecord}"/> it: the enclosing
/// execution stays the one commit point, and a participant failure dooms it.
/// </summary>
public static class KernelTransactionBoundary
{
    // ponytail: one execution per async flow, joined sequentially; no locking for participants racing on parallel tasks.
    private static readonly AsyncLocal<Execution?> Ambient = new();

    /// <summary>
    /// Opens the host transaction before preparing its one command, then stages that command's
    /// operation, record, and audit before committing. The producer is never called after commit.
    /// Participants the producer joins stage into this transaction; if any of them failed, the transaction rolls back
    /// and <see cref="KernelTransactionErrors.EnclosingExecutionDoomed"/> is thrown instead of committing.
    /// </summary>
    /// <exception cref="KernelTransactionStateException">
    /// <see cref="KernelTransactionErrors.NestedExecution"/> when called inside another execution, before the port begins.
    /// </exception>
    public static async ValueTask<KernelTransactionResult<TResult>> ExecutePreparedAsync<TRecord, TResult>(
        Func<CancellationToken, ValueTask<KernelCommand<TRecord>>> prepare,
        IKernelPreparedTransactionPort<TRecord, TResult> port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(port);

        var execution = Enter();
        try
        {
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
                execution.ThrowIfDoomed();
                var value = await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(value, null);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            execution.Ended = true;
        }
    }

    /// <summary>
    /// Commits exactly one command: stages its record and audit in one host transaction, then commits, rolling back on any
    /// failure. A batch of any other size is refused with <see cref="KernelTransactionErrors.MultiCommandBatch"/> before a
    /// transaction opens. Participants the port's staging joins commit or roll back with this transaction, and a failed
    /// participant dooms it with <see cref="KernelTransactionErrors.EnclosingExecutionDoomed"/>.
    /// </summary>
    /// <exception cref="KernelTransactionStateException">
    /// <see cref="KernelTransactionErrors.NestedExecution"/> when called inside another execution, before the port begins.
    /// </exception>
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

        var execution = Enter();
        try
        {
            await using var transaction = await port.BeginAsync(command.Operation, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await transaction.StageRecordAsync(command.Record, cancellationToken).ConfigureAwait(false);
                await transaction.StageAuditAsync(command.Audit, cancellationToken).ConfigureAwait(false);
                execution.ThrowIfDoomed();
                var value = await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(value, null);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            execution.Ended = true;
        }
    }

    /// <summary>
    /// Stages one command into the boundary execution running on the caller's async flow, without committing: the
    /// participant stages the operation identity, record and audit, and the enclosing execution's commit publishes them
    /// with its own command or not at all. Any failure here, including invalid identity or audit evidence, dooms the
    /// enclosing execution even if the caller catches it.
    /// </summary>
    /// <exception cref="KernelTransactionStateException">
    /// <see cref="KernelTransactionErrors.JoinWithoutEnclosingExecution"/> when no execution is running on this flow.
    /// </exception>
    public static async ValueTask JoinAsync<TRecord>(
        KernelCommand<TRecord> command,
        IKernelTransactionParticipant<TRecord> participant,
        CancellationToken cancellationToken = default)
    {
        var execution = Ambient.Value is { Ended: false } open ? open : null;
        try
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(participant);
            if (execution is null)
                throw new KernelTransactionStateException(KernelTransactionErrors.JoinWithoutEnclosingExecution);
            command.Operation.Validate();
            command.Audit.Validate();
            await participant.StageOperationAsync(command.Operation, cancellationToken).ConfigureAwait(false);
            await participant.StageRecordAsync(command.Record, cancellationToken).ConfigureAwait(false);
            await participant.StageAuditAsync(command.Audit, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (execution is not null) execution.Doomed = true;
            throw;
        }
    }

    private static Execution Enter()
    {
        if (Ambient.Value is { Ended: false })
            throw new KernelTransactionStateException(KernelTransactionErrors.NestedExecution);
        return Ambient.Value = new Execution();
    }

    private sealed class Execution
    {
        public bool Ended { get; set; }
        public bool Doomed { get; set; }

        public void ThrowIfDoomed()
        {
            if (Doomed) throw new KernelTransactionStateException(KernelTransactionErrors.EnclosingExecutionDoomed);
        }
    }
}
