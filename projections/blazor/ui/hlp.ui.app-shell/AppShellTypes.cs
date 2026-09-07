using Microsoft.AspNetCore.Components;
using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

public sealed record ShellScopeOption(string Id, string Label, string? Monogram = null, bool Pinnable = true, RenderFragment? Icon = null);
public sealed record ShellRowAction(string Id, string Label, string? KeyHint = null, bool Destructive = false);
public sealed record ShellNavThread(string Id, string Label, bool Active = false, bool Editing = false, IReadOnlyList<ShellRowAction>? Actions = null);
public sealed record ShellNavItem(string Id, string Label, bool Pinnable = true, IReadOnlyList<ShellNavThread>? Threads = null, string? Kind = null, string? Count = null);
public sealed record ShellFooterIdentity(string Label, string Role);
public sealed record ShellSystemItem(string Id, string Label, Func<Task>? Invoke = null);
public sealed record ShellThreadEvent(ShellNavThread Thread, string OwnerId);
public sealed record ShellThreadActionEvent(ShellNavThread Thread, string ActionId);
public sealed record ShellThreadRenameEvent(ShellNavThread Thread, string Value);

/// <summary>Projection-neutral mirror of harborline-api's api#58 wire declaration.</summary>
public sealed record PackNavigationDeclaration([property: JsonPropertyName("seedWorkspaces")] IReadOnlyList<PackNavigationWorkspace> SeedWorkspaces, [property: JsonPropertyName("modeSwitch")] PackNavigationModeSwitch? ModeSwitch = null, [property: JsonPropertyName("panelSet")] IReadOnlyList<PackPanelDeclaration>? PanelSet = null);
public sealed record PackNavigationModeSwitch([property: JsonPropertyName("modes")] IReadOnlyList<PackNavigationMode> Modes);
public sealed record PackNavigationMode([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("workspaceIds")] IReadOnlyList<string> WorkspaceIds);
public sealed record PackNavigationWorkspace([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("icon")] string? Icon = null, [property: JsonPropertyName("destinationQueryRef")] string? DestinationQueryRef = null, [property: JsonPropertyName("countQueryRef")] string? CountQueryRef = null, [property: JsonPropertyName("groups")] IReadOnlyList<PackNavigationGroup>? Groups = null, [property: JsonPropertyName("createActions")] IReadOnlyList<PackNavigationAction>? CreateActions = null, [property: JsonPropertyName("documentSpine")] IReadOnlyList<PackDocumentSpineNode>? DocumentSpine = null, [property: JsonPropertyName("defaultForPersonas")] IReadOnlyList<string>? DefaultForPersonas = null);
public sealed record PackNavigationGroup([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("itemIds")] IReadOnlyList<string> ItemIds, [property: JsonPropertyName("destinationQueryRef")] string? DestinationQueryRef = null, [property: JsonPropertyName("countQueryRef")] string? CountQueryRef = null, [property: JsonPropertyName("addAction")] PackNavigationAction? AddAction = null);
public sealed record PackNavigationAction([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("verbKey")] string VerbKey, [property: JsonPropertyName("icon")] string Icon, [property: JsonPropertyName("binding")] string Binding, [property: JsonPropertyName("shortcut")] string Shortcut, [property: JsonPropertyName("permittedRoles")] IReadOnlyList<string> PermittedRoles);
public sealed record PackDocumentSpineNode([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("binding")] string Binding, [property: JsonPropertyName("children")] IReadOnlyList<PackDocumentSpineNode>? Children = null);
public sealed record PackPanelDeclaration([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("binding")] string Binding, [property: JsonPropertyName("shortcut")] string Shortcut, [property: JsonPropertyName("defaultWidth")] int DefaultWidth, [property: JsonPropertyName("minimumHeight")] int MinimumHeight, [property: JsonPropertyName("defaultOpen")] bool DefaultOpen, [property: JsonPropertyName("labelKey")] string? LabelKey = null, [property: JsonPropertyName("footer")] PackPanelFooter? Footer = null, [property: JsonPropertyName("traits")] IReadOnlyList<string>? Traits = null, [property: JsonPropertyName("popOut")] bool PopOut = false);
public sealed record ShellPanelOpenItem(string Id, string Label, Action Close);
public sealed record ShellPanelPopOutRequest(string PanelId, DockStateSnapshot State);
public sealed record PackPanelFooter([property: JsonPropertyName("kind")] string Kind, [property: JsonPropertyName("labelKey")] string LabelKey, [property: JsonPropertyName("binding")] string? Binding = null);
public sealed record ShellNavigationState(IReadOnlyDictionary<string, ShellNavItem>? Items = null, IReadOnlyDictionary<string, string>? Counts = null, IReadOnlyDictionary<string, IReadOnlyList<ShellNavItem>>? RecentByWorkspace = null, IReadOnlyDictionary<string, ShellNavItem>? SuggestedByWorkspace = null, IReadOnlyDictionary<string, string>? DefaultCreateActionByWorkspace = null, IReadOnlyDictionary<string, string>? CapabilityGuidanceByBinding = null);
public sealed record ShellActionViewModel(string Id, string Label, string Icon, string Binding, string Shortcut);
public sealed record ShellGroupViewModel(string Id, string Label, IReadOnlyList<ShellNavItem> Items, ShellActionViewModel? AddAction = null);
public sealed record ShellWorkspaceViewModel(string Id, string Label, IReadOnlyList<ShellGroupViewModel> Groups, string? Count, IReadOnlyList<ShellActionViewModel> CreateActions, string? DefaultCreateActionId, string? CreateGuidance, IReadOnlyList<PackDocumentSpineNode> DocumentSpine, IReadOnlyList<ShellNavItem> Recent, ShellNavItem? Suggested);
public sealed record ShellNavigationViewModel(IReadOnlyList<ShellWorkspaceViewModel> Workspaces, IReadOnlyList<(string Id, string Label, IReadOnlyList<string> WorkspaceIds)> Modes, IReadOnlyList<PackPanelDeclaration> Panels);
public enum ShellBreakpoint { Compact, Medium, Expanded, Large, ExtraLarge }

