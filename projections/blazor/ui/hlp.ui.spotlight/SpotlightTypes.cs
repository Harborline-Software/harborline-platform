using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Navigation;

/// <summary>One command in the spotlight palette: label, action, description, icon, shortcut and badge.</summary>
public sealed record SpotlightItem(
    string Id,
    string Label,
    Func<Task> OnSelect,
    string? Description = null,
    RenderFragment? Icon = null,
    string? Shortcut = null,
    string? Badge = null,
    bool KeepOpen = false);

/// <summary>A titled section of spotlight items, which can show a loading state.</summary>
public sealed record SpotlightSection(
    string Id,
    string Label,
    IReadOnlyList<SpotlightItem> Items,
    bool Loading = false,
    string? LoadingLabel = null);
