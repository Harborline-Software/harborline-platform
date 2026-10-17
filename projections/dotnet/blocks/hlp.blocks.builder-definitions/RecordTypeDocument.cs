using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The provider-neutral envelope of one Records definition, a Record Type or a Class (DES-0015 records-ck-1).
/// The definition's identity is its body's id (<c>record_type_id</c>, <c>class_id</c>), which the minting caller
/// derives once from this section and the definition's name; the envelope carries no second identity. The version is
/// the catalogue's, not the body's: restore-as-draft copies a published body under a new version unchanged,
/// so a version inside the body would contradict the draft it was copied into.
/// </summary>
/// <param name="Tenant">The owning tenant identifier.</param>
/// <param name="Section">The catalogue section that scopes the Record Type id (L102), such as <c>finance</c> or <c>eam</c>.</param>
/// <param name="Contract">The authored definition contract version.</param>
/// <param name="PackageId">The owning package (records-ck-2 <c>package_id</c>; ADR-0006: control lives in the envelope). Required at publication.</param>
/// <param name="Requires">The packages this definition depends on, each as <c>pack-key@interfaceVersion</c> (ADR-0006; the api installer's spelling).</param>
/// <param name="Exposes">Present when other packages may reference this definition: the target-side declaration (ADR-0028; L127).</param>
public sealed record RecordsDefinitionEnvelope(
    string Tenant,
    string Section,
    DefinitionContractVersion? Contract,
    string? PackageId = null,
    IReadOnlyList<RecordsRequirement>? Requires = null,
    RecordsExposure? Exposes = null);

