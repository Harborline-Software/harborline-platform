namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>One caller-owned option in a radio group.</summary>
public sealed record HarborlineRadioOption(string Value, string Label, string? Description = null, bool Disabled = false);

/// <summary>Presentation-only option flow.</summary>
public enum RadioGroupOrientation { Vertical, Horizontal }
