using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Buttons;

/// <summary>The rendered size of a segmented control.</summary>
public enum SegmentedControlSize
{
    /// <summary>Renders the compact control for dense layouts.</summary>
    Sm,
    /// <summary>Renders the standard control size.</summary>
    Md,
    /// <summary>Renders the large control.</summary>
    Lg,
    /// <summary>Renders the control with touch-sized segments.</summary>
    Touch
}

/// <summary>One choice in a segmented control: value, label content, accessible label and disabled state.</summary>
public sealed record SegmentedOption(
    string Value,
    RenderFragment LabelContent,
    string? AccessibleLabel = null,
    bool Disabled = false,
    string? AutomationId = null)
{
    /// <summary>Creates an option with a plain-text label.</summary>
    public SegmentedOption(string value, string label, bool disabled = false, string? accessibleLabel = null, string? automationId = null)
        : this(value, builder => builder.AddContent(0, label), accessibleLabel, disabled, automationId) { }
}
