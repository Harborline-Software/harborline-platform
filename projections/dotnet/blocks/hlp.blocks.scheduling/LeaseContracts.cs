namespace Harborline.Kernel.Lease;

/// <summary>A time-bounded exclusive grant used by reservation coordination.</summary>
public sealed record Lease(
    string LeaseId,
    string ResourceId,
    string HolderNodeId,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyCollection<string> QuorumParticipants);

/// <summary>Port for the distributed lease substrate required by CP-class writes.</summary>
public interface ILeaseCoordinator : IAsyncDisposable
{
    /// <summary>Attempts to acquire an exclusive lease, returning null when quorum is unavailable.</summary>
    Task<Lease?> AcquireAsync(string resourceId, TimeSpan duration, CancellationToken ct);

    /// <summary>Releases a previously acquired lease.</summary>
    Task ReleaseAsync(Lease lease, CancellationToken ct);

    /// <summary>Reports whether this coordinator currently holds a lease for a resource.</summary>
    bool Holds(string resourceId);

    /// <summary>Gets a snapshot of leases held by this coordinator.</summary>
    IReadOnlyCollection<Lease> HeldLeases { get; }
}
