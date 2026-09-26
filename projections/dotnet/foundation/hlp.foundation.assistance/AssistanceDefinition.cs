using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Foundation.Assistance;

public static class AssistancePackIdentity
{
    // DES-0002 §5, row "Assistance"; 19 is the highest previously allocated content kind.
    public const int ContentKind = 20;
}

public enum AssistanceCascadeLayer { Base, Tenant }
public sealed record AssistanceDefinitionRequirement(string Capability, string? MinimumPlatformVersion = null);
public sealed record AssistanceDefinitionEnvelope(string Identity, string Version, string Tenant, AssistanceCascadeLayer CascadeLayer, JsonElement Provenance, string RetentionClass, bool LegalHold, IReadOnlyList<AssistanceDefinitionRequirement> Requires);
public enum AssistanceClassificationTier { Ap, Cp, Never }
public sealed record AssistanceCommandClassification(AssistanceClassificationTier Tier, bool Undoable, string? Archetype = null, string? Justification = null);
public sealed record AssistanceCommand(string CommandId, IReadOnlyList<string> Aliases, JsonElement ArgsSchema, AssistanceCommandClassification Classification);
public enum AssistanceContextRedaction { Value, Presence, Bucket }
public sealed record AssistanceContextAllowlistEntry(string Definition, string Field, AssistanceContextRedaction Redaction);
public sealed record AssistanceProvider(string ProviderId, string ModelId);

/// <summary>The pilot's portable Assistance definition. It deliberately contains no prompt or instruction text.</summary>
public sealed record AssistanceDefinition(
    string Tenant, string Key, string Version, string Surface, string Route, IReadOnlyList<AssistanceCommand> Commands,
    IReadOnlyList<AssistanceContextAllowlistEntry> ContextAllowlist, IReadOnlyList<string> Recipients,
    AssistanceProvider Provider, int SchemaVersion = 1, AssistanceDefinitionEnvelope? Envelope = null);

public sealed record CommandCatalogueEntry(string CommandId);
public interface ICommandCatalogueRegistry { CommandCatalogueEntry? Resolve(string commandId); }
/// <summary>The default registry declares no commands; hosts must provide their command catalogue explicitly.</summary>
public sealed class EmptyCommandCatalogueRegistry : ICommandCatalogueRegistry { public CommandCatalogueEntry? Resolve(string commandId) => null; }
public enum AssistanceAdmissionPhase { Author, Publish, Install }
public sealed record AssistanceCatalogueCoordinates(string Tenant, string Key, string Version);
public sealed record AssistanceRefusal(string Code, string Pointer);
public sealed class AssistanceAdmissionException(IReadOnlyList<AssistanceRefusal> refusals) : Exception("The Assistance definition was refused.") { public IReadOnlyList<AssistanceRefusal> Refusals { get; } = refusals; }

