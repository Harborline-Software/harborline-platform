using System.Text.Json;
using Harborline.Contracts.Fields;

namespace Harborline.Kernel.SchemaValidation.Records;

/// <summary>The authored identity and fields of one Record Type definition.</summary>
public sealed record RecordTypeDefinition(string RecordTypeId, IReadOnlyList<FieldDefinition> Fields);

/// <summary>An authored Record Type field whose stable identity is its containing type and key.</summary>
public sealed record FieldDefinition(string FieldKey, string DisplayName);

/// <summary>Validates the Records identity contract before a definition can mutate the schema registry.</summary>
public sealed class RecordsIntentValidator
{
    /// <summary>Returns every identity refusal for a candidate and its optional preceding version.</summary>
    public IReadOnlyList<FieldRefusal> Validate(
        RecordTypeDefinition candidate,
        RecordTypeDefinition? previousVersion)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var refusals = new List<FieldRefusal>();
        if (string.IsNullOrWhiteSpace(candidate.RecordTypeId))
        {
            refusals.Add(new(
                "records.identity.record_type_id_required",
                "/record_type_id",
                "record_type_id is required."));
        }

        if (previousVersion is not null && !string.Equals(
                candidate.RecordTypeId,
                previousVersion.RecordTypeId,
                StringComparison.Ordinal))
        {
            refusals.Add(new(
                "records.identity.record_type_id_immutable",
                "/record_type_id",
                "record_type_id cannot change within one definition's version history."));
        }

        var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (field, index) in (candidate.Fields ?? []).Select((field, index) => (field, index)))
        {
            if (string.IsNullOrWhiteSpace(field.FieldKey))
            {
                refusals.Add(new(
                    "records.identity.field_key_required",
                    $"/fields/{index}/field_key",
                    "field_key is required."));
                continue;
            }

            if (!fieldKeys.Add(field.FieldKey))
            {
                refusals.Add(new(
                    "records.identity.duplicate_field_key",
                    $"/fields/{index}/field_key",
                    "field_key must be unique within its record type."));
            }
        }

        return refusals;
    }
}

/// <summary>The result of compiling one Record Type grammar to a registered schema.</summary>
public sealed record RecordTypeSchemaCompilation(Schema? Schema, IReadOnlyList<FieldRefusal> Refusals);

/// <summary>Compiles admitted Record Type grammar to a draft 2020-12 JSON Schema document.</summary>
public sealed class RecordTypeSchemaCompiler
{
    private readonly RecordsIntentValidator _intentValidator;

    /// <summary>Creates a compiler using the supplied identity validator.</summary>
    public RecordTypeSchemaCompiler(RecordsIntentValidator? intentValidator = null)
    {
        _intentValidator = intentValidator ?? new RecordsIntentValidator();
    }

    /// <summary>
    /// Validates a candidate before registering its derived schema. Refused candidates never mutate the registry.
    /// </summary>
    public async ValueTask<RecordTypeSchemaCompilation> CompileAndRegisterAsync(
        RecordTypeDefinition candidate,
        RecordTypeDefinition? previousVersion,
        ISchemaRegistry schemaRegistry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(schemaRegistry);

        var refusals = _intentValidator.Validate(candidate, previousVersion);
        if (refusals.Count != 0)
        {
            return new(null, refusals);
        }

        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var field in candidate.Fields ?? [])
        {
            properties.Add(field.FieldKey, new Dictionary<string, string>
            {
                ["type"] = "string",
            });
        }

        var document = new Dictionary<string, object>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        var schema = await schemaRegistry.RegisterAsync(
            JsonSerializer.Serialize(document),
            cancellationToken: cancellationToken);
        return new(schema, []);
    }
}
