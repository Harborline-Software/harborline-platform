namespace Harborline.Blocks.Calendar.Models;

/// <summary>Why one resource cannot hold the requested window; <see cref="None"/> when it can.</summary>
public enum Unavailability
{
    None = 0,

    /// <summary>The window is not inside the resource's supply (base hours minus exceptions).</summary>
    OutsideSupply = 1,

    /// <summary>Every concurrent place is taken somewhere over the buffered footprint.</summary>
    CapacityExhausted = 2,
}

/// <summary>
/// One resource's derived availability over the requested window (T-626). Derived per read from the
/// supply and the current allocations and holds; nothing is stored.
/// </summary>
/// <param name="Resource">The resource read.</param>
/// <param name="Free">Supply with every capacity-exhausted span removed, clipped to the window, ordered by start.</param>
/// <param name="Busy">Supply where every concurrent place is taken, clipped to the window, ordered by start.</param>
/// <param name="Remaining">Places left over the whole buffered footprint: the limit minus the deepest overlap.</param>
/// <param name="Unavailability">Why the window cannot be held, or <see cref="Models.Unavailability.None"/>.</param>
public sealed record ResourceAvailabilityRead(
    ParticipantRef Resource,
    IReadOnlyList<TimeInterval> Free,
    IReadOnlyList<TimeInterval> Busy,
    int Remaining,
    Unavailability Unavailability)
{
    /// <summary>True when the resource can hold the window including its buffers.</summary>
    public bool Available => Unavailability == Unavailability.None;
}

/// <summary>
/// The runtime's answer: a refusal, or one read per required resource. <see cref="Available"/> is the
/// conjunction: every resource free for the whole window plus its buffers, or refused.
/// </summary>
public sealed record AvailabilityAnswer(
    AvailabilityRefusal? Refusal,
    IReadOnlyList<ResourceAvailabilityRead> Resources)
{
    /// <summary>True when the request was admitted and every required resource can hold the window.</summary>
    public bool Available => Refusal is null && Resources.All(r => r.Available);

    internal static AvailabilityAnswer Refused(string code, string pointer)
        => new(new AvailabilityRefusal(code, pointer), Array.Empty<ResourceAvailabilityRead>());
}