/// <summary>One declared package dependency, spelled <c>pack-key@interfaceVersion</c> (ADR-0006).</summary>
/// <param name="Capability">The requirement, for example <c>eam-core@1</c>.</param>
public sealed record RecordsRequirement(string Capability)
{
    /// <summary>Parses the spelling; <c>@</c> is a structural separator, and the interface version is a positive integer.</summary>
    public static bool TryParse(string? capability, out string packageId, out int interfaceVersion)
    {
        packageId = "";
        interfaceVersion = 0;
        var separator = capability?.LastIndexOf('@') ?? -1;
        if (separator <= 0 || separator == capability!.Length - 1
            || !int.TryParse(capability.AsSpan(separator + 1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out interfaceVersion)
            || interfaceVersion <= 0) return false;
        packageId = capability[..separator];
        return !string.IsNullOrWhiteSpace(packageId);
    }
}

/// <summary>The target-side declaration of a definition other packages may reference, at an interface version (ADR-0028).</summary>
public sealed record RecordsExposure(int InterfaceVersion);

/// <summary>
/// One authored Record Type as the shared catalogue stores it: its envelope, its author-facing name and the
/// typed Records grammar. The grammar's members sit at the document root, so the Records validator's RFC 6901
/// pointers (<c>/record_type_id</c>, <c>/fields/0/field_key</c>) address the stored body directly.
/// </summary>
/// <param name="Envelope">The provider-neutral envelope.</param>
/// <param name="Name">The author-facing name; duplicates across sections are legal and never an identity.</param>
/// <param name="RecordTypeId">The section-scoped identity, immutable across every version of the definition.</param>
/// <param name="Fields">The fields owned by this Record Type.</param>
/// <param name="Traits">The exact Trait revisions and their slot-to-field bindings.</param>
/// <param name="RetentionClockFieldId">The <c>field_key</c> of the field whose date starts the retention clock.</param>
/// <param name="ClassId">The type's one Class, its catalogue home (records-ck-18). Required at publication.</param>
/// <param name="RecordClass">Whether the type's records are reference, master or transactional data (records-ck-17).
/// Required at publication with no default: it decides whether the records may travel in a pack (ADR 0025).</param>
/// <param name="CreationGate">Who may create the type's records (records-ck-22, <c>creation_gate</c>). Absent means
/// <see cref="RecordCreationGate.FormOnly"/>, the default DES-0004 section 1 defines; the canonical body omits an explicit
/// <c>form_only</c>, so one meaning has one encoding and one digest.</param>
/// <param name="CreationRoleId">For <see cref="RecordCreationGate.Role"/> only, the one role whose holders create the type's
/// records (decision D1, <c>creation_role_id</c>): a domain role name, as Records names roles elsewhere. Required at
/// publication for a role gate and refused for any other gate.</param>
public sealed record RecordTypeDocument(
    RecordsDefinitionEnvelope Envelope,
    string Name,
    string RecordTypeId,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<TraitReference>? Traits = null,
    string? RetentionClockFieldId = null,
    string? ClassId = null,
    RecordClass? RecordClass = null,
    RecordCreationGate? CreationGate = null,
    string? CreationRoleId = null)
{
    /// <summary>
    /// The declared amendment justification constraint, or null for a legacy body without a declaration. It does not
    /// select an ADR-0040 amendment mode; an explicit value is retained in canonical JSON even when it is the create default.
    /// </summary>
    public RecordAmendmentPolicy? AmendmentPolicy { get; init; }

    /// <summary>The declared history policy, or null for a legacy body without a declaration.</summary>
    public RecordHistoryPolicy? HistoryPolicy { get; init; }

    /// <summary>The typed Records grammar the validator and schema compiler read.</summary>
    public RecordTypeDefinition ToDefinition() => new(RecordTypeId, Fields, Traits, RetentionClockFieldId);
}

/// <summary>
/// An amendment constraint (ADR-0097): a reason is required when a record is amended. This is a justification constraint
/// across ADR-0040's edit-in-place, supersession and immutable-from-a-state modes, not another mode or a choice among them.
/// The object form leaves room for a later, separately agreed mode member without changing this wire value's shape.
/// This declaration alone implements no runtime amendment operation.
/// </summary>
/// <param name="ReasonRequired">True: an amendment requires a reason. False is not an admitted policy in this contract.</param>
public sealed record RecordAmendmentPolicy(bool ReasonRequired);

/// <summary>A declared history policy (ADR-0040, ADR-0097). Absence is a legacy body, not an implicit default.</summary>
public enum RecordHistoryPolicy
{
    /// <summary>
    /// <c>enabled</c> requests history as effective-dated rows under ADR-0040. The declaration does not implement the
    /// runtime row history, choose effective timestamps, or reinterpret a row's recorded time.
    /// </summary>
    Enabled,
}

/// <summary>
/// A Record Type's creation gate (DES-0004 section 1, DES-0015 records-ck-22, L1004): a sealed literal set whose exact
/// spellings are <c>form_only</c>, <c>role</c> and <c>engine</c>. Any other spelling, case or a number is malformed.
/// </summary>
public enum RecordCreationGate
{
    /// <summary><c>form_only</c>, the default: records are created only by submitting a form whose submit gate is the create
    /// gate (ADR 0043). There is no direct create.</summary>
    FormOnly,
    /// <summary><c>role</c>: holders of the type's one <c>creation_role_id</c> create records, directly or through a form that
    /// invokes the same role-gated operation without adding another creation gate (decision D1).</summary>
    Role,
    /// <summary><c>engine</c>: only the engine creates the type's records, from an effect or an import. No principal and no
    /// form creates them.</summary>
    Engine,
}

/// <summary>
/// The one governing creation policy of a Record Type: its effective gate and, for a role gate, its creation role. Every
/// Records consumer (the Records write, the host's decider adapter and the host's Forms policy reader) reads it from the
/// published body through here, so the answer is in one place.
/// </summary>
/// <param name="Gate">The effective gate: the declared one, or <see cref="RecordCreationGate.FormOnly"/> when absent.</param>
/// <param name="RoleId">The declared <c>creation_role_id</c>, or null.</param>
public sealed record RecordCreationPolicy(RecordCreationGate Gate, string? RoleId)
{
    /// <summary>The policy <paramref name="document"/> declares, with the DES-0004 default for an absent gate.</summary>
    public static RecordCreationPolicy Of(RecordTypeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(document.CreationGate ?? RecordCreationGate.FormOnly, document.CreationRoleId);
    }
}

/// <summary>The data category of a Record Type's records (DES-0015 records-ck-17; ADR 0025; L088, L191-L194).</summary>
public enum RecordClass
{
    /// <summary>Shared standard data. The only class whose records travel in a pack, and they install tenant-read-only.</summary>
    Reference,
    /// <summary>The tenant's own lasting entities, independent of its transactions (ISO 8000-2). Never travels.</summary>
    Master,
    /// <summary>Activity the tenant accrues over time. Never travels.</summary>
    Transactional,
}

/// <summary>Canonical, projection-neutral Record Type definition JSON.</summary>
public static class RecordTypeDefinitionJson
{
    /// <summary>
    /// Serializes a Record Type definition to canonical UTF-8 JSON with a trailing newline. An explicit
    /// <c>creation_gate</c> of <c>form_only</c> is the default and is omitted, so a body that spells it and one that does
    /// not have the same bytes and digest.
    /// </summary>
    public static byte[] SerializeCanonical(RecordTypeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return RecordsJson.SerializeCanonical(document.CreationGate == RecordCreationGate.FormOnly
            ? document with { CreationGate = null }
            : document);
    }

