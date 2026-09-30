namespace Harborline.Foundation.NotificationDelivery;

/// <summary>The per-person state of an inbox entry, shared by every device for that person.</summary>
public enum InboxReadState
{
    /// <summary>The recipient has not yet read the entry.</summary>
    Unread = 0,
    /// <summary>The recipient has read the entry.</summary>
    Read = 1,
    /// <summary>The recipient has removed the entry from their active inbox.</summary>
    Dismissed = 2,
}

/// <summary>
/// One durable, on-node notification-centre item. Its rendered content is intentionally retained
/// here rather than projected to an external channel (<c>notification-delivery-ck-10</c>).
/// </summary>
public sealed record InboxEntry(
    Guid Id,
    string Tenant,
    string Recipient,
    string Tier,
    string Title,
    string RenderedContent,
    string SubjectDisplayName,
    string SubjectLink,
    bool AnswerOwed,
    DateTimeOffset CreatedAt,
    InboxReadState ReadState);

/// <summary>The caller-provided content for one inbox write.</summary>
public sealed record WriteInboxEntry(
    string Tenant,
    string Recipient,
    string Tier,
    string Title,
    string RenderedContent,
    string SubjectDisplayName,
    string SubjectLink,
    bool AnswerOwed);

/// <summary>Tenant- and recipient-scoped inbox persistence and read-state operations.</summary>
public interface IInboxStore
{
    /// <summary>Appends the delivered entry before it becomes visible to the recipient.</summary>
    Task<InboxEntry> AppendAsync(InboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Lists one recipient's entries newest first for the notification-centre surface.</summary>
    Task<IReadOnlyList<InboxEntry>> ListAsync(string tenant, string recipient, CancellationToken cancellationToken = default);

    /// <summary>Counts unread entries for the same recipient the notification centre displays.</summary>
    Task<int> UnreadCountAsync(string tenant, string recipient, CancellationToken cancellationToken = default);

    /// <summary>Marks one entry read for every session belonging to its recipient.</summary>
    Task MarkReadAsync(string tenant, string recipient, Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>Marks every unread entry read for the recipient's notification centre.</summary>
    Task MarkAllReadAsync(string tenant, string recipient, CancellationToken cancellationToken = default);
}

/// <summary>In-process atomic inbox persistence suitable for development composition and focused tests.</summary>
public sealed class InMemoryInboxStore : IInboxStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, InboxEntry> _entries = [];

    /// <inheritdoc />
    public Task<InboxEntry> AppendAsync(InboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _entries.Add(entry.Id, entry);
            return Task.FromResult(entry);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEntry>> ListAsync(string tenant, string recipient, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<InboxEntry> entries = _entries.Values
                .Where(entry => entry.Tenant == tenant && entry.Recipient == recipient)
                .OrderByDescending(entry => entry.CreatedAt)
                .ThenBy(entry => entry.Id)
                .ToArray();
            return Task.FromResult(entries);
        }
    }

    /// <inheritdoc />
    public Task<int> UnreadCountAsync(string tenant, string recipient, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_entries.Values.Count(entry => entry.Tenant == tenant
                && entry.Recipient == recipient
                && entry.ReadState == InboxReadState.Unread));
        }
    }

    /// <inheritdoc />
    public Task MarkReadAsync(string tenant, string recipient, Guid entryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_entries.TryGetValue(entryId, out var entry) || entry.Tenant != tenant || entry.Recipient != recipient)
            {
                throw new NotificationDeliveryRefusedException(
                    NotificationDeliveryRefusals.InboxEntryUnknown,
                    $"Inbox entry '{entryId}' is not in this recipient's inbox.");
            }

            _entries[entryId] = entry with { ReadState = InboxReadState.Read };
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task MarkAllReadAsync(string tenant, string recipient, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            foreach (var entry in _entries.Values.Where(entry => entry.Tenant == tenant
                && entry.Recipient == recipient
                && entry.ReadState == InboxReadState.Unread).ToArray())
            {
                _entries[entry.Id] = entry with { ReadState = InboxReadState.Read };
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>The in-app delivery channel that writes complete rendered content to the person's on-node inbox.</summary>
public sealed class InboxChannel
{
    private readonly IInboxStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Creates the channel with its durable inbox boundary and the authoritative time source.</summary>
    public InboxChannel(IInboxStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Writes one unread inbox entry with the full rendered content and returns the committed item.</summary>
    public async Task<InboxEntry> WriteAsync(WriteInboxEntry request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireContent(request.Tenant, nameof(request.Tenant));
        RequireContent(request.Recipient, nameof(request.Recipient));
        RequireContent(request.Tier, nameof(request.Tier));
        RequireContent(request.Title, nameof(request.Title));
        RequireContent(request.RenderedContent, nameof(request.RenderedContent));
        RequireContent(request.SubjectDisplayName, nameof(request.SubjectDisplayName));
        RequireContent(request.SubjectLink, nameof(request.SubjectLink));
        var entry = new InboxEntry(
            Guid.NewGuid(),
            request.Tenant,
            request.Recipient,
            request.Tier,
            request.Title,
            request.RenderedContent,
            request.SubjectDisplayName,
            request.SubjectLink,
            request.AnswerOwed,
            _clock.GetUtcNow(),
            InboxReadState.Unread);
        return await _store.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private static void RequireContent(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new NotificationDeliveryRefusedException(
                NotificationDeliveryRefusals.InboxEntryInvalid,
                $"Inbox entry field '{parameterName}' is required.");
        }
    }
}
