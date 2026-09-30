using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Harborline.Foundation.Definitions;

namespace Harborline.Foundation.DataExchange;

/// <summary>Data exchange's additive pack wire identity; the catalogue namespace is the host's concern.</summary>
public static class DataExchangePackIdentity
{
    /// <summary>Content kind 12, <c>DataExchangeDefinition</c>: the api's <c>PackContentKind.DataExchangeDefinition</c>. 11 is <c>ScheduleDefinition</c> and was a collision (T-724 ruling 51).</summary>
    public const int ContentKind = 12;
}

/// <summary>How a rerun treats source records the target may already hold.</summary>
public enum ReplayPolicy
{
    /// <summary>Writes every source record as a new target record.</summary>
    Append,
    /// <summary>Writes source records over target records that already exist instead of adding new ones.</summary>
    Overwrite,
    /// <summary>Appends only source records not already represented by the target identity.</summary>
    [JsonStringEnumMemberName("append_dedup")]
    AppendDeduplicate,
}

/// <summary>Names the acquisition capability, connector version, secret reference, and parameters.</summary>
public sealed record ExchangeSourceBinding(
    string CapabilityId,
    string ConnectorVersion,
    string SecretReference,
    IReadOnlyDictionary<string, string> Parameters,
    string FormatId = "csv");

/// <summary>The parameter names a source capability version accepts in a definition.</summary>
public sealed record SourceParameterSchema(IReadOnlySet<string> AllowedParameters);

/// <summary>Looks up the declared parameter schema of a source capability version.</summary>
public interface ISourceParameterSchemaRegistry
{
    /// <summary>Returns the parameter schema for the capability and connector version, or null when unregistered (refused as definition.source_capability_unregistered).</summary>
    SourceParameterSchema? Resolve(string capabilityId, string connectorVersion);
}

/// <summary>Registry that declares no parameters for any source, so every supplied parameter is refused as undeclared.</summary>
public sealed class EmptySourceParameterSchemaRegistry : ISourceParameterSchemaRegistry
{
    /// <summary>Shared schema that allows no parameters.</summary>
    private static readonly SourceParameterSchema Empty = new(new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Always returns the empty schema, whatever the capability and version.</summary>
    public SourceParameterSchema Resolve(string capabilityId, string connectorVersion) => Empty;
}

/// <summary>The layer an authored definition belongs to.</summary>
public enum DataExchangeCascadeLayer
{
    /// <summary>The base layer, shipped with the pack.</summary>
    Base,
    /// <summary>The tenant's own layer, which overrides the base.</summary>
    Tenant,
}

/// <summary>A capability an installing host must provide, with an optional minimum platform version.</summary>
public sealed record DataExchangeDefinitionRequirement(
    string Capability,
    string? MinimumPlatformVersion = null);

/// <summary>Catalogue identity, version, tenant, cascade layer, provenance, required capabilities and contract version; it must match the definition's key, version and tenant (definition.envelope_mismatch).</summary>
public sealed record DataExchangeDefinitionEnvelope(
    string Identity,
    string Version,
    string Tenant,
    DataExchangeCascadeLayer CascadeLayer,
    JsonElement Provenance,
    IReadOnlyList<DataExchangeDefinitionRequirement> Requires,
    DefinitionContractVersion? Contract);

/// <summary>Which mapping metadata wins when tenant and pack metadata disagree.</summary>
public enum MappingMetadataPrecedence
{
    /// <summary>Tenant metadata overrides pack metadata.</summary>
    TenantOverPack,
}

/// <summary>Binds a reference dataset to its pack and feed distributions.</summary>
public sealed record ReferenceSetBinding(
    string DatasetId,
    string PackDistribution,
    string FeedDistribution);

/// <summary>Content kind 12. Portable pack state only: runs, checkpoints and secrets stay outside.</summary>
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
    /// <summary>Admission while a definition is being authored; the envelope and catalogue coordinates are not yet required.</summary>
    Author,
    /// <summary>Admission at publication; the envelope is required and the body must match its catalogue coordinates.</summary>
    Publish,
    /// <summary>Admission when a pack is installed; the same closed checks as publication.</summary>
    Install,
}

