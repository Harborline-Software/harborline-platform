using Microsoft.AspNetCore.Components;
using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>A scope the shell can switch between: its id, label, optional monogram, whether it can be pinned and an optional icon.</summary>
public sealed record ShellScopeOption(string Id, string Label, string? Monogram = null, bool Pinnable = true, RenderFragment? Icon = null);
/// <summary>An action offered on a shell navigation row: its label, optional key hint and whether it is destructive.</summary>
public sealed record ShellRowAction(string Id, string Label, string? KeyHint = null, bool Destructive = false);
/// <summary>A conversation thread listed under a navigation item, with its active and editing state and its row actions.</summary>
public sealed record ShellNavThread(string Id, string Label, bool Active = false, bool Editing = false, IReadOnlyList<ShellRowAction>? Actions = null);
/// <summary>A navigation entry in the shell rail: label, pinnable flag, child threads, kind and an optional count badge.</summary>
public sealed record ShellNavItem(string Id, string Label, bool Pinnable = true, IReadOnlyList<ShellNavThread>? Threads = null, string? Kind = null, string? Count = null);
/// <summary>The signed-in identity shown in the shell footer: a label and the role.</summary>
public sealed record ShellFooterIdentity(string Label, string Role);
/// <summary>An item in the shell system menu, with an optional handler run when it is chosen.</summary>
public sealed record ShellSystemItem(string Id, string Label, Func<Task>? Invoke = null);
/// <summary>Raised when a navigation thread is opened; carries the thread and the id of the item that owns it.</summary>
public sealed record ShellThreadEvent(ShellNavThread Thread, string OwnerId);
/// <summary>Raised when a row action on a thread is chosen; carries the thread and the action id.</summary>
public sealed record ShellThreadActionEvent(ShellNavThread Thread, string ActionId);
/// <summary>Raised when a thread is renamed; carries the thread and the new name.</summary>
public sealed record ShellThreadRenameEvent(ShellNavThread Thread, string Value);

