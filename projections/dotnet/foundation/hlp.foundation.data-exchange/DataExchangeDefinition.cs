using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Harborline.Foundation.DataExchange;

/// <summary>Data exchange's additive pack wire identity; the catalogue namespace is the host's concern.</summary>
public static class DataExchangePackIdentity
{
    /// <summary>Content kind 11, <c>DataExchangeDefinition</c> (DES-0002 §5).</summary>
    public const int ContentKind = 11;
}

public enum ReplayPolicy
{
    Append,
    Overwrite,
    [JsonStringEnumMemberName("append_dedup")]
    AppendDeduplicate,
}

/// <summary>
/// The acquisition source named by reference and parameterised. <see cref="CapabilityId"/> is the
/// host-registered exchange kind; <see cref="SecretReference"/> names a tenant-held secret and never
/// carries its value.
/// </summary>
public sealed record ExchangeSourceBinding(
    string CapabilityId,
    string ConnectorVersion,
    string SecretReference,
    IReadOnlyDictionary<string, string> Parameters,
    string FormatId = "csv");

public sealed record SourceParameterSchema(IReadOnlySet<string> AllowedParameters);

public interface ISourceParameterSchemaRegistry
{
    SourceParameterSchema? Resolve(string capabilityId, string connectorVersion);
}

public sealed class EmptySourceParameterSchemaRegistry : ISourceParameterSchemaRegistry
{
    private static readonly SourceParameterSchema Empty = new(new HashSet<string>(StringComparer.Ordinal));

    public SourceParameterSchema Resolve(string capabilityId, string connectorVersion) => Empty;
}

public enum DataExchangeCascadeLayer
{
    Base,
    Tenant,
}

public sealed record DataExchangeDefinitionRequirement(
    string Capability,
    string? MinimumPlatformVersion = null);

public sealed record DataExchangeDefinitionEnvelope(
    string Identity,
    string Version,
    string Tenant,
    DataExchangeCascadeLayer CascadeLayer,
    JsonElement Provenance,
    IReadOnlyList<DataExchangeDefinitionRequirement> Requires);

public enum MappingMetadataPrecedence
{
    TenantOverPack,
}

public sealed record ReferenceSetBinding(
    string DatasetId,
    string PackDistribution,
    string FeedDistribution);

/// <summary>Content kind 11. Portable pack state only: runs, checkpoints and secrets stay outside.</summary>
public sealed record DataExchangeDefinition(
    string Tenant,
    string Key,
    string Version,
    string Title,
    ExchangeSourceBinding Source,
    TabularMappingDocument Mapping,
    ReplayPolicy ReplayPolicy,
    IReadOnlyList<string> ExternalKeyColumns,
    string? RefreshScheduleReference,
    int SchemaVersion = 1,
    DataExchangeDefinitionEnvelope? Envelope = null,
    MappingMetadataPrecedence MetadataPrecedence = MappingMetadataPrecedence.TenantOverPack,
    ReferenceSetBinding? ReferenceSet = null)
{
    /// <summary>The host-registered exchange kind token: the bound source capability.</summary>
    [JsonIgnore]
    public string ExchangeKind => Source.CapabilityId;
}

/// <summary>The boundary at which a definition is admitted; every boundary runs the same closed checks.</summary>
public enum DataExchangeAdmissionPhase
{
    Author,
    Publish,
    Install,
}

/// <summary>The shared catalogue's tenant, definition id and semantic version label for one body.</summary>
public sealed record DataExchangeCatalogueCoordinates(string Tenant, string Key, string Version);

/// <summary>Resolves the published head the interpreter runs; the shared catalogue supplies it.</summary>
public interface IDataExchangeDefinitionResolver
{
    ValueTask<DataExchangeDefinition?> ResolvePublishedHeadAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>Canonical, projection-neutral definition JSON: snake_case, ordinal-sorted keys, one trailing newline.</summary>
public static class DataExchangeDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] SerializeCanonical(DataExchangeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var source = JsonSerializer.SerializeToNode(definition, Options)
            ?? throw new JsonException("The Data exchange definition serialized to no JSON value.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonicalize(source).WriteTo(writer, Options);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    public static DataExchangeDefinition Deserialize(ReadOnlySpan<byte> json)
        => JsonSerializer.Deserialize<DataExchangeDefinition>(json, Options)
            ?? throw new JsonException("The Data exchange definition payload is null.");

    public static DataExchangeDefinition Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<DataExchangeDefinition>(json, Options)
            ?? throw new JsonException("The Data exchange definition payload is null.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        // Dictionary keys (source parameters, hl: extension terms) stay verbatim so they round-trip.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(
                property.Key,
                property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value.Select(item => item is null ? null : Canonicalize(item)).ToArray()),
        _ => node.DeepClone(),
    };
}

