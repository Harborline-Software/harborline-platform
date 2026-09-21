using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Harborline.Blocks.EntityViews;

/// <summary>Canonical Layout kind identities consumed by Views.</summary>
public static class ViewKindIds
{
    /// <summary>The canonical tabular Layout kind.</summary>
    public const string Table = "layout.table";
}

/// <summary>An immutable registry of the Layout view kinds admitted by a host.</summary>
public sealed class ViewKindRegistry : IViewKindRegistry
{
    private static readonly ViewKindDescriptor PlatformTable = new(
        ViewKindIds.Table,
        "hlp.ui.data-grid",
        [ViewShapeRole.Title]);

    private readonly FrozenDictionary<string, ViewKindDescriptor> _byKind;
    private readonly ReadOnlyCollection<ViewKindDescriptor> _listed;

    /// <summary>Creates a registry from exact, unique kind identities.</summary>
    public ViewKindRegistry(IEnumerable<ViewKindDescriptor> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        var byKind = new Dictionary<string, ViewKindDescriptor>(StringComparer.Ordinal);
        var listed = new List<ViewKindDescriptor>();
        foreach (var descriptor in kinds)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (string.IsNullOrWhiteSpace(descriptor.Kind)
                || string.IsNullOrWhiteSpace(descriptor.Renderer)
                || descriptor.RequiredRoles is null)
            {
                throw new ArgumentException(
                    "A view kind registration requires a kind, renderer, and role contract.",
                    nameof(kinds));
            }

            var normalized = descriptor with
            {
                RequiredRoles = Array.AsReadOnly(descriptor.RequiredRoles.ToArray()),
            };

            if (!byKind.TryAdd(normalized.Kind, normalized))
            {
                throw new ArgumentException(
                    $"The view kind '{descriptor.Kind}' is registered more than once.",
                    nameof(kinds));
            }

            listed.Add(normalized);
        }

        _byKind = byKind.ToFrozenDictionary(StringComparer.Ordinal);
        _listed = listed.AsReadOnly();
    }

    /// <summary>The canonical kinds shipped by the platform.</summary>
    public static ViewKindRegistry Platform { get; } = new([PlatformTable]);

    /// <inheritdoc />
    public ValueTask<ViewKindDescriptor?> ResolveAsync(
        string kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return ValueTask.FromResult(_byKind.GetValueOrDefault(kind));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<ViewKindDescriptor>>(_listed);
    }
}
