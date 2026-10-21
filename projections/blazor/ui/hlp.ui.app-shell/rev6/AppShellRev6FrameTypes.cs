using Harborline.UIAdapters.Blazor.Components.DataDisplay;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>The controlled frame composition.</summary>
public enum AppShellRev6FrameMode {
    /// <summary>Conversation-oriented navigation.</summary>
    Chat,
    /// <summary>Destination-oriented navigation.</summary>
    Workspace
}
/// <summary>The requested face identity; only the admitted structured table has a renderer.</summary>
public enum AppShellRev6FrameFaceKind {
    /// <summary>Home; unsupported in this bounded renderer.</summary>
    Home,
    /// <summary>Conversation; unsupported in this bounded renderer.</summary>
    Conversation,
    /// <summary>An existing Platform structured view.</summary>
    Structured
}
/// <summary>A fixed navigation zone, rendered in mode order.</summary>
public enum AppShellRev6FrameRailGroup {
    /// <summary>The supplied My work rows.</summary>
    MyWork,
    /// <summary>Primary destinations.</summary>
    Primary,
    /// <summary>Secondary destinations.</summary>
    Secondary,
    /// <summary>Conversations.</summary>
    Conversations,
    /// <summary>Browse in Chat mode.</summary>
    Browse
}

/// <summary>An identity-derived navigation href with its supplied label and count.</summary>
public sealed record AppShellRev6FrameLink(
    string Id, string Label, string Href, string IconKey,
    bool Current, int? Count = null);
/// <summary>A navigation link in a fixed mode zone.</summary>
public sealed record AppShellRev6FrameRailItem(
    AppShellRev6FrameRailGroup Group, AppShellRev6FrameLink Link);
/// <summary>Tenant Home, ordered mode items and the final account identity.</summary>
public sealed record AppShellRev6FrameRail(
    string TenantText, string TenantLabel, string HomeHref,
    AppShellRev6FrameRailItem[] Items, string AccountText, string AccountLabel);
/// <summary>An ordered section of caller-supplied sidebar links.</summary>
public sealed record AppShellRev6FrameSidebarSection(
    string Id, string Label, AppShellRev6FrameLink[] Items);
/// <summary>The current destination title, scope and sections.</summary>
public sealed record AppShellRev6FrameSidebar(
    string Title, string? ScopeLabel, AppShellRev6FrameSidebarSection[] Sections);
/// <summary>An action with caller-supplied availability and reason.</summary>
public sealed record AppShellRev6FrameAction(
    string Id, string Label, bool Enabled, string? DisabledReason);
/// <summary>The current title, breadcrumb, search trigger and actions.</summary>
public sealed record AppShellRev6FrameHeader(
    string Title, string Breadcrumb, string SearchLabel,
    string SearchPlaceholder, AppShellRev6FrameAction[] Actions);
/// <summary>Current-owner data for the fixed Platform structured-view route.</summary>
public sealed record AppShellRev6FrameMainFace(
    AppShellRev6Owner Owner, AppShellRev6FrameFaceKind Kind, string? Target,
    ViewRenderPlan? Plan = null, IReadOnlyList<ViewRuntimeRow>? Rows = null,
    string AccessibleLabel = "View results", string? Empty = null,
    bool ActionsDisabled = false);
/// <summary>One controlled current-owner frame snapshot and existing tab presentation.</summary>
public sealed record AppShellRev6FramePresentation(
    AppShellRev6Owner Owner, AppShellRev6FrameMode Mode,
    AppShellRev6FrameRail Rail, AppShellRev6FrameSidebar? Sidebar,
    AppShellRev6FrameHeader Header, AppShellRev6FrameMainFace MainFace,
    AppShellRev6Presentation Tabs, bool PanelOpen,
    bool RailLabels = false, bool FocusMode = false, bool IsSystemPage = false);

/// <summary>A request carrying the owner captured by the rendered control.</summary>
public abstract record AppShellRev6FrameIntent(AppShellRev6Owner Owner);
/// <summary>Requests activation of a supplied navigation item.</summary>
public sealed record AppShellRev6FrameNavigate(
    AppShellRev6Owner Owner, string ItemId) : AppShellRev6FrameIntent(Owner);
/// <summary>Requests a compiled shell chrome command.</summary>
public sealed record AppShellRev6FrameCommand(
    AppShellRev6Owner Owner, string CommandId) : AppShellRev6FrameIntent(Owner);
/// <summary>Requests a supplied header or view action.</summary>
public sealed record AppShellRev6FrameActionRequested(
    AppShellRev6Owner Owner, string ActionId) : AppShellRev6FrameIntent(Owner);
/// <summary>Requests a controlled panel visibility change.</summary>
public sealed record AppShellRev6FrameSetPanelOpen(
    AppShellRev6Owner Owner, bool Open) : AppShellRev6FrameIntent(Owner);
/// <summary>Forwards an unchanged native tab request with its rendered owner.</summary>
public sealed record AppShellRev6FrameTabIntent(
    AppShellRev6Owner Owner, AppShellRev6Intent Value) : AppShellRev6FrameIntent(Owner);
/// <summary>Requests activation of an existing view row.</summary>
public sealed record AppShellRev6FrameRowActivated(
    AppShellRev6Owner Owner, string RowId) : AppShellRev6FrameIntent(Owner);