/// <summary>The shared catalogue's tenant, definition id and semantic version label for one body.</summary>
public sealed record DataExchangeCatalogueCoordinates(string Tenant, string Key, string Version);

/// <summary>Resolves the published head the interpreter runs; the shared catalogue supplies it.</summary>
public interface IDataExchangeDefinitionResolver
{
    /// <summary>Returns the published head definition for the tenant and key, or null when none is published.</summary>
    ValueTask<DataExchangeDefinition?> ResolvePublishedHeadAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>Canonical, projection-neutral definition JSON: snake_case, ordinal-sorted keys, one trailing newline.</summary>
public static class DataExchangeDefinitionJson
{
    /// <summary>Cached serializer options: snake_case names, null members omitted, unmapped members rejected.</summary>
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a definition to canonical UTF-8 JSON with ordinal-sorted keys and one trailing newline; throws ArgumentNullException for null.</summary>
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

    /// <summary>Parses definition JSON bytes; throws JsonException for malformed JSON, unmapped members or a null payload.</summary>
    public static DataExchangeDefinition Deserialize(ReadOnlySpan<byte> json)
        => JsonSerializer.Deserialize<DataExchangeDefinition>(json, Options)
            ?? throw new JsonException("The Data exchange definition payload is null.");

    /// <summary>Parses definition JSON text; throws ArgumentException for blank input and JsonException for malformed JSON, unmapped members or a null payload.</summary>
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

/// <summary>Validates authored Data Exchange definitions at the admission boundary.</summary>
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

    /// <paramref name="catalogue"/> names the catalogue coordinates the body must agree with before it
    /// <paramref name="window"/> is the host's application-contract window from the platform seed (T-572);
    /// <summary>Parses body JSON and returns every refusal, empty when admitted: body_invalid, settings_not_object, rollback_refused and export_refused shape errors, then definition checks and catalogue_mismatch outside the Author phase.</summary>
    public static IReadOnlyList<DataExchangeRefusal> AdmitJson(
        string bodyJson,
        DataExchangeAdmissionPhase phase,
        DefinitionContractWindow window,
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
        var refusals = Validate(definition, phase, window, sources).ToList();
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
        DefinitionContractWindow window,
        ISourceParameterSchemaRegistry? sources = null)
    {
        var refusals = Validate(definition, phase, window, sources);
        return refusals.Count == 0 ? definition : throw new DataExchangeAdmissionException(refusals);
    }

    /// <summary>Returns every refusal for the definition in the given phase, empty when admitted: version, schema version, envelope, contract window, mapping, source format, secret reference, parameters and external key columns.</summary>
    public static IReadOnlyList<DataExchangeRefusal> Validate(
        DataExchangeDefinition definition,
        DataExchangeAdmissionPhase phase,
        DefinitionContractWindow window,
        ISourceParameterSchemaRegistry? sources = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(window);
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
        // T-572 (rulings 85-88, Q6): the contract travels with the envelope, inside the host's window.
        var contract = definition.Envelope is null ? null : window.Check(definition.Envelope.Contract, null);
        if (contract is not null)
            refusals.Add(new(contract.Code, contract.Pointer));
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
    /// <summary>Always 12, the Data Exchange definition content kind.</summary>
    public int ContentKind => DataExchangePackIdentity.ContentKind;
}

/// <summary>Admits at the publish boundary and projects canonical bytes; it does not publish a version.</summary>
public static class DataExchangeDefinitionPackExporter
{
    /// <summary>Admits the definition at the publish boundary and returns its canonical bytes as a pack entry; throws DataExchangeAdmissionException listing every refusal.</summary>
    public static DataExchangeDefinitionPackageEntry Export(
        DataExchangeDefinition definition,
        DefinitionContractWindow window,
        ISourceParameterSchemaRegistry? sources = null)
    {
        DataExchangeDefinitionAdmission.Require(definition, DataExchangeAdmissionPhase.Publish, window, sources);
        return new(definition.Key, definition.Version, DataExchangeDefinitionJson.SerializeCanonical(definition));
    }
}