    /// <summary>
    /// Deserializes a Record Type definition. An unknown member, a null payload or an explicit null for a non-nullable
    /// member is a <see cref="JsonException"/>; an absent required member is reported by <see cref="FirstMissing"/>.
    /// </summary>
    public static RecordTypeDocument Deserialize(ReadOnlySpan<byte> json) => RecordsJson.Deserialize<RecordTypeDocument>(json);

    /// <summary>
    /// The RFC 6901 pointer of the first required member the JSON left absent, or of a null list element; null when
    /// the document is whole. The serializer leaves an absent constructor member null rather than refusing it, because
    /// optional members are omitted from the canonical form; this walk names the required ones, so admission refuses
    /// a partial document instead of dereferencing it.
    /// </summary>
    public static string? FirstMissing(RecordTypeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Envelope is null) return null;
        foreach (var (requirement, index) in (document.Envelope.Requires ?? []).Select((requirement, index) => (requirement, index)))
            if (requirement is null) return $"/envelope/requires/{index}";
        if (document.Fields is null) return "/fields";
        foreach (var (field, index) in document.Fields.Select((field, index) => (field, index)))
        {
            var pointer = $"/fields/{index}";
            if (field is null) return pointer;
            if (field.FieldKey is null) return pointer + "/field_key";
            if (field.DisplayName is null) return pointer + "/display_name";
            if (field.DefaultsProvenance is { KindId: null }) return pointer + "/defaults_provenance/kind_id";
            if (field.DefaultsProvenance is { KindVersion: null }) return pointer + "/defaults_provenance/kind_version";
            if (field.Reference?.Pin is { } pin)
            {
                if (pin.PackageId is null) return pointer + "/reference/pin/package_id";
                if (pin.DefinitionId is null) return pointer + "/reference/pin/definition_id";
                if (pin.Version is null) return pointer + "/reference/pin/version";
                if (pin.Digest is null) return pointer + "/reference/pin/digest";
            }
            if (field.Binding is not { } binding) continue;
            if (binding.Kind is null) return pointer + "/binding/kind";
            if (binding.Kind.KindId is null) return pointer + "/binding/kind/kind_id";
            if (binding.Kind.Version is null) return pointer + "/binding/kind/version";
            if (binding.Kind.Parameters is null) return pointer + "/binding/kind/parameters";
            if (binding.Constraints is null) return pointer + "/binding/constraints";
            if (binding.Constraints.ReadRoleIds is null) return pointer + "/binding/constraints/read_role_ids";
        }
        foreach (var (trait, index) in (document.Traits ?? []).Select((trait, index) => (trait, index)))
        {
            var pointer = $"/traits/{index}";
            if (trait is null) return pointer;
            if (trait.TraitId is null) return pointer + "/trait_id";
            if (trait.Version is null) return pointer + "/version";
            foreach (var (binding, slot) in (trait.SlotBindings ?? []).Select((binding, slot) => (binding, slot)))
            {
                if (binding is null) return $"{pointer}/slot_bindings/{slot}";
                if (binding.SlotKey is null) return $"{pointer}/slot_bindings/{slot}/slot_key";
                if (binding.FieldKey is null) return $"{pointer}/slot_bindings/{slot}/field_key";
            }
        }
        return null;
    }
}

