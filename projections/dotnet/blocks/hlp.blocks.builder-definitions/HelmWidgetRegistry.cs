using System.Collections.Frozen;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>One registered Helm widget that a dashboard Layout block may name.</summary>
public sealed record HelmWidgetDescriptor(string Id, string Label);

/// <summary>
/// The library-neutral Helm composition seam. Layout authors choose a registered identifier;
/// neither this registry nor Layout owns a dashboard shell or a widget implementation.
/// </summary>
public interface IHelmWidgetRegistry
{
    /// <summary>Gets the immutable registered widget descriptors.</summary>
    IReadOnlyList<HelmWidgetDescriptor> Widgets { get; }

    /// <summary>Looks up one exact registered widget identifier.</summary>
    HelmWidgetDescriptor? Resolve(string id);
}

/// <summary>Immutable developer-composed Helm widget registry for the Layout producer.</summary>
public sealed class HelmWidgetRegistry : IHelmWidgetRegistry
{
    private readonly FrozenDictionary<string, HelmWidgetDescriptor> _widgets;

    /// <summary>Creates a registry from nonempty, unique stable widget identifiers.</summary>
    public HelmWidgetRegistry(IEnumerable<HelmWidgetDescriptor> widgets)
    {
        ArgumentNullException.ThrowIfNull(widgets);
        var values = widgets.ToArray();
        if (values.Length == 0 || values.Any(widget => widget is null || string.IsNullOrWhiteSpace(widget.Id) || string.IsNullOrWhiteSpace(widget.Label))
            || values.Select(widget => widget.Id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("A Helm widget registry requires unique nonempty descriptors.", nameof(widgets));
        _widgets = values.ToFrozenDictionary(widget => widget.Id, StringComparer.Ordinal);
        Widgets = Array.AsReadOnly(values);
    }

    /// <inheritdoc />
    public IReadOnlyList<HelmWidgetDescriptor> Widgets { get; }

    /// <inheritdoc />
    public HelmWidgetDescriptor? Resolve(string id) =>
        !string.IsNullOrWhiteSpace(id) && _widgets.TryGetValue(id, out var widget) ? widget : null;
}
