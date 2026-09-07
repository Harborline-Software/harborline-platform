namespace Harborline.Foundation.Session;

/// <summary>Asynchronous authoritative storage for server-side session records.</summary>
public interface ISessionStore
{
    /// <summary>Creates a record and rejects duplicate session identifiers.</summary>
    ValueTask CreateAsync(SessionRecord record, CancellationToken cancellationToken = default);

    /// <summary>Gets a live stored record by opaque identifier, or null when absent.</summary>
    ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Atomically advances last-seen time, or returns null when absent.</summary>
    ValueTask<SessionRecord?> TouchAsync(
        string sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Idempotently revokes a session.</summary>
    ValueTask<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}