public static class ShellChromeContract
{
    public static readonly string[] RailZoneOrder = ["head", "mode", "primary-action", "workspaces", "pinned", "groups", "recent", "suggested", "footer"];
    public static readonly int[] MaterialBreakpoints = [600, 840, 1200, 1600];
    public static ShellBreakpoint Breakpoint(int width) => width switch { >= 1600 => ShellBreakpoint.ExtraLarge, >= 1200 => ShellBreakpoint.Large, >= 840 => ShellBreakpoint.Expanded, >= 600 => ShellBreakpoint.Medium, _ => ShellBreakpoint.Compact };
    public static string BreakpointName(ShellBreakpoint value) => value switch { ShellBreakpoint.ExtraLarge => "extra-large", _ => value.ToString().ToLowerInvariant() };
    public static readonly IReadOnlyDictionary<string, string> Shortcuts = new Dictionary<string, string>
    {
        ["find"] = "Mod+K", ["create"] = "Mod+N", ["rail"] = "Mod+\\", ["inspector"] = "Mod+Shift+I", ["workspace"] = "Mod+1..9"
    };
    // RailWidth and ContentFloor are spec-owned taste, not values derived from published guidance.
    public const int BarHeight = 34;
    public const int RailWidth = 216;
    public const int RailMinimum = 120;
    public const int ContentFloor = 420;
    // chrome-spec 6:174 - the shell header is 33px; the toolbar and footer are the body's other content-sized slots.
    public const int PanelHeaderHeight = 33;
    public const int PanelToolbarHeight = 32;
    public const int PanelFooterHeight = 28;
    // chrome-spec 6:188-190 - "The header has exactly two forms ... Only panels that open one item take the second."
    public static string PanelHeaderForm(PackPanelDeclaration panel) => panel.Traits?.Contains("OpensOne") == true ? "toggle-chip" : "title";
    // chrome-spec 6:218 - "the overflow is earned by OpensOne or Consequential"; every other header is three affordances.
    public static bool PanelEarnsOverflow(PackPanelDeclaration panel) => panel.Traits?.Contains("OpensOne") == true || panel.Traits?.Contains("Consequential") == true;
    // chrome-spec 6:236-238 - the minimum is the content-sized slots ABOVE the one flexible slot; the body,
    // which is allowed to scroll, contributes nothing to it.
    public static int PanelSlotMinimum(PackPanelDeclaration panel) => PanelHeaderHeight + PanelToolbarHeight + (panel.Footer is null ? 0 : PanelFooterHeight);
    public static int PanelMinimumHeight(PackPanelDeclaration panel) => Math.Max(panel.MinimumHeight, PanelSlotMinimum(panel));
    /// <summary>Half-up rounding (floor(x + 0.5)) - the same rule the React projection's drags use.</summary>
    public static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);
    public static string Address(string kind, string id) => $"/{Uri.EscapeDataString(kind.Trim('/'))}/{Uri.EscapeDataString(id)}";
}
