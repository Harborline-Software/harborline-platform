using System.Text;

using Harborline.Kernel.Core;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>The capacity instruction that a hold transition commits atomically with its state.</summary>
public enum BookingHoldCapacityEffect
{
    /// <summary>Claims the hold's capacity while creating a live hold.</summary>
    Acquire,
    /// <summary>Keeps capacity occupied while confirming a hold.</summary>
    Retain,
    /// <summary>Releases capacity because the hold reached a non-confirmed terminal state.</summary>
    Release,
}

/// <summary>
/// The one durable write a hold effect presents to its transaction port. The port compares
/// <see cref="ExpectedVersion"/>, changes the hold, and applies <see cref="CapacityEffect"/> in one
/// transaction; a separate capacity release is forbidden.
/// </summary>
/// <param name="Hold">The new durable state for the hold.</param>
/// <param name="ExpectedVersion">The version that must still be current when this transition commits.</param>
/// <param name="CapacityEffect">The capacity change that commits with the hold state.</param>
public sealed record BookingHoldCommit(
    BookingHold Hold,
    long ExpectedVersion,
    BookingHoldCapacityEffect CapacityEffect);

/// <summary>The transaction port's version-fenced result for a hold write.</summary>
/// <param name="Applied">Whether the expected version was current and the complete write committed.</param>
/// <param name="CurrentHold">The current durable hold after the attempted write.</param>
/// <param name="Version">The current durable version after the attempted write.</param>
public sealed record BookingHoldStoreResult(bool Applied, BookingHold CurrentHold, long Version);

/// <summary>
/// The host transaction seam for Booking holds. Its commit implementation atomically persists the
/// hold, compares its version, and acquires, retains, or releases the associated capacity.
/// </summary>
public interface IBookingHoldTransactionPort
    : IKernelPreparedTransactionPort<BookingHoldCommit, BookingHoldStoreResult>;

/// <summary>The identity and actor attribution for one idempotent hold effect.</summary>
/// <param name="CommandId">The unique command identifier.</param>
/// <param name="IdempotencyKey">The retry key for this command.</param>
/// <param name="Fingerprint">The canonical request fingerprint for idempotency-key reuse.</param>
/// <param name="ActorId">The actor attributed in the kernel audit evidence.</param>
public sealed record BookingHoldOperation(
    string CommandId,
    string IdempotencyKey,
    string Fingerprint,
    string ActorId);

/// <summary>The result of a hold effect, including the durable terminal state when a version race lost.</summary>
/// <param name="Outcome">The resulting hold and any stable refusal.</param>
/// <param name="Committed">Whether this invocation committed a hold and capacity write.</param>
/// <param name="Version">The resulting durable version when known.</param>
public sealed record BookingHoldEffectResult(BookingHoldOutcome Outcome, bool Committed, long? Version);

/// <summary>
/// Booking's runtime hold effect (T-606; DES-0025 <c>booking-eng-10</c>, <c>-18</c> and
/// <c>booking-run-4</c>). It opens the kernel transaction before it reads the kernel clock, so an
/// at-or-after-expiry confirmation becomes an expiry in the same version-fenced capacity write.
/// Workflows chooses which public transition to invoke; it does not own this state or capacity effect.
/// </summary>
public sealed class BookingHoldEffect
{
    private readonly KernelClock _clock;

    /// <summary>Creates the effect over the host's authoritative kernel clock.</summary>
    public BookingHoldEffect(KernelClock clock) =>
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Atomically creates a live hold and claims its capacity. A retry whose creation version is no
    /// longer current writes nothing and returns the durable state.
    /// </summary>
    public Task<BookingHoldEffectResult> AcquireAsync(
        BookingHold hold,
        BookingHoldOperation operation,
        IBookingHoldTransactionPort port,
        CancellationToken cancellationToken = default) =>
        CommitAsync(hold, expectedVersion: -1, operation, port, Transition.Acquire, cancellationToken);

    /// <summary>
    /// Confirms a live hold when the kernel commit instant is before expiry. At or after expiry this
    /// commits Expired and releases capacity instead, so an expired hold cannot authorize a booking.
    /// </summary>
    public Task<BookingHoldEffectResult> ConfirmAsync(
        BookingHold hold,
        long expectedVersion,
        BookingHoldOperation operation,
        IBookingHoldTransactionPort port,
        CancellationToken cancellationToken = default) =>
        CommitAsync(hold, expectedVersion, operation, port, Transition.Confirm, cancellationToken);

