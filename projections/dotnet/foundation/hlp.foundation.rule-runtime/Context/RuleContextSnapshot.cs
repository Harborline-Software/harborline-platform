using System.Text.Json.Nodes;
using System.Text.Json;

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
    // Runtime input envelope, shared with the TS JSON-text capture boundary. These are
    // implementation limits for owned evaluation data, separate from authored AST limits.
    public const int MaxUtf8Bytes = RuntimeInputEnvelope.MaxUtf8Bytes;
    public const int MaxDepth = RuntimeInputEnvelope.MaxDepth;
    public const int MaxNodes = RuntimeInputEnvelope.MaxNodes;
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
        // This is deliberate host capture: it may enumerate the caller's trusted dictionary,
        // but the evaluator receives only a fresh JSON parse with no caller-owned JsonNode alias.
        var envelope = new JsonObject { ["root"] = ToObject(root) };
        if (row is not null) envelope["row"] = ToObject(row);
        return FromJsonText(envelope.ToJsonString());
    }

    /// <summary>Admits one bounded JSON-text context into runtime-owned inert data.</summary>
    public static RuleContextSnapshot FromJsonText(string jsonText)
    {
        ArgumentNullException.ThrowIfNull(jsonText);
        JsonObject envelope;
        try
        {
            envelope = RuntimeInputEnvelope.Parse(jsonText, nameof(jsonText))?.AsObject()
                ?? throw new ArgumentException("Rule context must be a JSON object.", nameof(jsonText));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Rule context is not valid bounded JSON.", nameof(jsonText), exception);
        }
        if (!envelope.TryGetPropertyValue("root", out var rootNode) || rootNode is not JsonObject root)
            throw new ArgumentException("Rule context must contain an object root.", nameof(jsonText));
        JsonObject? row = null;
        if (envelope.TryGetPropertyValue("row", out var rowNode) && rowNode is not null)
            row = rowNode as JsonObject ?? throw new ArgumentException("Rule context row must be an object.", nameof(jsonText));
        return new RuleContextSnapshot(CloneObject(root), row is null ? null : CloneObject(row));
    }

    internal IValueResolver CreateResolver(RuleEvalScope scope) => new SnapshotResolver(_root, _row, scope);

    private static IReadOnlyDictionary<string, JsonNode?> Clone(IReadOnlyDictionary<string, JsonNode?> source)
        => source.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.Ordinal);

    private static JsonObject ToObject(IReadOnlyDictionary<string, JsonNode?> source)
    {
        var result = new JsonObject();
        foreach (var (key, value) in source) result[key] = value?.DeepClone();
        return result;
    }

    private static IReadOnlyDictionary<string, JsonNode?> CloneObject(JsonObject source)
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
