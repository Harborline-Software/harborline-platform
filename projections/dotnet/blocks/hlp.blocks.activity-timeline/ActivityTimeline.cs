using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.ActivityTimeline;

public readonly record struct ActivitySessionId
{
    public ActivitySessionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>A sealed, display-safe identity. It deliberately contains no actor identifier.</summary>
public sealed record ActivityDisplayIdentity
{
    public ActivityDisplayIdentity(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }

    public string DisplayName { get; }
}

public enum ActivityCategory
{
    ProposalPending,
    ProposalConfirmed,
    ProposalDenied,
    CapabilityResult,
    CapabilityError,
    System
}

public sealed record ActivityDetail(string Action, string? Description);

public sealed record ActivityEntry(
    string Id,
    TenantId TenantId,
    ActivitySessionId SessionId,
    ActivityDisplayIdentity? ProposedBy,
    ActivityDisplayIdentity? ConfirmedBy,
    ActivityCategory Category,
    string Timestamp,
    ActivityDetail Detail);

/// <summary>Write-side neutral input. Raw identifiers are accepted here but never copied to returned entries.</summary>
public sealed record ActivityEntryDraft(
    string Id,
    TenantId TenantId,
    ActivitySessionId SessionId,
    string? ProposedByActorId,
    string? ConfirmedByActorId,
    ActivityCategory Category,
    string Timestamp,
    ActivityDetail Detail);

public interface IActivityAttribution
{
    ActivityDisplayIdentity Resolve(string actorId);
}

public interface IActivityEntrySource
{
    IReadOnlyList<ActivityEntry> Read(TenantId tenantId, ActivitySessionId sessionId);
    int Count(TenantId tenantId, ActivitySessionId sessionId);
}

public sealed record ActivityNotification(string Id, string Title, string? Body, string Kind, bool Read);

public sealed class InMemoryActivityEntrySource : IActivityEntrySource
{
    private readonly IActivityAttribution _attribution;
    private readonly TimeProvider _timeProvider;
    private readonly List<ActivityEntry> _entries = [];
    private readonly List<ActivityNotification> _notifications = [];

    // The pinned addResult read the ambient clock inline; per the platform convention
    // (calendar precedent) time is injected, never ambient — behavior proven identical.
    public InMemoryActivityEntrySource(IActivityAttribution attribution, TimeProvider? timeProvider = null)
    {
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<ActivityNotification> Notifications => _notifications.ToArray();

    public void Append(ActivityEntryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _entries.Insert(0, new ActivityEntry(
            draft.Id, draft.TenantId, draft.SessionId,
            draft.ProposedByActorId is null ? null : _attribution.Resolve(draft.ProposedByActorId),
            draft.ConfirmedByActorId is null ? null : _attribution.Resolve(draft.ConfirmedByActorId),
            draft.Category, draft.Timestamp, draft.Detail));
    }

    public void AddResult(TenantId tenantId, ActivitySessionId sessionId, string id, string title, string? body, bool succeeded)
    {
        var now = _timeProvider.GetLocalNow().ToString("HH:mm");
        _notifications.Insert(0, new($"result:{id}", title, body, "result", false));
        _entries.Insert(0, new($"act:result:{id}", tenantId, sessionId,
            new ActivityDisplayIdentity("System"), null,
            succeeded ? ActivityCategory.CapabilityResult : ActivityCategory.CapabilityError,
            now, new ActivityDetail(title, body)));
    }

    public void DismissNotification(string id) => _notifications.RemoveAll(item => item.Id == id);

    public void MarkAllRead()
    {
        for (var index = 0; index < _notifications.Count; index++)
            _notifications[index] = _notifications[index] with { Read = true };
    }

    public IReadOnlyList<ActivityEntry> Read(TenantId tenantId, ActivitySessionId sessionId) =>
        _entries.Where(entry => entry.TenantId == tenantId && entry.SessionId == sessionId).ToArray();

    public int Count(TenantId tenantId, ActivitySessionId sessionId) =>
        _entries.Count(entry => entry.TenantId == tenantId && entry.SessionId == sessionId);
}
