using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Harborline.Kernel.SchemaValidation;

/// <summary>The stable package content kind for Records definitions.</summary>
public enum RecordsContentKind
{
    /// <summary>The shipped content-kind-zero identity.</summary>
    AssetTypeDefinition = 0,
}

/// <summary>The three exclusive transport classes for record instances.</summary>
public enum RecordClassKind
{
    /// <summary>Package-carried, tenant-read-only reference data.</summary>
    Reference,
    /// <summary>Tenant-owned master data.</summary>
    Master,
    /// <summary>Tenant-accrued transactional data.</summary>
    Transactional,
}

/// <summary>Who may originate a record.</summary>
public enum RecordCreationGate
{
    /// <summary>Creation is admitted only through a Form.</summary>
    FormOnly,
    /// <summary>Creation is admitted for a declared role.</summary>
    Role,
    /// <summary>Creation is admitted only for an engine.</summary>
    Engine,
}

/// <summary>What an offline client may do for a record type.</summary>
public enum OfflineRecordCaptureMode
{
    /// <summary>The type is read-only while offline.</summary>
    ReadOnly,
    /// <summary>Capture locally and require later confirmation.</summary>
    CaptureThenConfirm,
    /// <summary>Capture and commit under the admitted offline contract.</summary>
    CaptureAndCommit,
}

/// <summary>The declared conflict strategy for one field.</summary>
public enum FieldConflictPolicy
{
    /// <summary>Raise a decision for a person.</summary>
    Ask,
    /// <summary>Combine values through the registered additive strategy.</summary>
    Additive,
    /// <summary>Keep the latest observed value, not a last-write-wins timestamp.</summary>
    LatestObservation,
}

/// <summary>The multiplicity of a reference field.</summary>
public enum ReferenceCardinality
{
    /// <summary>At most one target.</summary>
    One,
    /// <summary>Multiple targets.</summary>
    Many,
}

/// <summary>What happens when a referenced record is removed from its live set.</summary>
public enum ReferenceDeleteBehavior
{
    /// <summary>Refuse while a reference exists.</summary>
    Block,
    /// <summary>Leave the referencing value orphaned.</summary>
    Orphan,
    /// <summary>Apply the declared cascade.</summary>
    Cascade,
}

/// <summary>Provider-neutral metadata carried by every Records definition.</summary>
/// <param name="DefinitionId">The stable definition identity.</param>
/// <param name="Version">The immutable semantic version.</param>
/// <param name="TenantId">The tenant that owns the definition.</param>
/// <param name="PackageId">The package that supplied the definition.</param>
/// <param name="Provenance">The definition's source provenance.</param>
/// <param name="CascadeLayer">The cascade layer that contributed the definition.</param>
/// <param name="RetentionClass">The definition-level retention class.</param>
/// <param name="LegalHold">Whether legal hold protects the definition.</param>
/// <param name="Requires">Package dependencies required by this definition.</param>
public sealed record RecordDefinitionEnvelope(
    string DefinitionId,
    string Version,
    string TenantId,
    string PackageId,
    string Provenance,
    string CascadeLayer = "tenant",
    string RetentionClass = "definition",
    bool LegalHold = false,
    IReadOnlyList<string>? Requires = null);

/// <summary>One authored class in the Records catalogue.</summary>
/// <param name="ClassId">The stable class identity.</param>
/// <param name="Name">The author-facing class name.</param>
public sealed record RecordClassDefinition(string ClassId, string Name);

