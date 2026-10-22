namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>Identifies the person, tenant, and workspace whose tab presentation is being shown.</summary>
/// <param name="Person">The current person's identity.</param>
/// <param name="Tenant">The current tenant's identity.</param>
/// <param name="Workspace">The current workspace's identity.</param>
public sealed record AppShellRev6Owner(string Person, string Tenant, string Workspace);

/// <summary>Describes one tab in the current owner's presentation.</summary>
/// <param name="Id">The tab identity derived from its kind and target.</param>
/// <param name="Kind">The registered tab kind.</param>
/// <param name="Target">The stable target identity within that kind.</param>
/// <param name="Preview">Whether this is the temporary preview tab.</param>
/// <param name="Pinned">Whether the tab is pinned.</param>
/// <param name="Dirty">Whether the tab has unsaved edits.</param>
public sealed record AppShellRev6Tab(string Id, string Kind, string Target, bool Preview, bool Pinned, bool Dirty);

/// <summary>Correlates a requested save with the original owner and draft generation.</summary>
/// <param name="Owner">The owner captured when the save was requested.</param>
/// <param name="Id">The tab whose draft must be saved before closing.</param>
/// <param name="Generation">The draft generation to acknowledge.</param>
/// <param name="RequestId">The request identity used to match the save acknowledgment.</param>
public sealed record AppShellRev6SaveRequest(AppShellRev6Owner Owner, string Id, long Generation, long RequestId);

/// <summary>The current owner's visible tab state; private state for other users stays in the host.</summary>
/// <param name="Owner">The owner to whom this presentation belongs.</param>
/// <param name="Destination">The current destination shown by the host.</param>
/// <param name="Tabs">The tabs visible to this owner.</param>
/// <param name="Active">The active tab id, or null when no tab is active.</param>
/// <param name="PendingClose">The dirty tab id pending a close decision or save acknowledgment, or null.</param>
/// <param name="SaveRequest">The pending save request, or null when no save is awaiting acknowledgment.</param>
public sealed record AppShellRev6Presentation(AppShellRev6Owner Owner, string Destination,
    AppShellRev6Tab[] Tabs, string? Active, string? PendingClose, AppShellRev6SaveRequest? SaveRequest);

/// <summary>The person's decision when closing a dirty tab.</summary>
public enum AppShellRev6CloseChoice
{
    /// <summary>Request a save; the editor remains until the host acknowledges success.</summary>
    Save,
    /// <summary>Drop the draft and close the tab.</summary>
    Discard,
    /// <summary>Leave the tab and its draft open.</summary>
    Cancel
}

/// <summary>A tab action emitted to the host for the current owner.</summary>
/// <param name="Type">The action name interpreted by the host.</param>
/// <param name="Id">The tab id for actions that address an existing tab.</param>
/// <param name="Choice">The person's decision for a resolve-close action.</param>
/// <param name="Kind">The registered kind for an open action.</param>
/// <param name="Target">The stable target identity for an open action.</param>
public sealed record AppShellRev6Intent(string Type, string? Id = null, AppShellRev6CloseChoice? Choice = null,
    string? Kind = null, string? Target = null);
