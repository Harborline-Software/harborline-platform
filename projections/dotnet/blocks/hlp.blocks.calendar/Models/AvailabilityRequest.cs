namespace Harborline.Blocks.Calendar.Models;

/// <summary>
/// The two capacity kinds (ADR 0052; DES-0033 guarantee 4). <see cref="Exclusive"/>: two overlapping
/// allocations are the definition of a conflict. <see cref="Pool"/>: several fit up to a size and the
/// next is refused. Not a number — every constraint written for one assumes the other.
/// </summary>
public enum CapacityKind
{
    Exclusive = 0,

    Pool = 1,
}

/// <summary>
/// The admitted capacity and buffer data of one required resource (T-626). Booking authors and
/// persists the Resource definition this is read from; the availability runtime consumes it as an
/// input and never stores it. The buffer is part of the hold: the candidate's footprint is widened by
/// <see cref="Buffer"/> before occupancy is tested (DES-0033 ck-4).
/// </summary>
public sealed record ResourceCapacity
{
    private ResourceCapacity(ParticipantRef resource, CapacityKind kind, int? poolSize, EventPadding buffer)
    {
        Resource = resource;
        Kind = kind;
        PoolSize = poolSize;
        Buffer = buffer;
    }

    /// <summary>The resource (a Party or an Asset) whose availability is read.</summary>
    public ParticipantRef Resource { get; }

    /// <summary>Exclusive or pool.</summary>
    public CapacityKind Kind { get; }

    /// <summary>The pool size; <see langword="null"/> for an exclusive resource.</summary>
    public int? PoolSize { get; }

    /// <summary>Setup (<see cref="EventPadding.Pre"/>) and cleanup (<see cref="EventPadding.Post"/>) buffers.</summary>
    public EventPadding Buffer { get; }

    /// <summary>The number of allocations that may overlap: 1 for exclusive, the size for a pool.</summary>
    public int ConcurrentLimit => Kind == CapacityKind.Pool ? PoolSize!.Value : 1;

    /// <summary>An exclusive resource: any overlap on the buffered footprint is a conflict.</summary>
    public static ResourceCapacity Exclusive(ParticipantRef resource, EventPadding? buffer = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new ResourceCapacity(resource, CapacityKind.Exclusive, null, buffer ?? EventPadding.None);
    }

    /// <summary>A pool resource of <paramref name="size"/> concurrent places (at least one).</summary>
    public static ResourceCapacity Pool(ParticipantRef resource, int size, EventPadding? buffer = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        return new ResourceCapacity(resource, CapacityKind.Pool, size, buffer ?? EventPadding.None);
    }
}

/// <summary>
/// One availability read (T-626): a window and the required-resource set. The window endpoints are
/// nullable so that an omitted endpoint is representable at the request boundary and refused there
/// (<see cref="AvailabilityRefusal"/>); the runtime substitutes no default window (DES-0033 ck-9).
/// </summary>
/// <param name="FromUtc">The window start; required.</param>
/// <param name="ToUtc">The window end (exclusive); required and strictly after <paramref name="FromUtc"/>.</param>
/// <param name="Resources">The required resources, evaluated as a conjunction (DES-0033 guarantee 5).</param>
public sealed record AvailabilityRequest(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<ResourceCapacity> Resources);

/// <summary>
/// An invalid availability request, refused before any store is read. Stable <see cref="Code"/> and
/// the offending <see cref="Pointer"/>, suitable for an HTTP 400 mapping by the API (T-524).
/// </summary>
public sealed record AvailabilityRefusal(string Code, string Pointer)
{
    /// <summary>The window omits <c>from</c> or <c>to</c>.</summary>
    public const string WindowUnbounded = "AVAILABILITY_WINDOW_UNBOUNDED";

    /// <summary>The window end is at or before its start.</summary>
    public const string WindowInverted = "AVAILABILITY_WINDOW_INVERTED";
}
