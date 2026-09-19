namespace Harborline.Kernel.Core;

public static class KernelTransactionErrors
{
    public const string MultiCommandBatch = "kernel.multi-command-batch";
}

public sealed record KernelOperationIdentity(
    string CommandId,
    string IdempotencyKey,
    string Fingerprint)
{
    public KernelOperationIdentity Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Fingerprint);
        return this;
    }
}

public sealed record KernelAuditEvidence(
    string AuditId,
    string ActorId,
    DateTimeOffset RecordedAt,
    ReadOnlyMemory<byte> Payload)
{
    public KernelAuditEvidence Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AuditId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActorId);
        return this;
    }
}

public sealed record KernelCommand<TRecord>(
    KernelOperationIdentity Operation,
    TRecord Record,
    KernelAuditEvidence Audit);

public sealed record KernelTransactionRefusal(string Code, int ObservedCommandCount);

public sealed record KernelTransactionResult<TResult>(
    TResult? Value,
    KernelTransactionRefusal? Refusal)
{
    public bool Committed => Refusal is null;
}

/// <summary>
/// Host-backed transaction staged by the kernel boundary. Implementations must publish none of
/// the staged values before <see cref="CommitAsync"/> completes successfully.
/// </summary>
public interface IKernelTransaction<TRecord, TResult> : IAsyncDisposable
{
    ValueTask StageRecordAsync(TRecord record, CancellationToken cancellationToken = default);
    ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default);
    ValueTask<TResult> CommitAsync(CancellationToken cancellationToken = default);
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IKernelTransactionPort<TRecord, TResult>
{
    ValueTask<IKernelTransaction<TRecord, TResult>> BeginAsync(
        KernelOperationIdentity operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Owns one command's record, operation identity and audit commit or rollback.</summary>
public static class KernelTransactionBoundary
{
    public static async ValueTask<KernelTransactionResult<TResult>> ExecuteAsync<TRecord, TResult>(
        IEnumerable<KernelCommand<TRecord>> commands,
        IKernelTransactionPort<TRecord, TResult> port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(port);

        var materialized = commands as IReadOnlyList<KernelCommand<TRecord>> ?? commands.ToArray();
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
