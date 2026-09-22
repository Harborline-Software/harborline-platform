using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.RuleEngine.Context;

/// <summary>
/// Runtime-owned inert values captured before pure rule evaluation. Host adapters and
/// dictionaries are deliberately not accepted by the evaluator entry point.
/// </summary>
public sealed class RuleContextSnapshot
{
    private readonly IReadOnlyDictionary<string, JsonNode?> _root;
    private readonly IReadOnlyDictionary<string, JsonNode?>? _row;

    private RuleContextSnapshot(IReadOnlyDictionary<string, JsonNode?> root, IReadOnlyDictionary<string, JsonNode?>? row)
    {
        _root = root;
        _row = row;
    }

    /// <summary>Captures JSON data by value outside the evaluator.</summary>
    public static RuleContextSnapshot Capture(
        IReadOnlyDictionary<string, JsonNode?> root,
        IReadOnlyDictionary<string, JsonNode?>? row = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new RuleContextSnapshot(Clone(root), row is null ? null : Clone(row));
    }

    internal IValueResolver CreateResolver(RuleEvalScope scope) => new SnapshotResolver(_root, _row, scope);

    private static IReadOnlyDictionary<string, JsonNode?> Clone(IReadOnlyDictionary<string, JsonNode?> source)
        => source.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.Ordinal);
}

internal sealed class SnapshotResolver(
    IReadOnlyDictionary<string, JsonNode?> root,
    IReadOnlyDictionary<string, JsonNode?>? row,
    RuleEvalScope scope) : IValueResolver
{
    public RefValue ResolveVar(string path)
    {
        if (path.StartsWith("row.", StringComparison.Ordinal))
        {
            if (row is null || scope.RowSection is null || scope.RowId is null)
                return RefValue.OfError(RuleError.Of(RuleEngineCodes.BadReference, "path", path));
            return Read(row, path["row.".Length..], path);
        }
        var name = path.StartsWith("field.", StringComparison.Ordinal) ? path["field.".Length..] : path;
        if (!path.StartsWith("field.", StringComparison.Ordinal) && row is not null && row.TryGetValue(name, out var local))
            return PendingSentinel.Is(local) ? RefValue.Pending : RefValue.Resolved(local);
        return root.TryGetValue(name, out var value)
            ? (PendingSentinel.Is(value) ? RefValue.Pending : RefValue.Resolved(value))
            : RefValue.Resolved(null);
    }

    public RefValue ResolveAgg(string fn, string section, string col) => RefValue.UnavailableAggregate(fn, section, col);

    private static RefValue Read(IReadOnlyDictionary<string, JsonNode?> values, string name, string path)
        => values.TryGetValue(name, out var value)
            ? (PendingSentinel.Is(value) ? RefValue.Pending : RefValue.Resolved(value))
            : RefValue.OfError(RuleError.Of(RuleEngineCodes.BadReference, "path", path));
}
