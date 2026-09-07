using Harborline.Foundation.Builder;

namespace Harborline.Foundation.Theming;

/// <summary>Authored semantic CSS custom-property references for a builder lens tone.</summary>
public sealed record ToneStyle(string Swatch, string Border, string SoftBackground, string Text);

/// <summary>Projection-neutral mapping from the closed lens-tone vocabulary to theme tokens.</summary>
public static class ToneStyles
{
    /// <summary>Resolves one tone without consulting host state.</summary>
    public static ToneStyle Resolve(LensTone tone) => tone switch
    {
        LensTone.Accent or LensTone.SensitivityLow => Accent,
        LensTone.Warning or LensTone.SensitivityMedium => Warning,
        LensTone.Danger or LensTone.SensitivityHigh => Danger,
        LensTone.Success => Success,
        LensTone.Info => Info,
        LensTone.Muted or LensTone.SensitivityNone => Muted,
        _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, "Unknown lens tone."),
    };

    private static readonly ToneStyle Accent = new(
        "var(--color-accent)", "var(--color-accent)",
        "var(--color-accent-soft)", "var(--color-accent-foreground)");
    private static readonly ToneStyle Warning = new(
        "var(--color-warning)", "var(--color-warning)",
        "var(--color-warning-soft)", "var(--color-warning)");
    private static readonly ToneStyle Danger = new(
        "var(--color-danger)", "var(--color-danger)",
        "var(--color-danger-soft)", "var(--color-danger)");
    private static readonly ToneStyle Success = new(
        "var(--color-success)", "var(--color-success)",
        "var(--color-success-soft)", "var(--color-success)");
    private static readonly ToneStyle Info = new(
        "var(--color-secondary)", "var(--color-secondary)",
        "var(--color-muted)", "var(--color-secondary-foreground)");
    private static readonly ToneStyle Muted = new(
        "var(--color-muted-foreground)", "var(--color-border)",
        "var(--color-muted)", "var(--color-muted-foreground)");
}
