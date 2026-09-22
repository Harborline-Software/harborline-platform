using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;

using Harborline.Foundation.RuleEngine.Context;

namespace Harborline.Foundation.RuleEngine.Graph;

/// <summary>Explicit, bounded JSON-text capture for one reactive field value.</summary>
public sealed class RuleInputValue
{
    private readonly JsonNode? _value;

    private RuleInputValue(JsonNode? value) => _value = value;

    /// <summary>Parses and owns one JSON value outside graph evaluation.</summary>
    public static RuleInputValue FromJsonText(string jsonText)
        => new(RuleInstance.CaptureParsedValue(RuntimeInputEnvelope.Parse(jsonText, nameof(jsonText))));

    internal JsonNode? CloneOwned() => RuleInstance.CloneTrusted(_value);
}

/// <summary>One child-table row in a <see cref="RuleInstance"/>.</summary>
public sealed class RuleRow
{
    public RuleRow(string id, IReadOnlyDictionary<string, JsonNode?> fields)
    {
        Id = id;
        Fields = fields.ToDictionary(pair => pair.Key, pair => RuleInstance.CaptureHostValue(pair.Value), StringComparer.Ordinal);
    }

    /// <summary>Stable row id (the row's <c>_id</c> property, else its ordinal at load).</summary>
    public string Id { get; }

    /// <summary>The row's cell values, keyed by field name.</summary>
    public Dictionary<string, JsonNode?> Fields { get; }

    internal RuleRow CaptureOwned()
    {
        var fields = new Dictionary<string, JsonNode?>();
        foreach (var (key, value) in Fields) fields[key] = RuleInstance.CloneTrusted(value);
        return RuleInstance.CreateCapturedRow(Id, fields);
    }
}

/// <summary>
/// A form instance the rule graph evaluates over (SPINE-1 design §2.1): top-level
/// fields plus child tables (ordered rows). Decoupled from the persistence /
/// FormDefinition shape — the engine operates on this abstract value tree, so it
/// does not depend on the FORM-2 child-table build state.
/// </summary>
public sealed class RuleInstance
{
    private static readonly ConditionalWeakTable<JsonNode, object> TrustedNodes = new();
    private static readonly object TrustedMarker = new();
    private static readonly JsonSerializerOptions EnvelopeJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>Top-level field values, keyed by field name.</summary>
    public Dictionary<string, JsonNode?> Fields { get; } = new();

    /// <summary>Child tables, keyed by section id; each is an ordered list of rows.</summary>
    public Dictionary<string, List<RuleRow>> Tables { get; } = new();

    /// <summary>
    /// Builds an instance from a JSON object: a property whose value is an array of
    /// objects is a child table (section id = property name; row id = each element's
    /// <c>_id</c> string, else its index); every other property is a top-level field.
    /// </summary>
    public static RuleInstance FromJson(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return FromJsonText(json.ToJsonString());
    }

    /// <summary>Captures a bounded JSON object into a runtime-owned graph instance.</summary>
    public static RuleInstance FromJsonText(string jsonText)
    {
        var json = RuntimeInputEnvelope.Parse(jsonText, nameof(jsonText)) as JsonObject
            ?? throw new ArgumentException("Rule instance must be a JSON object.", nameof(jsonText));
        var instance = new RuleInstance();
        foreach (var (key, value) in json)
        {
            if (value is JsonArray arr && arr.Count > 0 && arr.All(e => e is JsonObject))
            {
                var rows = new List<RuleRow>();
                for (int i = 0; i < arr.Count; i++)
                {
                    var rowObj = (JsonObject)arr[i]!;
                    string id = rowObj.TryGetPropertyValue("_id", out var idNode) && idNode is not null
                        ? idNode.GetValue<string>()
                        : i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var fields = new Dictionary<string, JsonNode?>();
                    foreach (var (fk, fv) in rowObj)
                    {
                        if (fk == "_id") continue;
                    fields[fk] = CaptureParsedValue(fv);
                    }
                    rows.Add(new RuleRow(id, fields));
                }
                instance.Tables[key] = rows;
            }
            else
            {
                instance.Fields[key] = CaptureParsedValue(value);
            }
        }
        return instance;
    }

    /// <summary>
    /// Produces the runtime-owned value tree used by a graph. The graph must not retain
    /// aliases to a host instance after its explicit capture boundary.
    /// </summary>
    internal RuleInstance CaptureOwned()
    {
        var captured = new RuleInstance();
        foreach (var (key, value) in Fields) captured.Fields[key] = CloneTrusted(value);
        foreach (var (section, rows) in Tables)
        {
            var table = new List<RuleRow>();
            foreach (var row in rows)
            {
                var fields = new Dictionary<string, JsonNode?>();
                foreach (var (key, value) in row.Fields) fields[key] = CloneTrusted(value);
                table.Add(CreateCapturedRow(row.Id, fields));
            }
            captured.Tables[section] = table;
        }
        return captured;
    }

    /// <summary>Validates the complete prospective graph state against the JSON capture envelope.</summary>
    internal void EnsureEnvelope()
    {
        var json = new JsonObject();
        foreach (var (key, value) in Fields) json[key] = CloneTrusted(value);
        foreach (var (section, rows) in Tables)
        {
            var table = new JsonArray();
            foreach (var row in rows)
            {
                var rowObject = new JsonObject { ["_id"] = row.Id };
                foreach (var (key, value) in row.Fields) rowObject[key] = CloneTrusted(value);
                table.Add(rowObject);
            }
            json[section] = table;
        }
        _ = RuntimeInputEnvelope.Parse(json.ToJsonString(EnvelopeJson), nameof(RuleInstance));
    }

    /// <summary>Explicit host adaptation for a JSON node; never called by graph evaluation.</summary>
    internal static JsonNode? CaptureHostValue(JsonNode? value)
        => value is null ? null : CaptureParsedValue(RuntimeInputEnvelope.Parse(value.ToJsonString(), nameof(value)));

    internal static RuleRow CreateCapturedRow(string id, Dictionary<string, JsonNode?> fields)
    {
        var row = new RuleRow(id, new Dictionary<string, JsonNode?>());
        foreach (var (key, value) in fields) row.Fields[key] = value;
        return row;
    }

    internal static JsonNode? CaptureParsedValue(JsonNode? value)
    {
        if (value is not null) MarkTrusted(value);
        return value?.DeepClone() is { } clone ? MarkAndReturn(clone) : null;
    }

    internal static JsonNode? CloneTrusted(JsonNode? value)
    {
        if (value is null) return null;
        EnsureTrusted(value);
        return MarkAndReturn(value.DeepClone());
    }

    private static JsonNode MarkAndReturn(JsonNode value)
    {
        MarkTrusted(value);
        return value;
    }

    private static void MarkTrusted(JsonNode node)
    {
        TrustedNodes.GetValue(node, _ => TrustedMarker);
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj) if (child is not null) MarkTrusted(child);
                break;
            case JsonArray array:
                foreach (var child in array) if (child is not null) MarkTrusted(child);
                break;
        }
    }

    private static void EnsureTrusted(JsonNode node)
    {
        if (!TrustedNodes.TryGetValue(node, out _))
            throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj) if (child is not null) EnsureTrusted(child);
                break;
            case JsonArray array:
                foreach (var child in array) if (child is not null) EnsureTrusted(child);
                break;
        }
    }
}
