using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Buttons;

public enum SegmentedControlSize { Sm, Md, Lg, Touch }

public sealed record SegmentedOption(
    string Value,
    RenderFragment LabelContent,
    string? AccessibleLabel = null,
    bool Disabled = false,
    string? AutomationId = null)
{
    public SegmentedOption(string value, string label, bool disabled = false, string? accessibleLabel = null, string? automationId = null)
        : this(value, builder => builder.AddContent(0, label), accessibleLabel, disabled, automationId) { }
}
