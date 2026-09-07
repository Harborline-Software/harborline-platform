namespace Harborline.UIAdapters.Blazor.Components.Navigation;

/// <summary>One caller-owned item in an ordered breadcrumb trail.</summary>
public sealed record HarborlineBreadcrumbItem(
    string Id,
    string Label,
    string? Href = null,
    bool? Current = null);