/// <summary>Canonical, projection-neutral Assistance JSON: snake_case, ordinal-sorted keys, one trailing newline.</summary>
public static class AssistanceDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public static byte[] SerializeCanonical(AssistanceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var source = JsonSerializer.SerializeToNode(definition, Options) ?? throw new JsonException("The Assistance definition serialized to no JSON value.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonicalize(source).WriteTo(writer, Options);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }
    public static AssistanceDefinition Deserialize(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<AssistanceDefinition>(json, Options) ?? throw new JsonException("The Assistance definition payload is null.");
    public static AssistanceDefinition Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<AssistanceDefinition>(json, Options) ?? throw new JsonException("The Assistance definition payload is null.");
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = false };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value.OrderBy(property => property.Key, StringComparer.Ordinal).Select(property => KeyValuePair.Create(property.Key, property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value.Select(item => item is null ? null : Canonicalize(item)).ToArray()),
        _ => node.DeepClone(),
    };
}

/// <summary>Phase-aware intent admission for Assistance definitions.</summary>
public static class AssistanceDefinitionAdmission
{
    private static readonly HashSet<string> NeverArchetypes = new(StringComparer.Ordinal) { "irreversible-bulk", "security-access-control", "engine-parked", "human-authority-gate", "agent-self-repoint" };
    public static IReadOnlyList<AssistanceRefusal> AdmitJson(string bodyJson, AssistanceAdmissionPhase phase, ICommandCatalogueRegistry? commands = null, AssistanceCatalogueCoordinates? catalogue = null, AssistanceDefinition? previous = null)
    {
        try { using var document = JsonDocument.Parse(bodyJson); if (document.RootElement.ValueKind != JsonValueKind.Object) return [new("definition.settings_not_object", "/")]; }
        catch (JsonException) { return [new("definition.body_invalid", "/")]; }
        catch (ArgumentNullException) { return [new("definition.body_invalid", "/")]; }
        AssistanceDefinition definition;
        try { definition = AssistanceDefinitionJson.Deserialize(bodyJson); }
        catch (JsonException) { return [new("definition.body_invalid", "/")]; }
        var refusals = Validate(definition, phase, commands, previous).ToList();
        if (catalogue is not null && phase != AssistanceAdmissionPhase.Author)
        {
            if (definition.Tenant != catalogue.Tenant) refusals.Add(new("definition.catalogue_mismatch", "/tenant"));
            if (definition.Key != catalogue.Key) refusals.Add(new("definition.catalogue_mismatch", "/key"));
            if (definition.Version != catalogue.Version) refusals.Add(new("definition.catalogue_mismatch", "/version"));
        }
        return refusals;
    }
    public static AssistanceDefinition Require(AssistanceDefinition definition, AssistanceAdmissionPhase phase, ICommandCatalogueRegistry? commands = null, AssistanceDefinition? previous = null)
    {
        var refusals = Validate(definition, phase, commands, previous);
        return refusals.Count == 0 ? definition : throw new AssistanceAdmissionException(refusals);
    }
    public static IReadOnlyList<AssistanceRefusal> Validate(AssistanceDefinition definition, AssistanceAdmissionPhase phase, ICommandCatalogueRegistry? commands = null, AssistanceDefinition? previous = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        commands ??= new EmptyCommandCatalogueRegistry();
        var refusals = new List<AssistanceRefusal>();
        if (!IsThreePartVersion(definition.Version)) refusals.Add(new("definition.version_invalid", "/version"));
        if (definition.SchemaVersion != 1) refusals.Add(new("definition.schema_version_unsupported", "/schema_version"));
        if (string.IsNullOrWhiteSpace(definition.Surface)) refusals.Add(new("definition.surface_required", "/surface"));
        if (string.IsNullOrWhiteSpace(definition.Route)) refusals.Add(new("definition.route_required", "/route"));
        if (definition.Envelope is null) { if (phase != AssistanceAdmissionPhase.Author) refusals.Add(new("definition.envelope_required", "/envelope")); }
        else if (definition.Envelope.Identity != definition.Key || definition.Envelope.Version != definition.Version || definition.Envelope.Tenant != definition.Tenant) refusals.Add(new("definition.envelope_mismatch", "/envelope"));
        if (string.IsNullOrWhiteSpace(definition.Provider.ProviderId)) refusals.Add(new("definition.provider_required", "/provider/provider_id"));
        if (string.IsNullOrWhiteSpace(definition.Provider.ModelId)) refusals.Add(new("definition.model_required", "/provider/model_id"));
        // Registered-provider floors are deferred by DES-0026 open ruling 5.
        for (var index = 0; index < definition.Commands.Count; index++) ValidateCommand(definition.Commands[index], index, commands, refusals);
        for (var index = 0; index < definition.ContextAllowlist.Count; index++)
        {
            var entry = definition.ContextAllowlist[index];
            if (string.IsNullOrWhiteSpace(entry.Definition)) refusals.Add(new("definition.context_definition_required", $"/context_allowlist/{index}/definition"));
            if (string.IsNullOrWhiteSpace(entry.Field)) refusals.Add(new("definition.context_field_required", $"/context_allowlist/{index}/field"));
        }
        for (var index = 0; index < definition.Recipients.Count; index++) if (string.IsNullOrWhiteSpace(definition.Recipients[index])) refusals.Add(new("definition.recipient_required", $"/recipients/{index}"));
        if (previous is not null) ValidateNarrowing(definition, previous, refusals);
        return refusals;
    }
    private static void ValidateCommand(AssistanceCommand command, int index, ICommandCatalogueRegistry commands, List<AssistanceRefusal> refusals)
    {
        var pointer = $"/commands/{index}";
        if (string.IsNullOrWhiteSpace(command.CommandId) || commands.Resolve(command.CommandId) is null) refusals.Add(new("definition.command_unregistered", pointer + "/command_id"));
        if (command.ArgsSchema.ValueKind != JsonValueKind.Object) refusals.Add(new("definition.args_schema_not_object", pointer + "/args_schema"));
        if (command.Classification.Tier != AssistanceClassificationTier.Never) return;
        if (string.IsNullOrWhiteSpace(command.Classification.Archetype)) refusals.Add(new("definition.never_archetype_required", pointer + "/classification/archetype"));
        else if (!NeverArchetypes.Contains(command.Classification.Archetype)) refusals.Add(new("definition.never_archetype_unknown", pointer + "/classification/archetype"));
        if (string.IsNullOrWhiteSpace(command.Classification.Justification)) refusals.Add(new("definition.never_justification_required", pointer + "/classification/justification"));
    }
    private static void ValidateNarrowing(AssistanceDefinition definition, AssistanceDefinition previous, List<AssistanceRefusal> refusals)
    {
        var oldCommands = previous.Commands.ToDictionary(command => command.CommandId, StringComparer.Ordinal);
        foreach (var (command, index) in definition.Commands.Select((command, index) => (command, index)))
        {
            if (!oldCommands.TryGetValue(command.CommandId, out var old)) continue;
            var pointer = $"/commands/{index}";
            if (command.Classification.Tier < old.Classification.Tier) refusals.Add(new("definition.tier_lowered", pointer + "/classification/tier"));
            // This deliberately narrow proxy is not a JSON-Schema subset checker.
            if (command.ArgsSchema.GetRawText() != old.ArgsSchema.GetRawText() && RequiredEntries(old.ArgsSchema).Except(RequiredEntries(command.ArgsSchema), StringComparer.Ordinal).Any()) refusals.Add(new("definition.args_schema_widened", pointer + "/args_schema"));
        }
        if (definition.Recipients.Except(previous.Recipients, StringComparer.Ordinal).Any()) refusals.Add(new("definition.recipients_widened", "/recipients"));
    }
    private static IReadOnlySet<string> RequiredEntries(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array) return new HashSet<string>(StringComparer.Ordinal);
        return required.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
    }
    private static bool IsThreePartVersion(string? version)
    {
        var parts = version?.Split('.', StringSplitOptions.None);
        return parts is { Length: 3 } && parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }
}

