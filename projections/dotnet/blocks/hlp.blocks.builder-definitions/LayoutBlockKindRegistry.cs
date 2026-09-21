using System.Collections.Frozen;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The developer-supplied closed vocabulary of block kinds admitted by a host.</summary>
public sealed class LayoutBlockKindRegistry
{
    private readonly FrozenSet<string> _kinds;

    /// <summary>Creates an immutable register from the host's supported kind identifiers.</summary>
    /// <param name="kinds">The supported, nonempty identifiers.</param>
    public LayoutBlockKindRegistry(IEnumerable<string> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds.ToFrozenSet(StringComparer.Ordinal);
        if (_kinds.Count == 0 || _kinds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A block register requires nonempty kind identifiers.", nameof(kinds));
    }

    /// <summary>Gets the platform's built-in surface grammar.</summary>
    public static LayoutBlockKindRegistry Platform { get; } = new(new[]
    {
        "layout.stack", "layout.flow", "layout.areas", "layout.field", "layout.table",
        "layout.list", "layout.board", "layout.calendar", "layout.map", "layout.dashboard-widget",
        "layout.file-library", "layout.metric", "layout.text", "layout.document", "layout.form",
    });

    /// <summary>Returns whether the host registered the kind.</summary>
    /// <param name="kind">The exact kind identifier.</param>
    /// <returns>Whether the kind is present.</returns>
    public bool Contains(string kind) => _kinds.Contains(kind);
}
