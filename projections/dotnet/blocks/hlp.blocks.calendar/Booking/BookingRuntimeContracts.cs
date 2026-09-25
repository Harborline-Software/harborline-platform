using Harborline.Blocks.Calendar.Models;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>
/// An Allocation: a catalogue record, not a definition, because instances are tenant data
/// (DES-0025 booking-ck-16, L499, L531). It never travels in a pack. Its state is workflow-governed.
/// </summary>
public sealed record BookingAllocation(
    string BookableId,
    string SubjectId,
    TimeInterval Window,
    string State,
    IReadOnlyList<string> HeldResources,
    string? SwappedFromId);

/// <summary>A hold's states; every state but <see cref="Held"/> is terminal.</summary>
public enum BookingHoldState
{
    Held,
    Confirmed,
    Cancelled,
    Expired,
}

/// <summary>Stable refusal codes of hold transitions.</summary>
public static class BookingHoldCodes
{
    /// <summary>The commit instant is at or after expiry: expiry wins.</summary>
    public const string Expired = "booking.hold.expired";
    /// <summary>The hold already has its one terminal outcome.</summary>
    public const string Terminal = "booking.hold.terminal";
    /// <summary>An expiry attempted before the hold's expiry instant.</summary>
    public const string NotExpired = "booking.hold.not_expired";
    /// <summary>A requested hold lifetime that is not a positive whole number of minutes.</summary>
    public const string LifetimeInvalid = "booking.hold.lifetime_invalid";
    /// <summary>A requested hold lifetime above the Bookable's effective maximum (T-724 ruling Q21).</summary>
    public const string LifetimeExceedsMaximum = "booking.hold.lifetime_exceeds_maximum";
}

/// <summary>The hold after a transition, and the refusal when the requested transition did not happen.</summary>
public sealed record BookingHoldOutcome(BookingHold Hold, string? Refusal);

/// <summary>
/// A hold is Booking runtime state, never pack content (ADR 0095 ruling 7). It is valid over the
/// half-open interval [created, <see cref="ExpiresAtUtc"/>): at or after expiry it is expired (T-724 Q3). These transitions are
/// pure: the caller passes the kernel clock read inside the commit's transaction, never the client's
/// clock or the request's start time (ADR 0095 Q2, booking-ck-22), and commits the result under the
/// same version fence. The transactional effect is T-606's.
/// </summary>
public sealed record BookingHold(string HoldId, DateTimeOffset ExpiresAtUtc, BookingHoldState State = BookingHoldState.Held)
{
    /// <summary>Confirms before expiry; at or after it the hold expires instead and the confirm is refused.</summary>
    public BookingHoldOutcome Confirm(DateTimeOffset kernelCommitInstantUtc)
        => State != BookingHoldState.Held ? new(this, BookingHoldCodes.Terminal)
            : kernelCommitInstantUtc >= ExpiresAtUtc ? new(this with { State = BookingHoldState.Expired }, BookingHoldCodes.Expired)
            : new(this with { State = BookingHoldState.Confirmed }, null);

    /// <summary>Expires a live hold at or after its expiry instant.</summary>
    public BookingHoldOutcome Expire(DateTimeOffset kernelCommitInstantUtc)
        => State != BookingHoldState.Held ? new(this, BookingHoldCodes.Terminal)
            : kernelCommitInstantUtc < ExpiresAtUtc ? new(this, BookingHoldCodes.NotExpired)
            : new(this with { State = BookingHoldState.Expired }, null);

    /// <summary>Cancels a live hold.</summary>
    public BookingHoldOutcome Cancel()
        => State != BookingHoldState.Held ? new(this, BookingHoldCodes.Terminal)
            : new(this with { State = BookingHoldState.Cancelled }, null);
}
