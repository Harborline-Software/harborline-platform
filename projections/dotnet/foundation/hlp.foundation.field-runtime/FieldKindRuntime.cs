using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>Binds exact kind revisions to one schema projection and one complete value validator.</summary>
public sealed class FieldKindRuntime : IFieldKindRuntime
{
    private readonly FieldKindRegistry _kinds;

    /// <summary>Uses the composing runtime's admitted registry, without guessing built-in kinds.</summary>
    public FieldKindRuntime(FieldKindRegistry kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds;
    }

    /// <inheritdoc />
    public ICompiledFieldKind Bind(FieldKindReference reference, string jsonPointer)
    {
        var kind = _kinds.Resolve(reference, jsonPointer);
        if (reference.Parameters is null)
            throw new FieldAdmissionException([new("field.kind_parameter_invalid", jsonPointer + "/parameters",
                "A kind reference requires its parameter object.")]);
        var parameters = new Dictionary<string, string>(reference.Parameters, StringComparer.Ordinal);
        var limits = FieldKindLimits.FromKindParameters(parameters, jsonPointer + "/parameters");
        if ((limits.HasNumericLimits && kind.ValueShape is not (FieldScalarValueShape.Number or FieldScalarValueShape.Integer))
            || (limits.HasTextLimits && kind.ValueShape != FieldScalarValueShape.Text))
            throw new FieldAdmissionException([new("field.kind_parameter_type_mismatch", jsonPointer + "/parameters",
                "The declared limits do not apply to this kind's scalar shape.")]);
        return new CompiledKind(kind, limits, parameters);
    }

    private sealed class CompiledKind : ICompiledFieldKind
    {
        private readonly FieldKindLimits _limits;

        internal CompiledKind(AdmittedFieldKind kind, FieldKindLimits limits,
            IReadOnlyDictionary<string, string> parameters)
        {
            Kind = kind;
            _limits = limits;
            var schema = JsonNode.Parse(limits.Compile().JsonSchemaKeywords.GetRawText())!.AsObject();
            schema["type"] = kind.ValueShape switch
            {
                FieldScalarValueShape.Text => "string",
                FieldScalarValueShape.Boolean => "boolean",
                FieldScalarValueShape.Integer => "integer",
                FieldScalarValueShape.Number => "number",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            schema["x-harborline-field-kind"] = new JsonObject
            {
                ["kind_id"] = kind.KindId,
                ["version"] = kind.Version,
                ["parameters"] = JsonSerializer.SerializeToNode(parameters),
            };
            using var document = JsonDocument.Parse(schema.ToJsonString());
            JsonSchema = document.RootElement.Clone();
        }

        public AdmittedFieldKind Kind { get; }
        public JsonElement JsonSchema { get; }

        public IReadOnlyList<FieldRefusal> Validate(JsonElement value, string jsonPointer)
        {
            var matches = Kind.ValueShape switch
            {
                FieldScalarValueShape.Text => value.ValueKind == JsonValueKind.String,
                FieldScalarValueShape.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                FieldScalarValueShape.Number => value.ValueKind == JsonValueKind.Number,
                FieldScalarValueShape.Integer => value.ValueKind == JsonValueKind.Number
                    && new FieldNumber(value.GetRawText()).IsInteger,
                _ => false,
            };
            if (!matches)
                return [new("field.value_type_mismatch", jsonPointer, "The value does not match the admitted kind's scalar shape.")];
            return _limits.Validate(value, jsonPointer);
        }

        public IReadOnlyList<FieldRefusal> ValidateJson(string json, string jsonPointer)
        {
            if (json is null)
                return [new("field.value_malformed", jsonPointer, "The field value must be valid JSON.")];
            try
            {
                using var document = JsonDocument.Parse(json);
                return Validate(document.RootElement, jsonPointer);
            }
            catch (JsonException)
            {
                return [new("field.value_malformed", jsonPointer, "The field value must be valid JSON.")];
            }
        }
    }
}
