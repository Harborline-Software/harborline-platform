using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Harborline.Kernel.SchemaValidation;

/// <summary>A bounded, single-process reference implementation of <see cref="ISchemaRegistry"/>.</summary>
public sealed class InMemorySchemaRegistry : ISchemaRegistry
{
    private readonly SchemaRegistryOptions _options;
    private readonly BuildOptions _buildOptions;
    private readonly ConcurrentDictionary<SchemaId, Entry> _schemas = new();

    /// <summary>Creates a registry with default or caller-supplied resource bounds.</summary>
    public InMemorySchemaRegistry(SchemaRegistryOptions? options = null)
    {
        _options = options ?? SchemaRegistryOptions.Default;
        _options.Validate();
        _buildOptions = TimedSchemaDialect.Build(_options.PatternMatchTimeout);
    }

    /// <inheritdoc />
    public ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_schemas.TryGetValue(id, out var entry) ? entry.Schema : null);
    }

    /// <inheritdoc />
    public ValueTask<Schema> RegisterAsync(
        string jsonSchemaText,
        IReadOnlyList<SchemaId>? parents = null,
        IReadOnlyList<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonSchemaText);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] canonicalBytes;
        try
        {
            using var document = JsonDocument.Parse(
                jsonSchemaText,
                new JsonDocumentOptions { MaxDepth = _options.MaxNestingDepth });
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("$schema", out var dialect)
                && (dialect.ValueKind != JsonValueKind.String
                    || dialect.GetString() != "https://json-schema.org/draft/2020-12/schema"))
            {
                throw new InvalidSchemaException(
                    "Only JSON Schema draft 2020-12 is accepted by this registry.");
            }
            canonicalBytes = CanonicalContent.Serialize(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidSchemaException(
                $"JSON Schema is malformed or exceeds the {_options.MaxNestingDepth}-level nesting limit.",
                exception);
        }

        if (canonicalBytes.Length > _options.MaxSchemaBytes)
        {
            throw new InvalidSchemaException(
                $"JSON Schema is {canonicalBytes.Length} canonical bytes, exceeding the {_options.MaxSchemaBytes}-byte limit.");
        }

        JsonSchema parsedSchema;
        try
        {
            parsedSchema = JsonSchema.FromText(jsonSchemaText, _buildOptions, null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidSchemaException("JSON Schema is not a valid draft 2020-12 schema.", exception);
        }

        var contentAddress = CanonicalContent.ContentAddress(canonicalBytes);
        var id = new SchemaId($"schema:{contentAddress}");
        var schema = new Schema(
            id,
            jsonSchemaText,
            parents?.ToArray() ?? Array.Empty<SchemaId>(),
            tags?.ToArray() ?? Array.Empty<string>(),
            contentAddress);
        var entry = _schemas.GetOrAdd(id, _ => new Entry(schema, parsedSchema));
        return ValueTask.FromResult(entry.Schema);
    }

    /// <inheritdoc />
    public ValueTask<SchemaValidationResult> ValidateAsync(
        SchemaId id,
        ReadOnlyMemory<byte> documentBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_schemas.TryGetValue(id, out var entry))
        {
            throw new SchemaNotFoundException($"Schema '{id}' is not registered.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(documentBytes);
        }
        catch (JsonException exception)
        {
            return ValueTask.FromResult(Invalid(
                new SchemaValidationError(string.Empty, exception.Message, "invalid-json")));
        }

        using (document)
        {
            EvaluationResults results;
            try
            {
                results = entry.ParsedSchema.Evaluate(
                    document.RootElement,
                    new EvaluationOptions { OutputFormat = OutputFormat.List });
            }
            catch (RegexMatchTimeoutException)
            {
                return ValueTask.FromResult(Invalid(new SchemaValidationError(
                    string.Empty,
                    "Validation aborted because a schema pattern exceeded its match-time budget.",
                    "pattern-timeout")));
            }

            if (results.IsValid)
            {
                return ValueTask.FromResult(new SchemaValidationResult(
                    true,
                    Array.Empty<SchemaValidationError>()));
            }

            JsonNode? schemaNode = null;
            try { schemaNode = JsonNode.Parse(entry.Schema.JsonSchemaText); }
            catch (JsonException) { }
            return ValueTask.FromResult(Invalid(CollectErrors(results, schemaNode)));
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Schema> ListAsync(
        string? tagFilter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in _schemas.Values.OrderBy(value => value.Schema.Id.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tagFilter is null || entry.Schema.Tags.Contains(tagFilter, StringComparer.Ordinal))
            {
                yield return entry.Schema;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static SchemaValidationResult Invalid(params SchemaValidationError[] errors)
        => new(false, errors);

    private static SchemaValidationResult Invalid(IReadOnlyList<SchemaValidationError> errors)
        => new(false, errors);

    private static IReadOnlyList<SchemaValidationError> CollectErrors(
        EvaluationResults results,
        JsonNode? schemaNode)
    {
        var errors = new List<SchemaValidationError>();
        Walk(results, schemaNode, errors);
        if (errors.Count == 0)
        {
            errors.Add(new SchemaValidationError(
                results.InstanceLocation.ToString(),
                "Validation failed without keyword-level detail.",
                "invalid"));
        }

        return errors;
    }

    private static void Walk(
        EvaluationResults node,
        JsonNode? schemaNode,
        List<SchemaValidationError> errors)
    {
        if (node.Errors is { Count: > 0 } keywordErrors)
        {
            var pointer = node.InstanceLocation.ToString();
            var evaluationPath = node.EvaluationPath.ToString();
            foreach (var (keyword, detail) in keywordErrors)
            {
                EmitKeywordError(keyword, detail, pointer, evaluationPath, schemaNode, errors);
            }
        }

        if (node.Details is { Count: > 0 } children)
        {
            foreach (var child in children) Walk(child, schemaNode, errors);
        }
    }

    private static void EmitKeywordError(
        string keyword,
        string detail,
        string instancePointer,
        string evaluationPath,
        JsonNode? schemaNode,
        List<SchemaValidationError> errors)
    {
        var code = string.IsNullOrEmpty(keyword) ? "additional-properties" : keyword;
        var message = string.IsNullOrEmpty(keyword) ? detail : $"{keyword}: {detail}";
        var keywordValue = ResolveKeywordValue(schemaNode, evaluationPath, keyword);

        if (code == "required" && keywordValue is JsonArray required)
        {
            var missing = ExtractMissingRequiredNames(detail);
            foreach (var item in required)
            {
                var name = item?.GetValue<string>();
                if (string.IsNullOrEmpty(name) || (missing.Count > 0 && !missing.Contains(name))) continue;
                var pointer = $"{instancePointer}/{EncodePointerSegment(name)}";
                errors.Add(new SchemaValidationError(
                    pointer,
                    $"required: '{name}' is required.",
                    "required",
                    new Dictionary<string, string> { ["field"] = name }));
            }

            return;
        }

        errors.Add(new SchemaValidationError(
            instancePointer,
            message,
            code,
            BuildParams(code, keywordValue)));
    }

    private static IReadOnlyDictionary<string, string>? BuildParams(string code, JsonNode? keywordValue)
    {
        if (keywordValue is null) return null;
        var value = keywordValue.ToJsonString().Trim('"');
        return code switch
        {
            "minimum" or "exclusiveMinimum" or "minLength" or "minItems" or "minProperties"
                => new Dictionary<string, string> { ["min"] = value },
            "maximum" or "exclusiveMaximum" or "maxLength" or "maxItems" or "maxProperties"
                => new Dictionary<string, string> { ["max"] = value },
            "multipleOf" => new Dictionary<string, string> { ["multiple"] = value },
            "enum" or "const" => new Dictionary<string, string> { ["allowed"] = keywordValue.ToJsonString() },
            "pattern" => new Dictionary<string, string> { ["pattern"] = value },
            "format" => new Dictionary<string, string> { ["format"] = value },
            _ => null,
        };
    }

    private static JsonNode? ResolveKeywordValue(JsonNode? schemaNode, string evaluationPath, string keyword)
    {
        if (schemaNode is null || string.IsNullOrEmpty(keyword)) return null;
        return ResolvePointer(schemaNode, evaluationPath) is JsonObject schemaObject
            && schemaObject.TryGetPropertyValue(keyword, out var value)
                ? value
                : null;
    }

    private static JsonNode? ResolvePointer(JsonNode root, string pointer)
    {
        if (string.IsNullOrEmpty(pointer)) return root;
        JsonNode? current = root;
        foreach (var rawSegment in pointer.Split('/').Skip(1))
        {
            var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (current is JsonObject jsonObject && jsonObject.TryGetPropertyValue(segment, out var property))
            {
                current = property;
            }
            else if (current is JsonArray array
                && int.TryParse(segment, out var index)
                && index >= 0
                && index < array.Count)
            {
                current = array[index];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static HashSet<string> ExtractMissingRequiredNames(string detail)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var start = detail.IndexOf('[', StringComparison.Ordinal);
        var end = detail.IndexOf(']', StringComparison.Ordinal);
        if (start < 0 || end < start) return names;
        try
        {
            if (JsonNode.Parse(detail[start..(end + 1)]) is JsonArray array)
            {
                foreach (var item in array)
                {
                    var name = item?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
            }
        }
        catch (JsonException) { }

        return names;
    }

    private static string EncodePointerSegment(string value)
        => value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private sealed record Entry(Schema Schema, JsonSchema ParsedSchema);
}