    /// <summary>Cancels a live hold and releases its capacity in the same version-fenced transaction.</summary>
    public Task<BookingHoldEffectResult> CancelAsync(
        BookingHold hold,
        long expectedVersion,
        BookingHoldOperation operation,
        IBookingHoldTransactionPort port,
        CancellationToken cancellationToken = default) =>
        CommitAsync(hold, expectedVersion, operation, port, Transition.Cancel, cancellationToken);

    /// <summary>Expires a live hold at or after its expiry instant and releases capacity exactly once.</summary>
    public Task<BookingHoldEffectResult> ExpireAsync(
        BookingHold hold,
        long expectedVersion,
        BookingHoldOperation operation,
        IBookingHoldTransactionPort port,
        CancellationToken cancellationToken = default) =>
        CommitAsync(hold, expectedVersion, operation, port, Transition.Expire, cancellationToken);

    private async Task<BookingHoldEffectResult> CommitAsync(
        BookingHold hold,
        long expectedVersion,
        BookingHoldOperation operation,
        IBookingHoldTransactionPort port,
        Transition transition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(port);

        BookingHoldOutcome? preparedOutcome = null;
        KernelTransactionResult<BookingHoldStoreResult> committed;
        try
        {
            committed = await KernelTransactionBoundary.ExecutePreparedAsync<BookingHoldCommit, BookingHoldStoreResult>(
            _ =>
            {
                // ExecutePreparedAsync has already opened the port transaction. This is deliberately
                // the only clock read: request/device time cannot decide the expiry race.
                var now = _clock.GetUtcNow();
                var outcome = transition switch
                {
                    Transition.Acquire => new BookingHoldOutcome(hold, null),
                    Transition.Confirm => hold.Confirm(now),
                    Transition.Cancel => hold.Cancel(),
                    Transition.Expire => hold.Expire(now),
                    _ => throw new ArgumentOutOfRangeException(nameof(transition)),
                };
                preparedOutcome = outcome;
                // A transition the hold refuses without changing state (terminal, or not yet expired)
                // must write nothing: committing it would release a terminal hold's capacity again.
                if (transition != Transition.Acquire && outcome.Hold.State == hold.State)
                    throw new RefusedTransition();
                var capacity = transition == Transition.Acquire
                    ? BookingHoldCapacityEffect.Acquire
                    : outcome.Hold.State is BookingHoldState.Cancelled or BookingHoldState.Expired
                        ? BookingHoldCapacityEffect.Release
                        : BookingHoldCapacityEffect.Retain;
                var command = new KernelCommand<BookingHoldCommit>(
                    new KernelOperationIdentity(operation.CommandId, operation.IdempotencyKey, operation.Fingerprint),
                    new BookingHoldCommit(outcome.Hold, expectedVersion, capacity),
                    new KernelAuditEvidence($"{operation.CommandId}:audit", operation.ActorId, now,
                        Encoding.UTF8.GetBytes($"booking-hold:{transition}:{hold.HoldId}")));
                return ValueTask.FromResult(command);
            },
            port,
            cancellationToken).ConfigureAwait(false);
        }
        catch (RefusedTransition)
        {
            // The boundary rolled the transaction back; nothing was staged past preparation.
            return new(preparedOutcome!, false, null);
        }

        if (committed.Refusal is not null)
            return new(new(hold, committed.Refusal.Code), false, null);

        var store = committed.Value!;
        if (store.Applied)
            return new(preparedOutcome!, true, store.Version);

        // A winner has already committed at this version. Report its durable outcome rather than
        // re-running a stale transition; this makes retries and delayed cleanup harmless.
        var refusal = store.CurrentHold.State == BookingHoldState.Held
            ? BookingHoldCodes.VersionConflict
            : BookingHoldCodes.Terminal;
        return new(new(store.CurrentHold, refusal), false, store.Version);
    }

    private enum Transition { Acquire, Confirm, Cancel, Expire }

    private sealed class RefusedTransition : Exception;
}