/// <summary>The canonical JSON form shared by the Records definition families.</summary>
internal static class RecordsJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] SerializeCanonical<T>(T document) where T : class
    {
        ArgumentNullException.ThrowIfNull(document);
        var source = JsonSerializer.SerializeToNode(document, Options)
            ?? throw new JsonException("The Records definition serialized to no JSON value.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonicalize(source).WriteTo(writer, Options);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> json) where T : class
        => JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new JsonException("The Records definition payload is null.");

    private static JsonSerializerOptions CreateOptions()
    {
        // No dictionary key policy: field-kind parameter names are the kind's own wire names and must
        // round-trip unchanged.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // An explicit null for a non-nullable member is malformed. Absent members are named by FirstMissing,
            // since the canonical form omits optional ones and required-parameter enforcement would refuse those too.
            RespectNullableAnnotations = true,
        };
        options.Converters.Add(new RecordClassJsonConverter());
        options.Converters.Add(new RecordCreationGateJsonConverter());
        options.Converters.Add(new RecordAmendmentPolicyJsonConverter());
        options.Converters.Add(new RecordHistoryPolicyJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }

    // Dedicated converters keep these declarations exact; the general enum converter accepts case variants.
    private sealed class RecordAmendmentPolicyJsonConverter : JsonConverter<RecordAmendmentPolicy>
    {
        public override RecordAmendmentPolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject
                || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != "reason_required"
                || !reader.Read() || reader.TokenType != JsonTokenType.True
                || !reader.Read() || reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException("An amendment policy must declare exactly reason_required: true.");
            return new(true);
        }

        public override void Write(Utf8JsonWriter writer, RecordAmendmentPolicy value, JsonSerializerOptions options)
        {
            if (!value.ReasonRequired)
                throw new JsonException("Unknown amendment policy.");
            writer.WriteStartObject();
            writer.WriteBoolean("reason_required", true);
            writer.WriteEndObject();
        }
    }

    private sealed class RecordHistoryPolicyJsonConverter : JsonConverter<RecordHistoryPolicy>
    {
        public override RecordHistoryPolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String && reader.GetString() == "enabled"
                ? RecordHistoryPolicy.Enabled
                : throw new JsonException("Unknown history policy.");

        public override void Write(Utf8JsonWriter writer, RecordHistoryPolicy value, JsonSerializerOptions options)
            => writer.WriteStringValue(value == RecordHistoryPolicy.Enabled
                ? "enabled"
                : throw new JsonException("Unknown history policy."));
    }

    // The creation gate is a sealed literal set with exact lowercase spellings (records-ck-22); the general enum converter
    // would accept case variants, and the platform pack's legacy "form-only" spelling is read only by its own
    // version-specific reader (PlatformCreationGateCompatibility), never here.
    private sealed class RecordCreationGateJsonConverter : JsonConverter<RecordCreationGate>
    {
        public override RecordCreationGate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("A creation gate must be a lowercase named value.");
            return reader.GetString() switch
            {
                "form_only" => RecordCreationGate.FormOnly,
                "role" => RecordCreationGate.Role,
                "engine" => RecordCreationGate.Engine,
                _ => throw new JsonException("Unknown creation gate."),
            };
        }

        public override void Write(Utf8JsonWriter writer, RecordCreationGate value, JsonSerializerOptions options)
            => writer.WriteStringValue(value switch
            {
                RecordCreationGate.FormOnly => "form_only",
                RecordCreationGate.Role => "role",
                RecordCreationGate.Engine => "engine",
                _ => throw new JsonException("Unknown creation gate."),
            });
    }

    // Record class has an exact lowercase grammar; the general enum converter accepts case variants.
    private sealed class RecordClassJsonConverter : JsonConverter<RecordClass>
    {
        public override RecordClass Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("A record class must be a lowercase named value.");
            return reader.GetString() switch
            {
                "reference" => RecordClass.Reference,
                "master" => RecordClass.Master,
                "transactional" => RecordClass.Transactional,
                _ => throw new JsonException("Unknown record class."),
            };
        }

        public override void Write(Utf8JsonWriter writer, RecordClass value, JsonSerializerOptions options)
            => writer.WriteStringValue(value switch
            {
                RecordClass.Reference => "reference",
                RecordClass.Master => "master",
                RecordClass.Transactional => "transactional",
                _ => throw new JsonException("Unknown record class."),
            });
    }

    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(
                property.Key,
                property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value
            .Select(item => item is null ? null : Canonicalize(item))
            .ToArray()),
        _ => node.DeepClone(),
    };
}
