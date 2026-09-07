using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Navigation;

public sealed record SpotlightItem(
    string Id,
    string Label,
    Func<Task> OnSelect,
    string? Description = null,
    RenderFragment? Icon = null,
    string? Shortcut = null,
    string? Badge = null,
    bool KeepOpen = false);

public sealed record SpotlightSection(
    string Id,
    string Label,
    IReadOnlyList<SpotlightItem> Items,
    bool Loading = false,
    string? LoadingLabel = null);