/// <summary>A versioned field-runtime kind named by an author.</summary>
/// <param name="KindId">The registered kind identity.</param>
/// <param name="Version">The admitted immutable kind version.</param>
/// <param name="Parameters">Author-supplied parameters understood by that kind.</param>
public sealed record FieldKindReference(
    string KindId,
    string Version,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>A borrowed Rules expression and its grammar version.</summary>
/// <param name="Grammar">The admitted grammar identity and version.</param>
/// <param name="Expression">The authored expression text.</param>
public sealed record RuleExpression(string Grammar, string Expression);

/// <summary>A Taxonomy scheme used as the one permitted-value source.</summary>
/// <param name="SchemeId">The stable scheme identity.</param>
/// <param name="Version">The immutable scheme version.</param>
public sealed record TaxonomySchemeReference(string SchemeId, string Version);

/// <summary>A relationship and predicate used as the one permitted-value source.</summary>
/// <param name="RecordTypeId">The queried record type.</param>
/// <param name="Predicate">The admitted fixed Records query predicate.</param>
public sealed record RecordQueryValueSource(string RecordTypeId, string Predicate);

/// <summary>Exactly one of the three Records permitted-value sources.</summary>
/// <param name="LiteralValues">A small inline tenant-owned literal set.</param>
/// <param name="TaxonomyScheme">A referenced Taxonomy scheme.</param>
/// <param name="RecordQuery">A relationship to a record type plus predicate.</param>
public sealed record ValueDomainDefinition(
    IReadOnlyList<string>? LiteralValues = null,
    TaxonomySchemeReference? TaxonomyScheme = null,
    RecordQueryValueSource? RecordQuery = null);

/// <summary>A read-only coordinate into an immutable catalogue definition revision.</summary>
/// <param name="DefinitionId">The source definition.</param>
/// <param name="Revision">The immutable source revision.</param>
/// <param name="FieldKey">The source field key.</param>
/// <param name="ExpectedHash">The expected content hash.</param>
/// <param name="Provenance">The source provenance.</param>
public sealed record CatalogueFieldSource(
    string DefinitionId,
    string Revision,
    string FieldKey,
    string ExpectedHash,
    string Provenance);

/// <summary>A declared reference edge.</summary>
/// <param name="TargetRecordTypeId">The one target type, when type-targeted.</param>
/// <param name="TargetClassId">The one target class, when class-targeted.</param>
/// <param name="RequiredTraitId">The trait every target must carry, when trait-targeted.</param>
/// <param name="Cardinality">The edge cardinality.</param>
/// <param name="DeleteBehavior">The declared referential behavior.</param>
/// <param name="IsHierarchyEdge">Whether this ordinary reference is the hierarchy parent.</param>
/// <param name="TargetPackageId">The package that declares the target when the edge crosses a package boundary.</param>
public sealed record FieldReferenceDefinition(
    string? TargetRecordTypeId,
    string? TargetClassId,
    string? RequiredTraitId,
    ReferenceCardinality Cardinality,
    ReferenceDeleteBehavior DeleteBehavior,
    bool IsHierarchyEdge = false,
    string? TargetPackageId = null);

/// <summary>The governance facts declared on one field.</summary>
/// <param name="PersonalData">Whether the field contains personal data.</param>
/// <param name="Confidential">Whether the field is confidential.</param>
/// <param name="Masked">Whether the field is masked.</param>
/// <param name="Classification">The classification identity, when one is declared.</param>
public sealed record FieldGovernanceDefinition(
    bool PersonalData,
    bool Confidential,
    bool Masked,
    string? Classification);

/// <summary>The kind/version that supplied materialized creation defaults.</summary>
/// <param name="KindId">The field-kind identity.</param>
/// <param name="KindVersion">The field-kind version.</param>
public sealed record FieldKindDefaultProvenance(string KindId, string KindVersion);

/// <summary>The JSON scalar shape produced by an admitted field-kind revision.</summary>
public enum FieldScalarValueShape
{
    /// <summary>A JSON string.</summary>
    Text,
    /// <summary>A JSON boolean.</summary>
    Boolean,
    /// <summary>A JSON integer.</summary>
    Integer,
    /// <summary>A JSON number, including non-integral values.</summary>
    Number,
}

/// <summary>An admitted field kind and the editable defaults it supplies at field creation.</summary>
/// <param name="KindId">The registered field-kind identity.</param>
/// <param name="Version">The admitted immutable field-kind version.</param>
/// <param name="GovernanceDefaults">The governance values copied into a newly created field.</param>
/// <param name="ValueShape">The JSON scalar shape produced by this exact kind revision.</param>
public sealed record AdmittedFieldKind(
    string KindId,
    string Version,
    FieldGovernanceDefinition? GovernanceDefaults,
    FieldScalarValueShape ValueShape = FieldScalarValueShape.Text);

/// <summary>Materializes admitted field-kind defaults without reapplying them over author edits.</summary>
public sealed class RecordsFieldKindDefaultMaterializer
{
    private readonly FieldKindRegistry _kinds;

    /// <summary>Creates a materializer over the admitted immutable kind revisions.</summary>
    public RecordsFieldKindDefaultMaterializer(IEnumerable<AdmittedFieldKind> kinds)
        : this(new FieldKindRegistry(kinds))
    {
    }

    /// <summary>Uses the same admitted kinds as definition compilation.</summary>
    public RecordsFieldKindDefaultMaterializer(FieldKindRegistry kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds;
    }

    /// <summary>
    /// Copies defaults into fields that have never received them. Existing values and provenance
    /// are preserved, including after an author changes a field's kind.
    /// </summary>
    public RecordTypeDefinition Materialize(RecordTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var refusals = _kinds.Validate(definition);
        if (refusals.Count > 0)
        {
            throw new RecordsDefinitionAdmissionException(refusals);
        }

        return definition with
        {
            Fields = definition.Fields.Select(Materialize).ToArray(),
        };
    }

    private RecordFieldDefinition Materialize(RecordFieldDefinition field)
    {
        var kind = _kinds.Resolve(field.Kind);
        if (field.KindDefaultProvenance is not null
            || field.Governance is not null
            || kind.GovernanceDefaults is null)
        {
            return field;
        }

        return field with
        {
            Governance = kind.GovernanceDefaults with { },
            KindDefaultProvenance = new FieldKindDefaultProvenance(kind.KindId, kind.Version),
        };
    }
}

/// <summary>Constraints shared by fields, trait slots and refinements.</summary>
/// <param name="Required">Whether a value is always required at this floor.</param>
/// <param name="MinimumCount">The admitted minimum multiplicity.</param>
/// <param name="MaximumCount">The admitted maximum multiplicity, or no finite maximum.</param>
/// <param name="ReadRoleIds">The roles admitted to read the value.</param>
/// <param name="ValueDomain">The admitted value domain, when constrained.</param>
public sealed record FieldConstraintDefinition(
    bool Required,
    int MinimumCount,
    int? MaximumCount,
    IReadOnlyList<string> ReadRoleIds,
    ValueDomainDefinition? ValueDomain);

/// <summary>One Records-owned field declaration.</summary>
public sealed record RecordFieldDefinition
{
    /// <summary>Gets the author-facing field name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the stable field key, scoped by record type.</summary>
    public required string Key { get; init; }
    /// <summary>Gets the versioned field-runtime kind reference.</summary>
    public required FieldKindReference Kind { get; init; }
    /// <summary>Gets the one permitted-value source, when constrained.</summary>
    public ValueDomainDefinition? ValueDomain { get; init; }
    /// <summary>Gets the shape-only pattern.</summary>
    public string? Pattern { get; init; }
    /// <summary>Gets the condition that makes this field required.</summary>
    public RuleExpression? RequiredCondition { get; init; }
    /// <summary>Gets the creation-time default expression.</summary>
    public RuleExpression? DefaultExpression { get; init; }
    /// <summary>Gets the declared reference edge.</summary>
    public FieldReferenceDefinition? Reference { get; init; }
    /// <summary>Gets the field governance facts.</summary>
    public FieldGovernanceDefinition? Governance { get; init; }
    /// <summary>Gets the role that may write when read and write diverge.</summary>
    public string? WriteRoleId { get; init; }
    /// <summary>Gets the offline conflict policy.</summary>
    public FieldConflictPolicy ConflictPolicy { get; init; } = FieldConflictPolicy.Ask;
    /// <summary>Gets whether this field participates in record identity.</summary>
    public bool IsIdentity { get; init; }
    /// <summary>Gets whether values use locale-to-string-map storage.</summary>
    public bool IsTranslatable { get; init; }
    /// <summary>Gets a read-only immutable catalogue source coordinate.</summary>
    public CatalogueFieldSource? CatalogueSource { get; init; }
    /// <summary>Gets creation-default provenance materialized with this field.</summary>
    public FieldKindDefaultProvenance? KindDefaultProvenance { get; init; }
    /// <summary>Gets the field key this declaration narrows, when it is a refinement.</summary>
    public string? RefinesFieldKey { get; init; }
    /// <summary>Gets the admitted field constraints.</summary>
    public FieldConstraintDefinition? Constraints { get; init; }
}

/// <summary>One required slot in a versioned Trait.</summary>
/// <param name="SlotKey">The stable key scoped by trait identity.</param>
/// <param name="Required">Whether every member type must bind the slot.</param>
/// <param name="BlocksRelease">Whether an unfilled slot blocks publication.</param>
/// <param name="Constraints">The slot's admitted constraints.</param>
public sealed record TraitSlotDefinition(
    string SlotKey,
    bool Required,
    bool BlocksRelease,
    FieldConstraintDefinition Constraints);

/// <summary>A versioned Trait and its field slots.</summary>
/// <param name="TraitId">The stable trait identity.</param>
/// <param name="Version">The immutable trait version.</param>
/// <param name="Name">The author-facing name.</param>
/// <param name="Slots">The slots every member type must consider.</param>
public sealed record TraitDefinition(
    string TraitId,
    string Version,
    string Name,
    IReadOnlyList<TraitSlotDefinition> Slots);

/// <summary>An explicit binding from a qualified trait slot to a Records field.</summary>
/// <param name="TraitId">The trait identity.</param>
/// <param name="TraitVersion">The resolved immutable trait version.</param>
/// <param name="SlotKey">The slot key scoped by the trait.</param>
/// <param name="FieldKey">The field key scoped by the record type.</param>
public sealed record TraitSlotBinding(
    string TraitId,
    string TraitVersion,
    string SlotKey,
    string FieldKey);

/// <summary>A type-level identity constraint over one or more fields.</summary>
/// <param name="ConstraintId">The stable constraint identity.</param>
/// <param name="FieldKeys">The participating fields.</param>
public sealed record UniqueConstraintDefinition(string ConstraintId, IReadOnlyList<string> FieldKeys);

/// <summary>The four policies authored where the data lives.</summary>
/// <param name="Amendment">The amendment policy identity.</param>
/// <param name="History">The history policy identity.</param>
/// <param name="Visibility">The visibility policy identity.</param>
/// <param name="Retention">The type-owned retention policy identity.</param>
public sealed record RecordTypePolicies(string Amendment, string History, string Visibility, string Retention);

/// <summary>The read, write and transition permission floor for a record type.</summary>
/// <param name="ReadRoleIds">Roles admitted to read.</param>
/// <param name="WriteRoleIds">Roles admitted to write.</param>
/// <param name="TransitionRoleIds">Roles admitted to transition.</param>
public sealed record PermissionFloorDefinition(
    IReadOnlyList<string> ReadRoleIds,
    IReadOnlyList<string> WriteRoleIds,
    IReadOnlyList<string> TransitionRoleIds);

/// <summary>A domain-declared standing computed per record.</summary>
/// <param name="Name">The standing name.</param>
/// <param name="Rule">The rule over this type's fields.</param>
public sealed record StandingDefinition(string Name, RuleExpression Rule);

/// <summary>A measure declared once as a derived Records element.</summary>
/// <param name="Name">The measure name.</param>
/// <param name="ImplementationId">The registered implementation identity.</param>
public sealed record MeasureDefinition(string Name, string ImplementationId);

/// <summary>The authored grammar for one Records record type.</summary>
public sealed record RecordTypeDefinition
{
    /// <summary>Gets content kind zero, the shipped Records definition identity.</summary>
    public RecordsContentKind ContentKind { get; init; } = RecordsContentKind.AssetTypeDefinition;
    /// <summary>Gets the provider-neutral definition envelope.</summary>
    public required RecordDefinitionEnvelope Envelope { get; init; }

    /// <summary>Gets the stable, section-scoped record type identity.</summary>
    public required string RecordTypeId { get; init; }

    /// <summary>Gets the author-facing name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the stable key inside the record type's section.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the record type's one exclusive catalogue class.</summary>
    public required string ClassId { get; init; }

    /// <summary>Gets the record instance transport class.</summary>
    public RecordClassKind RecordClass { get; init; } = RecordClassKind.Master;
    /// <summary>Gets authored catalogue classes available to the type.</summary>
    public IReadOnlyList<RecordClassDefinition> Classes { get; init; } = [];
    /// <summary>Gets the Records-owned field declarations.</summary>
    public IReadOnlyList<RecordFieldDefinition> Fields { get; init; } = [];
    /// <summary>Gets the versioned Traits declared with this definition.</summary>
    public IReadOnlyList<TraitDefinition> Traits { get; init; } = [];
    /// <summary>Gets explicit qualified-slot-to-field bindings.</summary>
    public IReadOnlyList<TraitSlotBinding> TraitBindings { get; init; } = [];
    /// <summary>Gets type-level uniqueness constraints.</summary>
    public IReadOnlyList<UniqueConstraintDefinition> UniqueConstraints { get; init; } = [];
    /// <summary>Gets the type-level policies.</summary>
    public RecordTypePolicies? Policies { get; init; }
    /// <summary>Gets the field whose date starts the retention clock.</summary>
    public string? RetentionClockFieldKey { get; init; }
    /// <summary>Gets the record creation gate.</summary>
    public RecordCreationGate CreationGate { get; init; } = RecordCreationGate.FormOnly;
    /// <summary>Gets the offline capture mode.</summary>
    public OfflineRecordCaptureMode OfflineCaptureMode { get; init; } = OfflineRecordCaptureMode.ReadOnly;
    /// <summary>Gets category tags independent of the package label.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
    /// <summary>Gets the Workflow that is this type's lifecycle state machine.</summary>
    public string? LifecycleWorkflowId { get; init; }
    /// <summary>Gets the permission floor every write channel narrows from.</summary>
    public PermissionFloorDefinition? PermissionFloor { get; init; }
    /// <summary>Gets domain-declared standings.</summary>
    public IReadOnlyList<StandingDefinition> Standings { get; init; } = [];
    /// <summary>Gets measures declared by this record type.</summary>
    public IReadOnlyList<MeasureDefinition> Measures { get; init; } = [];
    /// <summary>Gets the registered merge policy, when merging is admitted.</summary>
    public string? MergePolicyId { get; init; }
    /// <summary>Gets whether effective dating is enabled for this type.</summary>
    public bool EffectiveDating { get; init; }
    /// <summary>Gets whether this is a sealed system record type.</summary>
    public bool IsSystemSealed { get; init; }
}

/// <summary>Canonical JSON for the typed Records definition contract.</summary>
public static class RecordsDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly JsonSerializerOptions AdmissionOptions = new(Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal static JsonElement AdmissionShape(RecordTypeDefinition definition)
        => JsonSerializer.SerializeToElement(definition, AdmissionOptions);

    /// <summary>Serializes a definition with deterministic object-property ordering.</summary>
    public static string SerializeCanonical(RecordTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(definition, Options);
        using var document = JsonDocument.Parse(bytes);
        return System.Text.Encoding.UTF8.GetString(CanonicalContent.Serialize(document.RootElement));
    }

    /// <summary>Deserializes a typed Records definition and refuses unknown wire members.</summary>
    public static RecordTypeDefinition Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<RecordTypeDefinition>(json, Options)
            ?? throw new JsonException("Records definition cannot be null.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

/// <summary>Raised when a Records definition is refused before schema-registry mutation.</summary>
public sealed class RecordsDefinitionAdmissionException : Exception
{
    /// <summary>Creates an admission exception carrying every structural refusal.</summary>
    public RecordsDefinitionAdmissionException(IReadOnlyList<RecordsRefusal> refusals)
        : base("The Records definition was refused.")
    {
        Refusals = refusals;
    }

    /// <summary>Gets the stable, field-addressable refusals.</summary>
    public IReadOnlyList<RecordsRefusal> Refusals { get; }
}

/// <summary>Compiles admitted Records grammar and registers its JSON Schema 2020-12 contract.</summary>
public sealed class RecordsDefinitionCompiler
{
    private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";
    private readonly ISchemaRegistry _registry;
    private readonly FieldKindRegistry _fieldKinds;

    /// <summary>Creates a compiler backed by the platform schema registry.</summary>
    public RecordsDefinitionCompiler(ISchemaRegistry registry, FieldKindRegistry? fieldKinds = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _fieldKinds = fieldKinds ?? new FieldKindRegistry([]);
    }

    /// <summary>Validates, compiles and idempotently registers one typed Records definition.</summary>
    public async ValueTask<Schema> CompileAndRegisterAsync(
        RecordTypeDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var schemaText = CompileSchema(definition, cancellationToken);

        return await _registry.RegisterAsync(
            schemaText,
            tags:
            [
                "records-definition",
                $"records-definition:{definition.Envelope.DefinitionId}",
                $"records-definition-version:{definition.Envelope.Version}",
            ],
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates and compiles one typed Records definition without registering it.</summary>
    public string CompileSchema(
        RecordTypeDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        var admission = new RecordsIntentValidator().Validate(definition);
        if (!admission.IsAdmitted)
        {
            throw new RecordsDefinitionAdmissionException(admission.Refusals);
        }
        var kindRefusals = _fieldKinds.Validate(definition);
        if (kindRefusals.Count > 0)
            throw new RecordsDefinitionAdmissionException(kindRefusals);

        return Compile(definition);
    }

    private string Compile(RecordTypeDefinition definition)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var field in definition.Fields)
        {
            if (!RecordsConstraintIntersection.TryResolve(
                    RecordsConstraintIntersection.ForField(definition, field), out var effective))
                throw new RecordsDefinitionAdmissionException(
                    [new("records.field.constraint_intersection_empty", "/fields", "Field constraints have no admitted intersection.")]);
            var kind = _fieldKinds.Resolve(field.Kind);
            properties[field.Key] = Compile(
                field with { Constraints = effective, ValueDomain = effective.ValueDomain },
                kind.ValueShape);
            if (field.IsIdentity || effective.Required || effective.MinimumCount > 0)
            {
                required.Add(field.Key);
            }
        }

        var schema = new JsonObject
        {
            ["$schema"] = Draft202012,
            ["$comment"] = $"Harborline Records {definition.Envelope.DefinitionId}@{definition.Envelope.Version} ({definition.RecordTypeId})",
            ["type"] = "object",
            ["title"] = definition.Name,
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (required.Count > 0)
        {
            schema["required"] = required;
        }
        return schema.ToJsonString();
    }

    private static JsonObject Compile(RecordFieldDefinition field, FieldScalarValueShape valueShape)
    {
        var jsonType = JsonType(valueShape);
        var schema = new JsonObject
        {
            ["type"] = jsonType,
        };
        if (!string.IsNullOrWhiteSpace(field.Pattern) && jsonType == "string")
        {
            schema["pattern"] = field.Pattern;
        }

        var valueDomain = field.ValueDomain ?? field.Constraints?.ValueDomain;
        if (valueDomain?.LiteralValues is { } values)
        {
            schema["enum"] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray());
        }

        AddIntegerParameter(schema, field.Kind.Parameters, "min_length", "minLength");
        AddIntegerParameter(schema, field.Kind.Parameters, "max_length", "maxLength");
        if (field.IsTranslatable)
            schema = new JsonObject { ["type"] = "object", ["additionalProperties"] = schema };
        if (field.Reference?.Cardinality == ReferenceCardinality.Many
            || field.Constraints?.MaximumCount > 1
            || field.Constraints is { MaximumCount: null })
        {
            var collection = new JsonObject { ["type"] = "array", ["items"] = schema };
            if (field.Constraints is { } bounds)
            {
                collection["minItems"] = Math.Max(bounds.MinimumCount, bounds.Required ? 1 : 0);
                if (bounds.MaximumCount is { } maximum) collection["maxItems"] = maximum;
            }
            return collection;
        }
        return schema;
    }

    private static string JsonType(FieldScalarValueShape valueShape)
    {
        return valueShape switch
        {
            FieldScalarValueShape.Text => "string",
            FieldScalarValueShape.Boolean => "boolean",
            FieldScalarValueShape.Integer => "integer",
            FieldScalarValueShape.Number => "number",
            _ => throw new ArgumentOutOfRangeException(nameof(valueShape), valueShape, "Unsupported field scalar value shape."),
        };
    }

    private static void AddIntegerParameter(
        JsonObject schema,
        IReadOnlyDictionary<string, string> parameters,
        string parameter,
        string keyword)
    {
        if (parameters.TryGetValue(parameter, out var text)
            && int.TryParse(
                text,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            schema[keyword] = value;
        }
    }
}

/// <summary>One stable, field-addressable refusal returned by Records admission.</summary>
/// <param name="Code">The stable refusal code.</param>
/// <param name="JsonPointer">The RFC 6901 location responsible for the refusal.</param>
/// <param name="Message">The non-sensitive diagnostic.</param>
public sealed record RecordsRefusal(string Code, string JsonPointer, string Message);

/// <summary>The complete structural refusal list for one Records intent.</summary>
/// <param name="Refusals">All refusals found without mutating a catalogue.</param>
public sealed record RecordsIntentValidationResult(IReadOnlyList<RecordsRefusal> Refusals)
{
    /// <summary>Gets whether the intent is structurally admitted.</summary>
    public bool IsAdmitted => Refusals.Count == 0;
}

/// <summary>The admitted identity and Trait facts used by reference validation and target filtering.</summary>
/// <param name="RecordTypeId">The target record-type identity.</param>
/// <param name="ClassId">The target record class.</param>
/// <param name="TraitIds">The Traits implemented by the target type.</param>
public sealed record RecordReferenceTargetFacts(
    string RecordTypeId,
    string ClassId,
    IReadOnlySet<string> TraitIds);

/// <summary>Owns the shared predicate used by reference writes and reference-target pickers.</summary>
public static class RecordsReferenceAdmission
{
    /// <summary>Returns whether one target satisfies the reference's type, class and Trait clauses.</summary>
    public static bool TargetMatches(FieldReferenceDefinition reference, RecordReferenceTargetFacts target)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(reference.TargetRecordTypeId)
            == string.IsNullOrWhiteSpace(reference.TargetClassId)) return false;
        return (string.IsNullOrWhiteSpace(reference.TargetRecordTypeId)
                || string.Equals(reference.TargetRecordTypeId, target.RecordTypeId, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(reference.TargetClassId)
                || string.Equals(reference.TargetClassId, target.ClassId, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(reference.RequiredTraitId)
                || target.TraitIds.Contains(reference.RequiredTraitId));
    }

    /// <summary>Returns a field-addressable refusal when a target does not satisfy the declared edge.</summary>
    public static RecordsIntentValidationResult Validate(
        FieldReferenceDefinition reference,
        RecordReferenceTargetFacts target,
        string referencePointer)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(referencePointer);
        var refusals = new List<RecordsRefusal>();
        if (!string.IsNullOrWhiteSpace(reference.RequiredTraitId)
            && !target.TraitIds.Contains(reference.RequiredTraitId))
        {
            refusals.Add(new RecordsRefusal(
                "records.reference.target_trait_missing",
                $"{referencePointer}/required_trait_id",
                "The reference target does not implement the required Trait."));
        }
        else if (!TargetMatches(reference, target))
        {
            refusals.Add(new RecordsRefusal(
                "records.reference.target_mismatch",
                referencePointer,
                "The reference target does not satisfy the declared type or class."));
        }
        return new RecordsIntentValidationResult(refusals);
    }
}

/// <summary>Validates authored Records intent before publish or install.</summary>
public sealed class RecordsIntentValidator
{
    private static readonly HashSet<string> ForeignBehaviorFlags = new(StringComparer.Ordinal)
    {
        "can_be_inspected",
        "can_carry_schedule",
        "can_be_exported",
        "has_workflow",
    };

    /// <summary>Validates raw authoring intent and names ruled negative boundaries before decoding.</summary>
    public RecordsIntentValidationResult ValidateJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var refusals = new List<RecordsRefusal>();
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            return new([new("records.definition.json_invalid", "", "The definition is not valid JSON.")]);
        }
        using var documentLifetime = document;
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            refusals.Add(new RecordsRefusal(
                "records.definition.object_required",
                "",
                "A Records definition must be a JSON object."));
            return new RecordsIntentValidationResult(refusals);
        }

        var root = document.RootElement;
        foreach (var property in root.EnumerateObject())
        {
            if (ForeignBehaviorFlags.Contains(property.Name))
            {
                refusals.Add(new RecordsRefusal(
                    "records.definition.foreign_behavior_flag",
                    $"/{EscapePointer(property.Name)}",
                    "Behavior declared by another primitive cannot be stored as a Records type flag."));
            }
        }
        if (root.TryGetProperty("class_ids", out var classIds)
            && (classIds.ValueKind != JsonValueKind.Array || classIds.GetArrayLength() != 1))
        {
            refusals.Add(new RecordsRefusal(
                "records.definition.class_count",
                "/class_ids",
                "A record type must belong to exactly one class."));
        }
        if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind == JsonValueKind.Object)
                {
                    AddForbiddenFieldMember(field, index, "enum", "records.field.inline_membership_forbidden");
                    AddForbiddenFieldMember(field, index, "regex", "records.field.inline_membership_forbidden");
                    AddForbiddenFieldMember(field, index, "retention_policy", "records.field.retention_forbidden");
                    AddForbiddenFieldMember(field, index, "retention_class", "records.field.retention_forbidden");
                    if (field.TryGetProperty("value_domain", out var domain)
                        && domain.ValueKind == JsonValueKind.Object
                        && domain.TryGetProperty("pattern", out _))
                    {
                        refusals.Add(new RecordsRefusal(
                            "records.field.pattern_membership_forbidden",
                            $"/fields/{index}/value_domain/pattern",
                            "Pattern constrains shape and cannot prove value-domain membership."));
                    }
                }
                index++;
            }
        }
        RecordsDefinitionShape.Validate(root, refusals);
        if (refusals.Count == 0)
        {
            try { refusals.AddRange(Validate(RecordsDefinitionJson.Deserialize(json)).Refusals); }
            catch (JsonException)
            {
                refusals.Add(new("records.definition.shape_invalid", "", "The definition does not match the Records grammar."));
            }
        }
        return new RecordsIntentValidationResult(refusals);

        void AddForbiddenFieldMember(JsonElement field, int index, string member, string code)
        {
            if (!field.TryGetProperty(member, out _))
            {
                return;
            }
            refusals.Add(new RecordsRefusal(
                code,
                $"/fields/{index}/{EscapePointer(member)}",
                code == "records.field.retention_forbidden"
                    ? "Retention policy is owned by the record type."
                    : "Permitted values must use exactly one declared value-domain source."));
        }
    }

    /// <summary>Returns every structural refusal for <paramref name="definition"/>.</summary>
    public RecordsIntentValidationResult Validate(
        RecordTypeDefinition definition,
        RecordTypeDefinition? publishedSameVersion = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var refusals = new List<RecordsRefusal>();
        RecordsDefinitionShape.Validate(RecordsDefinitionJson.AdmissionShape(definition), refusals);
        if (refusals.Count > 0) return new RecordsIntentValidationResult(refusals);
        if (string.IsNullOrWhiteSpace(definition.RecordTypeId))
        {
            refusals.Add(new RecordsRefusal(
                "records.definition.record_type_id_required",
                "/record_type_id",
                "Record type identity is required."));
        }

        if (publishedSameVersion is not null
            && string.Equals(definition.Envelope.DefinitionId, publishedSameVersion.Envelope.DefinitionId, StringComparison.Ordinal)
            && string.Equals(definition.Envelope.Version, publishedSameVersion.Envelope.Version, StringComparison.Ordinal)
            && !string.Equals(definition.RecordTypeId, publishedSameVersion.RecordTypeId, StringComparison.Ordinal))
        {
            refusals.Add(new RecordsRefusal(
                "records.definition.record_type_id_immutable",
                "/record_type_id",
                "Record type identity cannot change within a definition version."));
        }

        if (string.IsNullOrWhiteSpace(definition.ClassId))
        {
            refusals.Add(new RecordsRefusal(
                "records.definition.class_count",
                "/class_id",
                "A record type must belong to exactly one class."));
        }

        AddDuplicateIdentities(
            definition.Classes.Select((item, index) => (item.ClassId, index)),
            "records.class.identity_duplicate",
            "/classes/{0}/class_id",
            "Class identity must be unique within the definition.",
            refusals);

        var duplicateFieldKeys = definition.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .GroupBy(field => field.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < definition.Fields.Count; index++)
        {
            var field = definition.Fields[index];
            if (field.Constraints is { } constraints
                && (constraints.MinimumCount < 0 || constraints.MaximumCount < 0
                    || constraints.MaximumCount < Math.Max(constraints.MinimumCount, constraints.Required ? 1 : 0)))
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.multiplicity_invalid",
                    $"/fields/{index}/constraints",
                    "Multiplicity must have non-negative bounds and admit its required minimum."));
            }
            if (duplicateFieldKeys.Contains(field.Key))
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.identity_duplicate",
                    $"/fields/{index}/key",
                    "Field identity must be unique within its record type."));
            }

            ValidateValueDomain(field.ValueDomain, $"/fields/{index}/value_domain", refusals);
            ValidateValueDomain(field.Constraints?.ValueDomain, $"/fields/{index}/constraints/value_domain", refusals);

            if (field.Reference is { } reference && CountTargets(reference) != 1)
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.reference_target_count",
                    $"/fields/{index}/reference",
                    "A reference must name exactly one target."));
            }

            if (field.Reference is { TargetPackageId: { Length: > 0 } targetPackage }
                && !string.Equals(targetPackage, definition.Envelope.PackageId, StringComparison.Ordinal)
                && !(definition.Envelope.Requires?.Contains(targetPackage, StringComparer.Ordinal) ?? false))
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.cross_package_dependency_missing",
                    $"/fields/{index}/reference/target_package_id",
                    "A cross-package reference requires an explicit package dependency."));
            }

            if (field.IsIdentity && field.IsTranslatable)
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.identity_translatable",
                    $"/fields/{index}/is_translatable",
                    "An identity field cannot be translatable."));
            }

            if (!string.IsNullOrWhiteSpace(field.RefinesFieldKey)
                && field.Constraints is { } refinement
                && definition.Fields.FirstOrDefault(candidate =>
                    string.Equals(candidate.Key, field.RefinesFieldKey, StringComparison.Ordinal))?.Constraints is { } floor
                && !IsNarrowerOrEqual(refinement, floor))
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.refinement_widens",
                    $"/fields/{index}/constraints",
                    "A field refinement may narrow but cannot widen its declaring field."));
            }
        }

        ValidateTraitBindings(definition, refusals);

        return new RecordsIntentValidationResult(refusals);
    }

    private static void ValidateTraitBindings(
        RecordTypeDefinition definition,
        List<RecordsRefusal> refusals)
    {
        AddDuplicateIdentities(
            definition.Traits.Select((trait, index) => ($"{trait.TraitId}\u001f{trait.Version}", index)),
            "records.trait.version_identity_duplicate",
            "/traits/{0}/trait_id",
            "Trait identity and version must be unique within the definition.",
            refusals);
        for (var traitIndex = 0; traitIndex < definition.Traits.Count; traitIndex++)
        {
            var trait = definition.Traits[traitIndex];
            for (var slotIndex = 0; slotIndex < trait.Slots.Count; slotIndex++)
                ValidateValueDomain(trait.Slots[slotIndex].Constraints.ValueDomain,
                    $"/traits/{traitIndex}/slots/{slotIndex}/constraints/value_domain", refusals);
            AddDuplicateIdentities(
                trait.Slots.Select((slot, slotIndex) => (slot.SlotKey, slotIndex)),
                "records.trait.slot_identity_duplicate",
                $"/traits/{traitIndex}/slots/{{0}}/slot_key",
                "Trait slot identity must be unique within its trait.",
                refusals);
        }

        var traits = definition.Traits
            .GroupBy(trait => (trait.TraitId, trait.Version))
            .ToDictionary(group => group.Key, group => group.First());
        var fields = definition.Fields
            .Select((field, index) => (field, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.field.Key))
            .GroupBy(item => item.field.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First());
        var seenBindings = new HashSet<(string TraitId, string TraitVersion, string SlotKey)>();
        var resolvedByField = new Dictionary<string, List<FieldConstraintDefinition>>(StringComparer.Ordinal);

        for (var index = 0; index < definition.TraitBindings.Count; index++)
        {
            var binding = definition.TraitBindings[index];
            if (!traits.TryGetValue((binding.TraitId, binding.TraitVersion), out var trait))
            {
                refusals.Add(new RecordsRefusal(
                    "records.trait.version_unresolved",
                    $"/trait_bindings/{index}/trait_version",
                    "The bound Trait version is not declared."));
                continue;
            }

            var slot = trait.Slots.FirstOrDefault(candidate =>
                string.Equals(candidate.SlotKey, binding.SlotKey, StringComparison.Ordinal));
            if (slot is null)
            {
                refusals.Add(new RecordsRefusal(
                    "records.trait.slot_unresolved",
                    $"/trait_bindings/{index}/slot_key",
                    "The bound Trait slot is not declared."));
                continue;
            }

            if (!seenBindings.Add((binding.TraitId, binding.TraitVersion, binding.SlotKey)))
            {
                refusals.Add(new RecordsRefusal(
                    "records.trait.binding_ambiguous",
                    $"/trait_bindings/{index}",
                    "A qualified Trait slot may bind to only one field."));
            }

            if (!fields.TryGetValue(binding.FieldKey, out var field))
            {
                refusals.Add(new RecordsRefusal(
                    "records.trait.field_unresolved",
                    $"/trait_bindings/{index}/field_key",
                    "The bound Records field is not declared."));
                continue;
            }

            if (!resolvedByField.TryGetValue(binding.FieldKey, out var constraints))
            {
                constraints = [];
                resolvedByField.Add(binding.FieldKey, constraints);
            }
            constraints.Add(slot.Constraints);

            if (field.field.Constraints is { } fieldConstraints
                && !IsNarrowerOrEqual(fieldConstraints, slot.Constraints))
            {
                refusals.Add(new RecordsRefusal(
                    "records.trait.binding_widens",
                    $"/fields/{field.index}/constraints",
                    "A Trait binding may narrow but cannot widen its slot."));
            }
        }

        for (var traitIndex = 0; traitIndex < definition.Traits.Count; traitIndex++)
        {
            var trait = definition.Traits[traitIndex];
            for (var slotIndex = 0; slotIndex < trait.Slots.Count; slotIndex++)
            {
                var slot = trait.Slots[slotIndex];
                if ((slot.Required || slot.BlocksRelease)
                    && !seenBindings.Contains((trait.TraitId, trait.Version, slot.SlotKey)))
                {
                    refusals.Add(new RecordsRefusal(
                        "records.trait.slot_unfilled",
                        $"/traits/{traitIndex}/slots/{slotIndex}",
                        "A required Trait slot must be bound before publication."));
                }
            }
        }

        foreach (var (fieldKey, constraints) in resolvedByField)
        {
            var field = fields[fieldKey].field;
            if (field.Constraints is not null) constraints.Add(field.Constraints);
            if (field.ValueDomain is not null)
                constraints.Add(new(false, 0, null, [], field.ValueDomain));
            if (RecordsConstraintIntersection.TryResolve(constraints, out _))
            {
                continue;
            }

            refusals.Add(new RecordsRefusal(
                "records.trait.constraint_intersection_empty",
                $"/fields/{fields[fieldKey].index}/constraints",
                "The slots bound to this field have no admitted constraint intersection."));
        }
    }

    private static void ValidateValueDomain(
        ValueDomainDefinition? domain,
        string pointer,
        List<RecordsRefusal> refusals)
    {
        if (domain is not null && CountSources(domain) != 1)
            refusals.Add(new RecordsRefusal(
                "records.field.value_domain_source_count", pointer,
                "A value domain must name exactly one permitted-value source."));
    }

    private static int CountSources(ValueDomainDefinition valueDomain)
        => (valueDomain.LiteralValues is null ? 0 : 1)
            + (valueDomain.TaxonomyScheme is null ? 0 : 1)
            + (valueDomain.RecordQuery is null ? 0 : 1);

    private static int CountTargets(FieldReferenceDefinition reference)
        => (string.IsNullOrWhiteSpace(reference.TargetRecordTypeId) ? 0 : 1)
            + (string.IsNullOrWhiteSpace(reference.TargetClassId) ? 0 : 1);

    private static void AddDuplicateIdentities(
        IEnumerable<(string Identity, int Index)> candidates,
        string code,
        string pointerFormat,
        string message,
        List<RecordsRefusal> refusals)
    {
        var materialized = candidates.ToArray();
        var duplicates = materialized
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Identity))
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in materialized.Where(candidate => duplicates.Contains(candidate.Identity)))
        {
            refusals.Add(new RecordsRefusal(
                code,
                string.Format(System.Globalization.CultureInfo.InvariantCulture, pointerFormat, candidate.Index),
                message));
        }
    }

    private static string EscapePointer(string value)
        => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static bool IsNarrowerOrEqual(
        FieldConstraintDefinition candidate,
        FieldConstraintDefinition floor)
    {
        if (floor.Required && !candidate.Required)
        {
            return false;
        }
        if (candidate.MinimumCount < floor.MinimumCount)
        {
            return false;
        }
        if (floor.MaximumCount is not null
            && (candidate.MaximumCount is null || candidate.MaximumCount > floor.MaximumCount))
        {
            return false;
        }
        if (floor.ReadRoleIds.Count > 0
            && (candidate.ReadRoleIds.Count == 0
                || candidate.ReadRoleIds.Except(floor.ReadRoleIds, StringComparer.Ordinal).Any()))
        {
            return false;
        }
        return DomainIsSubset(candidate.ValueDomain, floor.ValueDomain);
    }

    private static bool DomainIsSubset(ValueDomainDefinition? candidate, ValueDomainDefinition? floor)
    {
        if (floor is null)
        {
            return true;
        }
        if (candidate is null)
        {
            return false;
        }
        if (candidate.LiteralValues is not null && floor.LiteralValues is not null)
        {
            return !candidate.LiteralValues.Except(floor.LiteralValues, StringComparer.Ordinal).Any();
        }
        return candidate == floor;
    }

    private static int? Minimum(int? left, int? right)
        => left is null ? right : right is null ? left : Math.Min(left.Value, right.Value);

    private static bool DomainsIntersect(ValueDomainDefinition? left, ValueDomainDefinition? right)
    {
        if (left is null || right is null)
        {
            return true;
        }
        if (left.LiteralValues is not null && right.LiteralValues is not null)
        {
            return left.LiteralValues.Intersect(right.LiteralValues, StringComparer.Ordinal).Any();
        }
        if (left.TaxonomyScheme is not null && right.TaxonomyScheme is not null)
        {
            return left.TaxonomyScheme == right.TaxonomyScheme;
        }
        if (left.RecordQuery is not null && right.RecordQuery is not null)
        {
            return left.RecordQuery == right.RecordQuery;
        }
        return false;
    }
}
