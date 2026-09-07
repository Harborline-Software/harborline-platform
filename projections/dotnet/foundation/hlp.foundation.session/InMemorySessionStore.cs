using System.Collections.Concurrent;

namespace Harborline.Foundation.Session;

/// <summary>Single-process, fail-safe-on-restart reference implementation of <see cref="ISessionStore"/>.</summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask CreateAsync(
        SessionRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        record.ValidateForStorage();

        if (!_sessions.TryAdd(record.SessionId, record))
        {
            throw new InvalidOperationException("A session record already exists for this id.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<SessionRecord?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            string.IsNullOrWhiteSpace(sessionId)
                ? null
                : _sessions.GetValueOrDefault(sessionId));
    }

    /// <inheritdoc />
    public ValueTask<SessionRecord?> TouchAsync(
        string sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sessionId)) return ValueTask.FromResult<SessionRecord?>(null);

        while (_sessions.TryGetValue(sessionId, out var current))
        {
            var touched = current.Touch(now);
            if (_sessions.TryUpdate(sessionId, touched, current))
            {
                return ValueTask.FromResult<SessionRecord?>(touched);
            }
        }

        return ValueTask.FromResult<SessionRecord?>(null);
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            !string.IsNullOrWhiteSpace(sessionId) && _sessions.TryRemove(sessionId, out _));
    }
}
