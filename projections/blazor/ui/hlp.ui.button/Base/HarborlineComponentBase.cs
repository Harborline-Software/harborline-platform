using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Base;

/// <summary>Preserves the public base type and host-attribute composition used by earlier source components.</summary>
public abstract class HarborlineComponentBase : ComponentBase, IDisposable
{
    private bool disposed;

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional inline styles for the root element.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Unmatched attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Combines non-empty class names and the consumer class.</summary>
    protected string CombineClasses(params string?[] classes) => string.Join(
        " ",
        classes.Append(Class).Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>Combines non-empty inline styles and the consumer style.</summary>
    protected string CombineStyles(params string?[] styles) => string.Join(
        ";",
        styles.Append(Style).Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>Releases component resources while preserving the legacy public disposal contract.</summary>
    public void Dispose()
    {
        if (!disposed)
        {
            Dispose(disposing: true);
            disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Allows derived components to release managed resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}