/// <summary>
/// The intent validator. Pure and phase-aware; the shared builder-definitions catalogue binds it as
/// the <c>DataExchange</c> registry's admission, and pack installation admits the same JSON body.
/// </summary>
public static class DataExchangeDefinitionAdmission
{
    private static readonly HashSet<string> RollbackMembers = new(StringComparer.Ordinal)
    {
        "rollback", "all_or_nothing_rollback", "atomic_batch", "multi_command_atomic_commit", "undo",
    };

    private static readonly HashSet<string> ExportMembers = new(StringComparer.Ordinal)
    {
        "export", "direction", "outbound", "report_shape", "row_set",
    };

    /// <summary>
    /// Admits a canonical JSON body at a boundary; a refusal list is never a partial admission.
    /// <paramref name="catalogue"/> names the catalogue coordinates the body must agree with before it
    /// publishes or installs; a restored draft may disagree until the author re-versions it.
    /// </summary>
    public static IReadOnlyList<DataExchangeRefusal> AdmitJson(
        string bodyJson,
        DataExchangeAdmissionPhase phase,
        ISourceParameterSchemaRegistry? sources = null,
        DataExchangeCatalogueCoordinates? catalogue = null)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(bodyJson); }
        catch (JsonException) { return [new("definition.body_invalid", "/")]; }
        catch (ArgumentNullException) { return [new("definition.body_invalid", "/")]; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return [new("definition.settings_not_object", "/")];
            var shape = new List<DataExchangeRefusal>();
            foreach (var property in root.EnumerateObject())
            {
                if (RollbackMembers.Contains(property.Name)) shape.Add(new("definition.rollback_refused", "/" + property.Name));
                else if (ExportMembers.Contains(property.Name)) shape.Add(new("definition.export_refused", "/" + property.Name));
            }
            if (!root.TryGetProperty("mapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object)
                shape.Add(new("definition.settings_not_object", "/mapping"));
            if (shape.Count > 0) return shape;
        }
        DataExchangeDefinition definition;
        try { definition = DataExchangeDefinitionJson.Deserialize(bodyJson); }
        catch (JsonException) { return [new("definition.body_invalid", "/")]; }
        var refusals = Validate(definition, phase, sources).ToList();
        if (catalogue is not null && phase != DataExchangeAdmissionPhase.Author)
        {
            if (definition.Tenant != catalogue.Tenant) refusals.Add(new("definition.catalogue_mismatch", "/tenant"));
            if (definition.Key != catalogue.Key) refusals.Add(new("definition.catalogue_mismatch", "/key"));
            if (definition.Version != catalogue.Version) refusals.Add(new("definition.catalogue_mismatch", "/version"));
        }
        return refusals;
    }

    /// <summary>Throws the complete refusal list instead of returning it.</summary>
    public static DataExchangeDefinition Require(
        DataExchangeDefinition definition,
        DataExchangeAdmissionPhase phase,
        ISourceParameterSchemaRegistry? sources = null)
    {
        var refusals = Validate(definition, phase, sources);
        return refusals.Count == 0 ? definition : throw new DataExchangeAdmissionException(refusals);
    }

    public static IReadOnlyList<DataExchangeRefusal> Validate(
        DataExchangeDefinition definition,
        DataExchangeAdmissionPhase phase,
        ISourceParameterSchemaRegistry? sources = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        sources ??= new EmptySourceParameterSchemaRegistry();
        var refusals = new List<DataExchangeRefusal>();
        if (!TabularMappingAdmission.IsThreePartVersion(definition.Version))
            refusals.Add(new("definition.version_invalid", "/version"));
        if (definition.SchemaVersion != 1)
            refusals.Add(new("definition.schema_version_unsupported", "/schema_version"));
        if (definition.Envelope is null)
        {
            if (phase != DataExchangeAdmissionPhase.Author)
                refusals.Add(new("definition.envelope_required", "/envelope"));
        }
        else if (definition.Envelope.Identity != definition.Key
            || definition.Envelope.Version != definition.Version
            || definition.Envelope.Tenant != definition.Tenant)
        {
            refusals.Add(new("definition.envelope_mismatch", "/envelope"));
        }
        refusals.AddRange(TabularMappingAdmission.Refusals(definition.Mapping)
            .Select(refusal => refusal with { Pointer = "/mapping" + refusal.Pointer }));
        if (string.IsNullOrWhiteSpace(definition.Source.FormatId))
            refusals.Add(new("definition.format_required", "/source/format_id"));
        if (!IsSecretReference(definition.Source.SecretReference))
            refusals.Add(new("definition.secret_reference_invalid", "/source/secret_reference"));
        var parameterSchema = sources.Resolve(definition.Source.CapabilityId, definition.Source.ConnectorVersion);
        if (parameterSchema is null)
            refusals.Add(new("definition.source_capability_unregistered", "/source/capability_id"));
        foreach (var parameter in definition.Source.Parameters.Keys)
        {
            var pointer = $"/source/parameters/{Escape(parameter)}";
            if (parameterSchema is not null && !parameterSchema.AllowedParameters.Contains(parameter))
                refusals.Add(new("definition.source_parameter_undeclared", pointer));
            var normalized = new string(parameter.Where(char.IsLetterOrDigit).ToArray());
            if (normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("accesstoken", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("clientsecret", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("token", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("secret", StringComparison.OrdinalIgnoreCase))
                refusals.Add(new("definition.credential_forbidden", pointer));
            if (normalized.Contains("cursor", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
                refusals.Add(new("definition.cursor_forbidden", pointer));
            if (normalized.Contains("retention", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("retainuntil", StringComparison.OrdinalIgnoreCase))
                refusals.Add(new("definition.retention_forbidden", pointer));
        }
        if (definition.ReplayPolicy == ReplayPolicy.AppendDeduplicate && definition.ExternalKeyColumns.Count == 0)
            refusals.Add(new("definition.external_key_required", "/external_key_columns"));
        var mappedColumns = definition.Mapping.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var externalKey in definition.ExternalKeyColumns.Where(column => !mappedColumns.Contains(column)))
            refusals.Add(new("definition.external_key_unknown", $"/external_key_columns/{Escape(externalKey)}"));
        return refusals;
    }

    private static bool IsSecretReference(string value)
    {
        const string secretScheme = "secret://";
        const string referenceScheme = "secretref:";
        var remainder = value.StartsWith(secretScheme, StringComparison.Ordinal)
            ? value[secretScheme.Length..]
            : value.StartsWith(referenceScheme, StringComparison.Ordinal)
                ? value[referenceScheme.Length..]
                : string.Empty;
        return remainder.Length > 0
            && remainder.All(character => char.IsLetterOrDigit(character)
                || character is '.' or '_' or ':' or '/' or '-');
    }

    private static string Escape(string value) => value
        .Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);
}

/// <summary>A provider-neutral pack entry; publication lifecycle belongs to the shared catalogue.</summary>
public sealed record DataExchangeDefinitionPackageEntry(
    string DefinitionId,
    string Version,
    ReadOnlyMemory<byte> Content)
{
    public int ContentKind => DataExchangePackIdentity.ContentKind;
}

/// <summary>Admits at the publish boundary and projects canonical bytes; it does not publish a version.</summary>
public static class DataExchangeDefinitionPackExporter
{
    public static DataExchangeDefinitionPackageEntry Export(
        DataExchangeDefinition definition,
        ISourceParameterSchemaRegistry? sources = null)
    {
        DataExchangeDefinitionAdmission.Require(definition, DataExchangeAdmissionPhase.Publish, sources);
        return new(definition.Key, definition.Version, DataExchangeDefinitionJson.SerializeCanonical(definition));
    }
}
