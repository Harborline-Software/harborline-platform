using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
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
        : this(id, fields.ToDictionary(pair => pair.Key, pair => RuleInstance.CaptureHostValue(pair.Value), StringComparer.Ordinal), hasExplicitId: true)
    {
    }

    internal RuleRow(string id, Dictionary<string, JsonNode?> fields, bool hasExplicitId)
    {
        Id = id;
        Fields = fields;
        HasExplicitId = hasExplicitId;
    }

    /// <summary>Stable row id (the row's <c>_id</c> property, else its ordinal at load).</summary>
    public string Id { get; }

    /// <summary>The row's cell values, keyed by field name.</summary>
    public Dictionary<string, JsonNode?> Fields { get; }

    internal bool HasExplicitId { get; }

    internal RuleRow CaptureOwned()
    {
        // Fields remain publicly mutable. Validate every current descendant before any
        // clone can promote it into runtime-owned state.
        var prospective = new RuleInstance();
        foreach (var (key, value) in Fields) prospective.Fields[key] = value;
        prospective.EnsureEnvelope();
        var fields = new Dictionary<string, JsonNode?>();
        foreach (var (key, value) in Fields) fields[key] = RuleInstance.CloneTrusted(value);
        return RuleInstance.CreateCapturedRow(Id, fields, HasExplicitId);
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
        ValidateHostObject(json);
        return CaptureParsedObject(json.DeepClone().AsObject());
    }

    /// <summary>Captures a bounded JSON object into a runtime-owned graph instance.</summary>
    public static RuleInstance FromJsonText(string jsonText)
    {
        var json = RuntimeInputEnvelope.Parse(jsonText, nameof(jsonText)) as JsonObject
            ?? throw new ArgumentException("Rule instance must be a JSON object.", nameof(jsonText));
        return CaptureParsedObject(json);
    }

    private static RuleInstance CaptureParsedObject(JsonObject json)
    {
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
                    rows.Add(CreateCapturedRow(id, fields, idNode is not null));
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
        PreflightStructure();
        var captured = new RuleInstance();
        foreach (var (key, value) in Fields) captured.Fields[key] = CloneTrusted(value);
        foreach (var (section, rows) in Tables)
        {
            var table = new List<RuleRow>();
            foreach (var row in rows)
            {
                var fields = new Dictionary<string, JsonNode?>();
                foreach (var (key, value) in row.Fields) fields[key] = CloneTrusted(value);
                table.Add(CreateCapturedRow(row.Id, fields, row.HasExplicitId));
            }
            captured.Tables[section] = table;
        }
        return captured;
    }

    /// <summary>Validates the complete prospective graph state against the JSON capture envelope.</summary>
    internal void EnsureEnvelope()
    {
        PreflightStructure();
    }

    private void PreflightStructure()
    {
        var nodes = 1; // root object
        var composedContainers = new List<JsonNode>();
        void Visit(JsonNode? node, int depth)
        {
            if (node is null) { if (++nodes > RuntimeInputEnvelope.MaxNodes) throw new ArgumentException("Rule context exceeds the node envelope."); return; }
            if (++nodes > RuntimeInputEnvelope.MaxNodes)
                throw new ArgumentException("Rule context exceeds the bounded structure envelope.");
            switch (node)
            {
                case JsonValue:
                    EnsureTrusted(node);
                    break;
                case JsonObject obj:
                    if (depth > RuntimeInputEnvelope.MaxDepth) throw new ArgumentException("Rule context exceeds the bounded structure envelope.");
                    composedContainers.Add(node);
                    foreach (var (key, child) in obj) { RuntimeInputEnvelope.ValidateMemberName(key, nameof(RuleInstance)); Visit(child, depth + 1); }
                    break;
                case JsonArray array:
                    if (depth > RuntimeInputEnvelope.MaxDepth) throw new ArgumentException("Rule context exceeds the bounded structure envelope.");
                    composedContainers.Add(node);
                    foreach (var child in array) Visit(child, depth + 1);
                    break;
                default:
                    throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
            }
        }
        foreach (var (key, value) in Fields) { RuntimeInputEnvelope.ValidateMemberName(key, nameof(RuleInstance)); Visit(value, 2); }
        foreach (var (section, rows) in Tables)
        {
            RuntimeInputEnvelope.ValidateMemberName(section, nameof(RuleInstance));
            if (++nodes > RuntimeInputEnvelope.MaxNodes) throw new ArgumentException("Rule context exceeds the node envelope."); // table array
            foreach (var row in rows)
            {
                RuntimeInputEnvelope.ValidateMemberName(row.Id, nameof(RuleInstance));
                if (++nodes > RuntimeInputEnvelope.MaxNodes) throw new ArgumentException("Rule context exceeds the node envelope."); // row object
                if (row.HasExplicitId && ++nodes > RuntimeInputEnvelope.MaxNodes) throw new ArgumentException("Rule context exceeds the node envelope."); // supplied _id
                foreach (var (key, value) in row.Fields) { RuntimeInputEnvelope.ValidateMemberName(key, nameof(RuleInstance)); Visit(value, 4); }
            }
        }

        var bytes = new JsonStringifyCounter(RuntimeInputEnvelope.MaxUtf8Bytes);
        bytes.Add(1); // root brace
        var first = true;
        void AddMember(string name, JsonNode? value)
        {
            if (!first) bytes.Add(1);
            bytes.CountString(name);
            bytes.Add(1); // colon
            first = false;
            bytes.CountValue(value);
        }
        foreach (var (key, value) in Fields) AddMember(key, value);
        foreach (var (section, rows) in Tables)
        {
            if (!first) bytes.Add(1);
            bytes.CountString(section);
            bytes.Add(2); // colon + opening array bracket
            first = false;
            var firstRow = true;
            foreach (var row in rows)
            {
                if (!firstRow) bytes.Add(1);
                firstRow = false;
                bytes.Add(1); // object opening brace
                var firstRowMember = true;
                void AddRowMember(string name, JsonNode? value)
                {
                    if (!firstRowMember) bytes.Add(1);
                    bytes.CountString(name);
                    bytes.Add(1); // colon
                    firstRowMember = false;
                    bytes.CountValue(value);
                }
                if (row.HasExplicitId) AddRowMember("_id", JsonValue.Create(row.Id));
                foreach (var (key, value) in row.Fields) AddRowMember(key, value);
                bytes.Add(1); // object closing brace
            }
            bytes.Add(1); // array closing brace
        }
        bytes.Add(1); // root object closing brace

        // A host may compose a new object/array from separately captured leaves. Only
        // brand those mutable containers after provenance and the complete envelope pass.
        foreach (var container in composedContainers) TrustedNodes.GetValue(container, _ => TrustedMarker);

    }

    /// <summary>Counts the UTF-8 bytes that ECMAScript <c>JSON.stringify</c> would emit without materializing JSON.</summary>
    private sealed class JsonStringifyCounter
    {
        private readonly int _maximum;
        private int _count;

        public JsonStringifyCounter(int maximum)
        {
            _maximum = maximum;
        }

        public void Add(int count)
        {
            if (count < 0 || count > _maximum - _count)
                throw new ArgumentException("Rule context exceeds the UTF-8 byte envelope.");
            _count += count;
        }

        public void CountValue(JsonNode? node)
        {
            if (node is null) { Add(4); return; }
            switch (node)
            {
                case JsonObject obj:
                    Add(1);
                    var firstObjectMember = true;
                    foreach (var (name, value) in obj)
                    {
                        if (!firstObjectMember) Add(1);
                        CountString(name);
                        Add(1);
                        CountValue(value);
                        firstObjectMember = false;
                    }
                    Add(1);
                    return;
                case JsonArray array:
                    Add(1);
                    var firstElement = true;
                    foreach (var value in array)
                    {
                        if (!firstElement) Add(1);
                        CountValue(value);
                        firstElement = false;
                    }
                    Add(1);
                    return;
                case JsonValue value:
                    CountScalar(value);
                    return;
                default:
                    throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
            }
        }

        public void CountString(string value)
        {
            Add(2); // quotes
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"':
                    case '\\':
                    case '\b':
                    case '\f':
                    case '\n':
                    case '\r':
                    case '\t':
                        Add(2);
                        break;
                    default:
                        if (character < 0x20) Add(6);
                        else if (character < 0x80) Add(1);
                        else if (character < 0x800) Add(2);
                        else if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                        {
                            Add(4);
                            index++;
                        }
                        else if (char.IsSurrogate(character)) Add(6); // well-formed JSON.stringify escapes lone surrogates
                        else Add(3);
                        break;
                }
            }
        }

        private void CountScalar(JsonValue value)
        {
            switch (value.GetValueKind())
            {
                case JsonValueKind.String:
                    CountString(value.GetValue<string>());
                    return;
                case JsonValueKind.True:
                    Add(4);
                    return;
                case JsonValueKind.False:
                    Add(5);
                    return;
                case JsonValueKind.Number:
                    CountNumber(value);
                    return;
                case JsonValueKind.Null:
                    Add(4);
                    return;
                default:
                    throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
            }
        }

        private void CountNumber(JsonValue value)
        {
            // JsonValue preserves the CLR numeric type for safe host captures. Normalize
            // each through the existing ECMAScript number formatter; parsed JSON numbers
            // arrive through the double branch and retain the same rule.
            if (value.TryGetValue<sbyte>(out var signedByte)) { Add(CanonicalNumber.ToJsonString((long)signedByte).Length); return; }
            if (value.TryGetValue<byte>(out var unsignedByte)) { Add(CanonicalNumber.ToJsonString((long)unsignedByte).Length); return; }
            if (value.TryGetValue<short>(out var shortInteger)) { Add(CanonicalNumber.ToJsonString((long)shortInteger).Length); return; }
            if (value.TryGetValue<ushort>(out var unsignedShort)) { Add(CanonicalNumber.ToJsonString((long)unsignedShort).Length); return; }
            if (value.TryGetValue<int>(out var integer)) { Add(CanonicalNumber.ToJsonString((long)integer).Length); return; }
            if (value.TryGetValue<long>(out var longInteger)) { Add(CanonicalNumber.ToJsonString(longInteger).Length); return; }
            if (value.TryGetValue<uint>(out var unsignedInteger)) { Add(CanonicalNumber.ToJsonString((long)unsignedInteger).Length); return; }
            if (value.TryGetValue<ulong>(out var unsignedLong)) { Add(CanonicalNumber.ToJsonString((double)unsignedLong).Length); return; }
            if (value.TryGetValue<float>(out var single)) { CountDouble(single); return; }
            if (value.TryGetValue<double>(out var number)) { CountDouble(number); return; }
            if (value.TryGetValue<decimal>(out var decimalNumber)) { CountDouble((double)decimalNumber); return; }
            throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
        }

        private void CountDouble(double number)
        {
            if (!double.IsFinite(number)) Add(4); // JSON.stringify serializes non-finite Numbers as null.
            else Add(CanonicalNumber.ToJsonString(number).Length);
        }
    }

    /// <summary>Explicit host adaptation for a JSON node; never called by graph evaluation.</summary>
    internal static JsonNode? CaptureHostValue(JsonNode? value)
    {
        if (value is null) return null;
        if (value is not JsonValue jsonValue || !IsSafeHostValue(jsonValue))
            throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
        MeasureHostValue(value);
        return CaptureParsedValue(value);
    }

    internal static RuleRow CreateCapturedRow(string id, Dictionary<string, JsonNode?> fields, bool hasExplicitId = true)
        => new(id, fields, hasExplicitId);

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
    }

    private static bool IsSafeHostValue(JsonValue value)
        => !value.GetType().Name.StartsWith("JsonValueCustomized", StringComparison.Ordinal)
            && value.GetValueKind() is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Undefined);

    private static void ValidateHostObject(JsonObject json)
    {
        var nodes = 0;
        void Visit(JsonNode? node, int depth)
        {
            if (++nodes > RuntimeInputEnvelope.MaxNodes)
                throw new ArgumentException("Rule context exceeds the node envelope.", nameof(json));
            switch (node)
            {
                case null:
                    return;
                case JsonValue value when IsSafeHostValue(value):
                    return;
                case JsonValue:
                    throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
                case JsonObject obj:
                    if (depth > RuntimeInputEnvelope.MaxDepth)
                        throw new ArgumentException("Rule context exceeds the bounded structure envelope.", nameof(json));
                    foreach (var (key, child) in obj)
                    {
                        RuntimeInputEnvelope.ValidateMemberName(key, nameof(json));
                        Visit(child, depth + 1);
                    }
                    return;
                case JsonArray array:
                    if (depth > RuntimeInputEnvelope.MaxDepth)
                        throw new ArgumentException("Rule context exceeds the bounded structure envelope.", nameof(json));
                    foreach (var child in array) Visit(child, depth + 1);
                    return;
                default:
                    throw new InvalidOperationException(RuleEngineCodes.ContextSnapshotRequired);
            }
        }

        Visit(json, 1);
        MeasureHostValue(json);
    }

    private static void MeasureHostValue(JsonNode value)
    {
        // Validation always runs first, so this pass never invokes a host converter.
        var counter = new JsonStringifyCounter(RuntimeInputEnvelope.MaxUtf8Bytes);
        counter.CountValue(value);
    }
}