/// <summary>Projection-neutral mirror of harborline-api's api#58 wire declaration.</summary>
public sealed record PackNavigationDeclaration([property: JsonPropertyName("seedWorkspaces")] IReadOnlyList<PackNavigationWorkspace> SeedWorkspaces, [property: JsonPropertyName("modeSwitch")] PackNavigationModeSwitch? ModeSwitch = null, [property: JsonPropertyName("panelSet")] IReadOnlyList<PackPanelDeclaration>? PanelSet = null);
/// <summary>Pack declaration of the rail mode switch: the modes a user can switch between.</summary>
public sealed record PackNavigationModeSwitch([property: JsonPropertyName("modes")] IReadOnlyList<PackNavigationMode> Modes);
/// <summary>One rail mode from a pack declaration: its id, label key and the workspaces it shows.</summary>
public sealed record PackNavigationMode([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("workspaceIds")] IReadOnlyList<string> WorkspaceIds);
/// <summary>A workspace declared by a pack: label key, icon, destination and count queries, groups, create actions and document spine.</summary>
public sealed record PackNavigationWorkspace([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("icon")] string? Icon = null, [property: JsonPropertyName("destinationQueryRef")] string? DestinationQueryRef = null, [property: JsonPropertyName("countQueryRef")] string? CountQueryRef = null, [property: JsonPropertyName("groups")] IReadOnlyList<PackNavigationGroup>? Groups = null, [property: JsonPropertyName("createActions")] IReadOnlyList<PackNavigationAction>? CreateActions = null, [property: JsonPropertyName("documentSpine")] IReadOnlyList<PackDocumentSpineNode>? DocumentSpine = null, [property: JsonPropertyName("defaultForPersonas")] IReadOnlyList<string>? DefaultForPersonas = null);
/// <summary>A navigation item declared inside a group: its id, label key and resolved label.</summary>
public sealed record PackNavigationItem([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("label")] string Label);
/// <summary>A group of navigation items in a workspace, with its destination and count queries and an optional add action.</summary>
public sealed record PackNavigationGroup([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("itemIds")] IReadOnlyList<string> ItemIds, [property: JsonPropertyName("destinationQueryRef")] string? DestinationQueryRef = null, [property: JsonPropertyName("countQueryRef")] string? CountQueryRef = null, [property: JsonPropertyName("addAction")] PackNavigationAction? AddAction = null, [property: JsonPropertyName("items")] IReadOnlyList<PackNavigationItem>? Items = null);
/// <summary>A create or add action declared by a pack: verb key, icon, binding, shortcut and the roles allowed to use it.</summary>
public sealed record PackNavigationAction([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("verbKey")] string VerbKey, [property: JsonPropertyName("icon")] string Icon, [property: JsonPropertyName("binding")] string Binding, [property: JsonPropertyName("shortcut")] string Shortcut, [property: JsonPropertyName("permittedRoles")] IReadOnlyList<string> PermittedRoles);
/// <summary>One node of a workspace document spine: label key, binding and nested child nodes.</summary>
public sealed record PackDocumentSpineNode([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("binding")] string Binding, [property: JsonPropertyName("children")] IReadOnlyList<PackDocumentSpineNode>? Children = null);
/// <summary>A dock panel declared by a pack: binding, shortcut, default width, minimum height, default-open state, footer and traits.</summary>
public sealed record PackPanelDeclaration([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("binding")] string Binding, [property: JsonPropertyName("shortcut")] string Shortcut, [property: JsonPropertyName("defaultWidth")] int DefaultWidth, [property: JsonPropertyName("minimumHeight")] int MinimumHeight, [property: JsonPropertyName("defaultOpen")] bool DefaultOpen, [property: JsonPropertyName("labelKey")] string? LabelKey = null, [property: JsonPropertyName("footer")] PackPanelFooter? Footer = null, [property: JsonPropertyName("traits")] IReadOnlyList<string>? Traits = null, [property: JsonPropertyName("popOut")] bool PopOut = false, [property: JsonPropertyName("headerForm")] string? HeaderForm = null, [property: JsonPropertyName("bodyTemplate")] string? BodyTemplate = null);
/// <summary>An open panel listed in the shell panel switcher, with the callback that closes it.</summary>
public sealed record ShellPanelOpenItem(string Id, string Label, Action Close);
/// <summary>A request to pop a panel out of the dock into its own window, carrying the dock state to hand over.</summary>
public sealed record ShellPanelPopOutRequest(string PanelId, DockStateSnapshot State);
/// <summary>The footer a panel declares: its kind, label key and optional binding.</summary>
public sealed record PackPanelFooter([property: JsonPropertyName("kind")] string Kind, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("binding")] string? Binding = null);
/// <summary>Host-supplied navigation state layered over a declaration: items, counts, recent and suggested entries and create defaults.</summary>
public sealed record ShellNavigationState(IReadOnlyDictionary<string, ShellNavItem>? Items = null, IReadOnlyDictionary<string, string>? Counts = null, IReadOnlyDictionary<string, IReadOnlyList<ShellNavItem>>? RecentByWorkspace = null, IReadOnlyDictionary<string, ShellNavItem>? SuggestedByWorkspace = null, IReadOnlyDictionary<string, string>? DefaultCreateActionByWorkspace = null, IReadOnlyDictionary<string, string>? CapabilityGuidanceByBinding = null);
/// <summary>A resolved create action as the rail renders it: label, icon, binding and shortcut.</summary>
public sealed record ShellActionViewModel(string Id, string Label, string Icon, string Binding, string Shortcut);
/// <summary>A resolved rail group: its label, the items it shows and an optional add action.</summary>
public sealed record ShellGroupViewModel(string Id, string Label, IReadOnlyList<ShellNavItem> Items, ShellActionViewModel? AddAction = null);
/// <summary>A resolved workspace for the rail: its groups, count badge, create actions and document spine.</summary>
public sealed record ShellWorkspaceViewModel(string Id, string Label, IReadOnlyList<ShellGroupViewModel> Groups, string? Count, IReadOnlyList<ShellActionViewModel> CreateActions, string? DefaultCreateActionId, string? CreateGuidance, IReadOnlyList<PackDocumentSpineNode> DocumentSpine, IReadOnlyList<ShellNavItem> Recent, ShellNavItem? Suggested);
/// <summary>The complete rail render model: resolved workspaces, mode switch entries and the panels to dock.</summary>
public sealed record ShellNavigationViewModel(IReadOnlyList<ShellWorkspaceViewModel> Workspaces, IReadOnlyList<(string Id, string Label, IReadOnlyList<string> WorkspaceIds)> Modes, IReadOnlyList<PackPanelDeclaration> Panels);
/// <summary>The viewport width band the app shell is laid out for.</summary>
public enum ShellBreakpoint
{
    /// <summary>Viewport narrower than 600 px, the phone layout.</summary>
    Compact,
    /// <summary>Viewport from 600 px up to 839 px.</summary>
    Medium,
    /// <summary>Viewport from 840 px up to 1199 px.</summary>
    Expanded,
    /// <summary>Viewport from 1200 px up to 1599 px.</summary>
    Large,
    /// <summary>Viewport of 1600 px or wider.</summary>
    ExtraLarge
}

