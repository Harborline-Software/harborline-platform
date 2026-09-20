using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Conformance;
using Harborline.Foundation.RuleEngine.Skins;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>
/// Strict source codec. It preserves authored strings and rejects unknown discriminants,
/// duplicate properties and unknown members instead of selecting a default interpretation.
/// </summary>
public static class RuleDefinitionCodec
{
    public static string SerializeCanonical(RuleDefinitionDocument document)
        => Canonical(WriteSource(document));

    /// <summary>Encodes opaque source without the shared store's identity and version metadata.</summary>
    public static string SerializeBody(RuleDefinitionDocument document)
    {
        var source = WriteSource(document);
        var envelope = source["envelope"]!.AsObject();
        envelope.Remove("id");
        envelope.Remove("tenant");
        envelope.Remove("version");
        return Canonical(source);
    }

    private static JsonObject WriteSource(RuleDefinitionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var envelope = document.Envelope;
        CheckProvenance(envelope.Provenance, "/envelope/provenance", 0);
        var json = new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["id"] = envelope.Id,
                ["version"] = envelope.Version,
                ["tenant"] = envelope.Tenant,
                ["cascadeLayer"] = envelope.CascadeLayer,
                ["provenance"] = envelope.Provenance.DeepClone(),
                ["requires"] = new JsonArray(envelope.Requires.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            },
            ["name"] = document.Name,
            ["tier"] = document.Tier.ToString(),
            ["draft"] = WriteDraft(document.Draft),
        };
        return json;
    }

    private static string Canonical(JsonObject json)
    {
        var result = new StringBuilder();
        CanonicalJson.Write(json, result);
        return result.ToString();
    }

    public static RuleIntentResult Parse(string json, RuleIntentPhase phase)
        => ParseCore(json, phase, null);

    /// <summary>Decodes a body using neutral shared metadata; embedded identity fields refuse.</summary>
    public static RuleIntentResult ParseBody(string json, string id, string tenant, string version, RuleIntentPhase phase)
        => ParseCore(json, phase, (id, tenant, version));

    private static RuleIntentResult ParseCore(string json, RuleIntentPhase phase,
        (string Id, string Tenant, string Version)? header)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = RuleEngineLimits.Default.MaxAstNodes * 4,
            });
            CheckDuplicateMembers(parsed.RootElement, "");
            var root = Object(parsed.RootElement, "", "envelope", "name", "tier", "draft");
            var metadata = Object(Member(root, "envelope", ""), "/envelope",
                header is null ? new[] { "id", "version", "tenant", "cascadeLayer", "provenance", "requires" }
                    : new[] { "cascadeLayer", "provenance", "requires" });
            string version = header?.Version ?? String(metadata, "version", "/envelope");
            var provenance = Member(metadata, "provenance", "/envelope");
            if (provenance.ValueKind != JsonValueKind.Object)
                throw Refuse(RuleDefinitionCodes.InvalidDocument, "/envelope/provenance");
            var provenanceObject = JsonNode.Parse(provenance.GetRawText(), documentOptions: new JsonDocumentOptions
                { MaxDepth = RuleEngineLimits.Default.MaxAstNodes * 4 })!.AsObject();
            CheckProvenance(provenanceObject, "/envelope/provenance", 0);
            var envelope = new RuleDefinitionEnvelope(
                header?.Id ?? Nonblank(metadata, "id", "/envelope"), version,
                header?.Tenant ?? Nonblank(metadata, "tenant", "/envelope"), Nonblank(metadata, "cascadeLayer", "/envelope"),
                provenanceObject,
                Array(metadata, "requires", "/envelope").Select((item, i) =>
                    StringValue(item, $"/envelope/requires/{i}")).ToArray());
            var document = new RuleDefinitionDocument(envelope, Nonblank(root, "name", ""),
                EnumValue<RuleDefinitionTier>(root, "tier", "", RuleDefinitionCodes.InvalidTier),
                ReadDraft(Member(root, "draft", "")));
            return new(document, System.Array.Empty<RuleIntentDiagnostic>());
        }
        catch (DefinitionReadException error)
        {
            return new(null, new[] { new RuleIntentDiagnostic(error.Code, error.Pointer, phase) });
        }
        catch (JsonException)
        {
            return new(null, new[] { new RuleIntentDiagnostic(RuleDefinitionCodes.InvalidDocument, "", phase) });
        }
    }

    private static JsonObject WriteDraft(RuleDraft draft)
    {
        var json = new JsonObject
        {
            ["scope"] = draft.Scope.ToString(),
            ["scopeTarget"] = draft.ScopeTarget,
            ["outputType"] = draft.OutputType.ToString(),
        };
        switch (draft)
        {
            case FormulaDraft formula:
                json["kind"] = "Formula";
                json["inputs"] = new JsonArray(formula.Inputs.Select(input => (JsonNode?)new JsonObject
                {
                    ["id"] = input.Id, ["ref"] = input.Ref, ["type"] = input.Type.ToString(),
                }).ToArray());
                json["expression"] = formula.Expression is null ? null : WriteExpression(formula.Expression, 0);
                break;
            case DecisionTableDraft table:
                json["kind"] = "Table";
                json["hitPolicy"] = table.HitPolicy.ToString();
                json["columns"] = new JsonArray(table.Columns.Select(column => (JsonNode?)new JsonObject
                {
                    ["id"] = column.Id, ["input"] = column.Input, ["valueType"] = column.ValueType.ToString(),
                }).ToArray());
                json["rows"] = new JsonArray(table.Rows.Select(row =>
                {
                    var cells = new JsonObject();
                    foreach (var (key, cell) in row.Cells) cells[key] = WriteCell(cell);
                    return (JsonNode?)new JsonObject
                    {
                        ["id"] = row.Id, ["priority"] = row.Priority, ["output"] = row.Output, ["cells"] = cells,
                    };
                }).ToArray());
                json["noMatch"] = table.NoMatch switch
                {
                    NoMatchPosture.Default value => new JsonObject { ["kind"] = "Default", ["value"] = value.Value },
                    NoMatchPosture.CatchAll => new JsonObject { ["kind"] = "CatchAll" },
                    _ => throw Refuse(SkinCodes.NoMatchUnresolved, "/draft/noMatch"),
                };
                break;
            default: throw Refuse(RuleDefinitionCodes.InvalidDocument, "/draft/kind");
        }
        return json;
    }

    private static JsonObject WriteCell(TableCell cell) => cell switch
    {
        TableCell.Any => new() { ["kind"] = "Any" },
        TableCell.Range range => new() { ["kind"] = "Range", ["lo"] = range.Lo, ["hi"] = range.Hi },
        TableCell.Compare compare => new() { ["kind"] = "Compare", ["op"] = compare.Op, ["value"] = compare.Value },
        _ => throw Refuse(RuleDefinitionCodes.InvalidCellKind, "/draft/rows"),
    };

    private static JsonObject WriteExpression(FormulaExpr expression, int depth)
    {
        if (depth > RuleEngineLimits.Default.MaxAstNodes)
            throw Refuse(RuleEngineCodes.CompileAstTooLarge, "/draft/expression");
        return expression switch
        {
            FormulaExpr.Ref value => new() { ["kind"] = "Ref", ["name"] = value.Name },
            FormulaExpr.Literal value => new()
            {
                ["kind"] = "Literal", ["value"] = value.Value, ["valueType"] = value.ValueType.ToString(),
            },
            FormulaExpr.Binary value => new()
            {
                ["kind"] = "Binary", ["op"] = value.Op,
                ["left"] = WriteExpression(value.Left, depth + 1), ["right"] = WriteExpression(value.Right, depth + 1),
            },
            FormulaExpr.If value => new()
            {
                ["kind"] = "If",
                ["when"] = new JsonObject
                {
                    ["op"] = value.When.Op, ["left"] = WriteExpression(value.When.Left, depth + 1),
                    ["right"] = WriteExpression(value.When.Right, depth + 1),
                },
                ["then"] = WriteExpression(value.Then, depth + 1), ["else"] = WriteExpression(value.Else, depth + 1),
            },
            _ => throw Refuse(RuleDefinitionCodes.InvalidDocument, "/draft/expression"),
        };
    }

    private static RuleDraft ReadDraft(JsonElement node)
    {
        const string pointer = "/draft";
        string kind = String(node, "kind", pointer);
        if (kind is not ("Formula" or "Table")) throw Refuse(RuleDefinitionCodes.InvalidDocument, pointer + "/kind");
        Object(node, pointer, kind == "Formula"
            ? new[] { "kind", "scope", "scopeTarget", "outputType", "inputs", "expression" }
            : new[] { "kind", "scope", "scopeTarget", "outputType", "hitPolicy", "columns", "rows", "noMatch" });
        var scope = EnumValue<RuleScope>(node, "scope", pointer, RuleDefinitionCodes.InvalidScope);
        string target = String(node, "scopeTarget", pointer);
        var action = EnumValue<RuleActionKind>(node, "outputType", pointer, RuleDefinitionCodes.InvalidAction);
        if (kind == "Formula")
        {
            var inputs = Array(node, "inputs", pointer).Select((input, i) =>
            {
                string location = pointer + $"/inputs/{i}";
                Object(input, location, "id", "ref", "type");
                return new FormulaInputDecl(Nonblank(input, "id", location), Nonblank(input, "ref", location),
                    EnumValue<ColumnValueType>(input, "type", location));
            }).ToArray();
            var expression = Member(node, "expression", pointer);
            return new FormulaDraft
            {
                Scope = scope, ScopeTarget = target, OutputType = action, Inputs = inputs,
                Expression = expression.ValueKind == JsonValueKind.Null ? null : ReadExpression(expression, pointer + "/expression", 0),
            };
        }
        var columns = Array(node, "columns", pointer).Select((column, i) =>
        {
            string location = pointer + $"/columns/{i}";
            Object(column, location, "id", "input", "valueType");
            return new ConditionColumn(Nonblank(column, "id", location), Nonblank(column, "input", location),
                EnumValue<ColumnValueType>(column, "valueType", location));
        }).ToArray();
        var rows = Array(node, "rows", pointer).Select((row, i) =>
        {
            string location = pointer + $"/rows/{i}";
            Object(row, location, "id", "cells", "output", "priority");
            var cellObject = Member(row, "cells", location);
            RequireKind(cellObject, JsonValueKind.Object, location + "/cells");
            var cells = new Dictionary<string, TableCell>(StringComparer.Ordinal);
            foreach (var cell in cellObject.EnumerateObject())
            {
                string cellLocation = location + "/cells/" + Escape(cell.Name);
                var column = columns.FirstOrDefault(item => string.Equals(item.Id, cell.Name, StringComparison.Ordinal));
                if (column is null) throw Refuse(SkinCodes.DecisionTableBadCell, cellLocation);
                var decoded = ReadCell(cell.Value, cellLocation);
                if (decoded is TableCell.Compare comparison)
                    ValidateTypedValue(comparison.Value, column.ValueType, cellLocation + "/value", SkinCodes.DecisionTableBadCell);
                cells.Add(cell.Name, decoded);
            }
            var priority = Member(row, "priority", location);
            if (priority.ValueKind != JsonValueKind.Number || !priority.TryGetInt32(out int number))
                throw Refuse(RuleDefinitionCodes.InvalidDocument, location + "/priority");
            return new TableRow(Nonblank(row, "id", location), cells, String(row, "output", location), number);
        }).ToArray();
        var noMatch = Member(node, "noMatch", pointer);
        string posture = String(noMatch, "kind", pointer + "/noMatch");
        NoMatchPosture result;
        if (posture == "Default")
        {
            Object(noMatch, pointer + "/noMatch", "kind", "value");
            result = new NoMatchPosture.Default(String(noMatch, "value", pointer + "/noMatch"));
        }
        else if (posture == "CatchAll")
        {
            Object(noMatch, pointer + "/noMatch", "kind");
            result = new NoMatchPosture.CatchAll();
        }
        else throw Refuse(SkinCodes.NoMatchUnresolved, pointer + "/noMatch/kind");
        return new DecisionTableDraft
        {
            Scope = scope, ScopeTarget = target, OutputType = action, Columns = columns, Rows = rows, NoMatch = result,
            HitPolicy = EnumValue<HitPolicy>(node, "hitPolicy", pointer, SkinCodes.DecisionTableInvalidHitPolicy),
        };
    }

    private static TableCell ReadCell(JsonElement node, string pointer)
    {
        string kind = String(node, "kind", pointer);
        switch (kind)
        {
            case "Any": Object(node, pointer, "kind"); return new TableCell.Any();
            case "Compare":
                Object(node, pointer, "kind", "op", "value");
                string op = String(node, "op", pointer);
                if (!DecisionCell.CompareOps.Contains(op)) throw Refuse(SkinCodes.DecisionTableBadCell, pointer + "/op");
                return new TableCell.Compare(op, String(node, "value", pointer));
            case "Range":
                Object(node, pointer, "kind", "lo", "hi");
                return new TableCell.Range(Endpoint(node, "lo", pointer), Endpoint(node, "hi", pointer));
            default: throw Refuse(RuleDefinitionCodes.InvalidCellKind, pointer + "/kind");
        }
    }

    private static string Endpoint(JsonElement node, string member, string pointer)
    {
        string text = String(node, member, pointer);
        if (text.Trim().Length > 0 && !double.IsFinite(JsNumberMirror.ToNumber(text)))
            throw Refuse(RuleDefinitionCodes.InvalidNumericEndpoint, pointer + "/" + member);
        return text;
    }

    private static FormulaExpr ReadExpression(JsonElement node, string pointer, int depth)
    {
        if (depth > RuleEngineLimits.Default.MaxAstNodes)
            throw Refuse(RuleEngineCodes.CompileAstTooLarge, "/draft/expression");
        switch (String(node, "kind", pointer))
        {
            case "Ref":
                Object(node, pointer, "kind", "name");
                return new FormulaExpr.Ref(Nonblank(node, "name", pointer));
            case "Literal":
                Object(node, pointer, "kind", "value", "valueType");
                string literal = String(node, "value", pointer);
                var type = EnumValue<ColumnValueType>(node, "valueType", pointer);
                ValidateTypedValue(literal, type, pointer + "/value", RuleEngineCodes.CompileInvalidExpression);
                return new FormulaExpr.Literal(literal, type);
            case "Binary":
                Object(node, pointer, "kind", "op", "left", "right");
                return new FormulaExpr.Binary(FormulaOperator(node, pointer, comparison: false),
                    ReadExpression(Member(node, "left", pointer), pointer + "/left", depth + 1),
                    ReadExpression(Member(node, "right", pointer), pointer + "/right", depth + 1));
            case "If":
                Object(node, pointer, "kind", "when", "then", "else");
                var condition = Object(Member(node, "when", pointer), pointer + "/when", "op", "left", "right");
                return new FormulaExpr.If(new FormulaCondition(
                    ReadExpression(Member(condition, "left", pointer + "/when"), pointer + "/when/left", depth + 1),
                    FormulaOperator(condition, pointer + "/when", comparison: true),
                    ReadExpression(Member(condition, "right", pointer + "/when"), pointer + "/when/right", depth + 1)),
                    ReadExpression(Member(node, "then", pointer), pointer + "/then", depth + 1),
                    ReadExpression(Member(node, "else", pointer), pointer + "/else", depth + 1));
            default: throw Refuse(RuleDefinitionCodes.InvalidDocument, pointer + "/kind");
        }
    }

    private static string FormulaOperator(JsonElement node, string pointer, bool comparison)
    {
        string op = String(node, "op", pointer);
        bool supported = comparison ? DecisionCell.CompareOps.Contains(op)
            : op is ArithOps.Add or ArithOps.Subtract or ArithOps.Multiply or ArithOps.Divide;
        if (!supported) throw Refuse(RuleEngineCodes.CompileInvalidExpression, pointer + "/op");
        return op;
    }

    private static void ValidateTypedValue(string value, ColumnValueType type, string pointer, string code)
    {
        if ((type == ColumnValueType.Number && !double.IsFinite(JsNumberMirror.ToNumber(value)))
            || (type == ColumnValueType.Boolean && value is not ("true" or "false")))
            throw Refuse(code, pointer);
    }

    private static JsonElement Object(JsonElement node, string pointer, params string[] allowed)
    {
        RequireKind(node, JsonValueKind.Object, pointer);
        foreach (var property in node.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Refuse(RuleDefinitionCodes.UnknownMember, pointer + "/" + Escape(property.Name));
        return node;
    }

    private static JsonElement Member(JsonElement node, string name, string pointer)
    {
        RequireKind(node, JsonValueKind.Object, pointer);
        if (!node.TryGetProperty(name, out var value))
            throw Refuse(RuleDefinitionCodes.InvalidDocument, pointer + "/" + Escape(name));
        return value;
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement node, string name, string pointer)
    {
        var value = Member(node, name, pointer);
        RequireKind(value, JsonValueKind.Array, pointer + "/" + Escape(name));
        return value.EnumerateArray();
    }

    private static string StringValue(JsonElement node, string pointer)
    {
        RequireKind(node, JsonValueKind.String, pointer);
        return node.GetString()!;
    }

    private static string String(JsonElement node, string member, string pointer)
        => StringValue(Member(node, member, pointer), pointer + "/" + Escape(member));

    private static string Nonblank(JsonElement node, string member, string pointer)
    {
        string value = String(node, member, pointer);
        if (string.IsNullOrWhiteSpace(value)) throw Refuse(RuleDefinitionCodes.InvalidDocument, pointer + "/" + Escape(member));
        return value;
    }

    private static T EnumValue<T>(JsonElement node, string member, string pointer, string code = RuleDefinitionCodes.InvalidDocument)
        where T : struct, Enum
    {
        var value = Member(node, member, pointer);
        if (value.ValueKind != JsonValueKind.String || !Enum.TryParse<T>(value.GetString(), out var result)
            || !Enum.IsDefined(result) || !string.Equals(result.ToString(), value.GetString(), StringComparison.Ordinal))
            throw Refuse(code, pointer + "/" + Escape(member));
        return result;
    }

    private static void RequireKind(JsonElement node, JsonValueKind kind, string pointer)
    {
        if (node.ValueKind != kind) throw Refuse(RuleDefinitionCodes.InvalidDocument, pointer);
    }

    private static void CheckProvenance(JsonNode? node, string pointer, int depth)
    {
        if (depth >= RuleEngineLimits.Default.MaxAstNodes * 4)
            throw Refuse(RuleDefinitionCodes.InvalidDocument, pointer);
        switch (node)
        {
            case JsonObject obj:
                foreach (var member in obj)
                    CheckProvenance(member.Value, pointer + "/" + Escape(member.Key), depth + 1);
                break;
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                    CheckProvenance(array[i], pointer + "/" + i.ToString(CultureInfo.InvariantCulture), depth + 1);
                break;
            case JsonValue value:
                bool finite = value.TryGetValue<double>(out var number) ? double.IsFinite(number)
                    : !value.TryGetValue<float>(out var single) || float.IsFinite(single);
                if (!finite) throw Refuse(RuleDefinitionCodes.InvalidDocument, pointer);
                break;
        }
    }

    private static void CheckDuplicateMembers(JsonElement node, string pointer)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                string location = pointer + "/" + Escape(property.Name);
                if (!names.Add(property.Name)) throw Refuse(RuleDefinitionCodes.DuplicateMember, location);
                CheckDuplicateMembers(property.Value, location);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var value in node.EnumerateArray()) CheckDuplicateMembers(value, pointer + "/" + (i++).ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static DefinitionReadException Refuse(string code, string pointer) => new(code, pointer);
    internal sealed class DefinitionReadException(string code, string pointer) : Exception(code)
    {
        public string Code { get; } = code;
        public string Pointer { get; } = pointer;
    }
}