/// <summary>Adapter for binding Assistance under the shared catalogue's reserved <see cref="DefinitionKind.Pilot"/> namespace.</summary>
public static class AssistanceDefinitionStoreAdmission
{
    public static IReadOnlyList<DefinitionRefusal> Admit(DefinitionDocument document, DefinitionAdmissionPhase phase) => Admit(document, phase, new EmptyCommandCatalogueRegistry());
    public static IReadOnlyList<DefinitionRefusal> Admit(DefinitionDocument document, DefinitionAdmissionPhase phase, ICommandCatalogueRegistry commands, AssistanceDefinition? previous = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var assistancePhase = phase switch { DefinitionAdmissionPhase.Author => AssistanceAdmissionPhase.Author, DefinitionAdmissionPhase.Publish => AssistanceAdmissionPhase.Publish, _ => AssistanceAdmissionPhase.Install };
        return AssistanceDefinitionAdmission.AdmitJson(document.BodyJson, assistancePhase, commands, new(document.Key.Tenant, document.Key.DefinitionId, document.Version), previous).Select(refusal => new DefinitionRefusal(refusal.Code, refusal.Pointer)).ToArray();
    }
}

public sealed record AssistanceDefinitionPackageEntry(string DefinitionId, string Version, ReadOnlyMemory<byte> Content) { public int ContentKind => AssistancePackIdentity.ContentKind; }
/// <summary>Admits at Publish and projects canonical bytes; publication itself belongs to the shared catalogue.</summary>
public static class AssistanceDefinitionPackExporter
{
    public static AssistanceDefinitionPackageEntry Export(AssistanceDefinition definition, ICommandCatalogueRegistry? commands = null)
    {
        AssistanceDefinitionAdmission.Require(definition, AssistanceAdmissionPhase.Publish, commands);
        return new(definition.Key, definition.Version, AssistanceDefinitionJson.SerializeCanonical(definition));
    }
}