/// <summary>Fixed measurements and rules of the shell chrome, shared by the rail, dock and panels.</summary>
public static class ShellChromeContract
{
/// <summary>The top-to-bottom order of the zones in the shell rail.</summary>
    public static readonly string[] RailZoneOrder = ["head", "mode", "primary-action", "workspaces", "pinned", "groups", "recent", "suggested", "footer"];
/// <summary>The Material width breakpoints, in pixels, that the shell layout switches on.</summary>
    public static readonly int[] MaterialBreakpoints = [600, 840, 1200, 1600];
/// <summary>Maps a viewport width in pixels to the shell breakpoint class.</summary>
    public static ShellBreakpoint Breakpoint(int width) => width switch { >= 1600 => ShellBreakpoint.ExtraLarge, >= 1200 => ShellBreakpoint.Large, >= 840 => ShellBreakpoint.Expanded, >= 600 => ShellBreakpoint.Medium, _ => ShellBreakpoint.Compact };
/// <summary>Returns the lowercase CSS name of a shell breakpoint, such as extra-large.</summary>
    public static string BreakpointName(ShellBreakpoint value) => value switch { ShellBreakpoint.ExtraLarge => "extra-large", _ => value.ToString().ToLowerInvariant() };
/// <summary>Keyboard shortcut chords for the built-in shell commands, by command id.</summary>
    public static readonly IReadOnlyDictionary<string, string> Shortcuts = new Dictionary<string, string>
    {
        ["find"] = "Mod+K", ["create"] = "Mod+N", ["rail"] = "Mod+\\", ["inspector"] = "Mod+Shift+I", ["workspace"] = "Mod+1..9"
    };
    // RailWidth and ContentFloor are spec-owned taste, not values derived from published guidance.
/// <summary>Height of the shell top bar, in pixels.</summary>
    public const int BarHeight = 34;
/// <summary>Default width of the navigation rail, in pixels.</summary>
    public const int RailWidth = 216;
/// <summary>Narrowest the navigation rail can be resized to, in pixels.</summary>
    public const int RailMinimum = 120;
/// <summary>Minimum width kept for the main content when panels are docked, in pixels.</summary>
    public const int ContentFloor = 420;
    // chrome-spec 6:174 - the shell header is 33px; the toolbar and footer are the body's other content-sized slots.
/// <summary>Height of a panel header, in pixels.</summary>
    public const int PanelHeaderHeight = 33;
/// <summary>Height of a panel toolbar, in pixels.</summary>
    public const int PanelToolbarHeight = 32;
/// <summary>Height of a panel footer, in pixels.</summary>
    public const int PanelFooterHeight = 28;
    // chrome-spec 6:188-190 - "The header has exactly two forms ... Only panels that open one item take the second."
/// <summary>Picks how a panel header renders: a toggle chip for panels that open one item, otherwise a plain title.</summary>
    public static string PanelHeaderForm(PackPanelDeclaration panel) => panel.Traits?.Contains("OpensOne") == true ? "toggle-chip" : "title";
    // chrome-spec 6:218 - "the overflow is earned by OpensOne or Consequential"; every other header is three affordances.
/// <summary>Whether a panel needs an overflow menu, which panels that open one item or carry consequential actions do.</summary>
    public static bool PanelEarnsOverflow(PackPanelDeclaration panel) => panel.Traits?.Contains("OpensOne") == true || panel.Traits?.Contains("Consequential") == true;
    // chrome-spec 6:236-238 - the minimum is the content-sized slots ABOVE the one flexible slot; the body,
    // which is allowed to scroll, contributes nothing to it.
/// <summary>Smallest height a panel slot can take: header, toolbar and footer when it has one.</summary>
    public static int PanelSlotMinimum(PackPanelDeclaration panel) => PanelHeaderHeight + PanelToolbarHeight + (panel.Footer is null ? 0 : PanelFooterHeight);
/// <summary>Smallest height a panel is laid out at: its declared minimum or its chrome height, whichever is larger.</summary>
    public static int PanelMinimumHeight(PackPanelDeclaration panel) => Math.Max(panel.MinimumHeight, PanelSlotMinimum(panel));
    /// <summary>Half-up rounding (floor(x + 0.5)) - the same rule the React projection's drags use.</summary>
    public static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);
/// <summary>Builds the route address for a record from its kind and id, escaping each segment.</summary>
    public static string Address(string kind, string id) => $"/{Uri.EscapeDataString(kind.Trim('/'))}/{Uri.EscapeDataString(id)}";
}
