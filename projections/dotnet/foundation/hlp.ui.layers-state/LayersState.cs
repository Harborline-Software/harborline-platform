namespace Harborline.Foundation.Builder;

/// <summary>Immutable active/enabled state shared by layer-oriented builders.</summary>
public sealed record LayersState
{
    private LayersState(string? activeId, IReadOnlySet<string> enabledIds)
    {
        ActiveId = activeId;
        EnabledIds = enabledIds;
    }

    /// <summary>The sole active layer id, or null.</summary>
    public string? ActiveId { get; }
    /// <summary>The immutable enabled-id membership.</summary>
    public IReadOnlySet<string> EnabledIds { get; }

    /// <summary>Initializes omitted state by selecting the first supplied lens.</summary>
    public static LayersState Initialize(IEnumerable<string> lensIds)
    {
        ArgumentNullException.ThrowIfNull(lensIds);
        var first = lensIds.FirstOrDefault();
        return Create(first, first is null ? [] : [first]);
    }

    /// <summary>Initializes explicit state; <paramref name="initialActiveId"/> may be null.</summary>
    public static LayersState Initialize(IEnumerable<string> lensIds, string? initialActiveId)
    {
        ArgumentNullException.ThrowIfNull(lensIds);
        return Create(initialActiveId, initialActiveId is null ? [] : [initialActiveId]);
    }

    /// <summary>Activates and enables an id while retaining prior enabled ids.</summary>
    public LayersState Activate(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (ActiveId == id && EnabledIds.Contains(id)) return this;
        var enabled = CopyEnabled();
        enabled.Add(id);
        return Create(id, enabled);
    }

    /// <summary>Clears active status without changing enabled membership.</summary>
    public LayersState Clear() => ActiveId is null ? this : Create(null, EnabledIds);

    /// <summary>Toggles enabled membership and clears active status when toggling the active id off.</summary>
    public LayersState Toggle(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var enabled = CopyEnabled();
        if (!enabled.Remove(id)) enabled.Add(id);
        var active = ActiveId == id && !enabled.Contains(id) ? null : ActiveId;
        return Create(active, enabled);
    }

    private HashSet<string> CopyEnabled() => new(EnabledIds, StringComparer.Ordinal);

    private static LayersState Create(string? activeId, IEnumerable<string> enabledIds) =>
        new(activeId, new HashSet<string>(enabledIds, StringComparer.Ordinal));
}
