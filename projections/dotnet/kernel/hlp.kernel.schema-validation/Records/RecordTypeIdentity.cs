using System.Text.Json;
using Harborline.Contracts.Fields;

namespace Harborline.Kernel.SchemaValidation.Records;

/// <summary>The authored identity, fields, and explicit Trait memberships of one Record Type definition.</summary>
/// <param name="RecordTypeId">The stable identity that scopes every field key.</param>
/// <param name="Fields">The fields owned by this Record Type.</param>
/// <param name="Traits">The exact Trait revisions and their slot-to-field bindings.</param>
/// <param name="RetentionClockFieldId">
/// The field whose date starts the type's retention clock (records-ck-2 <c>retention_clock_field_id</c>), named
/// by its <c>field_key</c> since a field's identity is scoped by this type. The type still owns the retention
/// policy; the field only supplies the clock (records-ck-21, ADR 0095 ruling 3).
/// </param>
public sealed record RecordTypeDefinition(
    string RecordTypeId,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<TraitReference>? Traits = null,
    string? RetentionClockFieldId = null);

/// <summary>An authored Record Type field whose stable identity is its containing type and key.</summary>
/// <param name="FieldKey">The field's stable key within its record type.</param>
/// <param name="DisplayName">The author-facing label; not an identity.</param>
/// <param name="Binding">The field-runtime kind and constraint floor, including the one value domain.</param>
/// <param name="Governance">
/// The field's governance members (records-ck-13): created from its kind's defaults and editable afterwards.
/// </param>
/// <param name="DefaultsProvenance">
/// The kind revision whose creation defaults were materialized into this field, once (records-ck-38). Absent
/// until the field is first created through <see cref="RecordFieldDefaults"/>.
/// </param>
public sealed record FieldDefinition(
    string FieldKey,
    string DisplayName,
    FieldBindingDefinition? Binding = null,
    FieldGovernanceDefinition? Governance = null,
    FieldKindDefaultProvenance? DefaultsProvenance = null);

/// <summary>
/// Materializes a field kind's creation defaults into each newly created bound field (DES-0015 records-ck-38,
/// ADR 0095 ruling 3). A field is new until it carries <see cref="FieldDefinition.DefaultsProvenance"/>: this
/// fills its absent governance from the bound kind's defaults and stamps that kind revision as provenance.
/// A field that already carries provenance is left exactly as authored, so the defaults apply once and an
/// edit survives every later save, including a change of kind, which the compile then re-binds.
/// </summary>
public sealed class RecordFieldDefaults
{
    private readonly IFieldKindRuntime _kinds;

    /// <summary>Creates a materializer that reads defaults from the admitted field kinds.</summary>
    public RecordFieldDefaults(IFieldKindRuntime kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds;
    }

    /// <summary>
    /// Returns the definition with defaults materialized into every new bound field. A field whose kind does
    /// not resolve is left unchanged for the compile to refuse with its own pointer.
    /// </summary>
    public RecordTypeDefinition Materialize(RecordTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var fields = (definition.Fields ?? []).Select((field, index) => Materialize(field, index)).ToArray();
        return definition with { Fields = fields };
    }

    private FieldDefinition Materialize(FieldDefinition field, int index)
    {
        if (field.DefaultsProvenance is not null || field.Binding is null)
        {
            return field;
        }

        AdmittedFieldKind kind;
        try
        {
            kind = _kinds.Bind(field.Binding.Kind, $"/fields/{index}/binding/kind").Kind;
        }
        catch (FieldAdmissionException)
        {
            return field;
        }

        return field with
        {
            Governance = field.Governance ?? kind.GovernanceDefaults,
            DefaultsProvenance = new(kind.KindId, kind.Version),
        };
    }
}

/// <summary>A versioned Trait revision that declares the slots a member Record Type may bind.</summary>
/// <param name="TraitId">The stable Trait identity.</param>
/// <param name="Version">The immutable Trait revision required by a member type.</param>
/// <param name="Slots">The slots declared by this exact Trait revision.</param>
public sealed record TraitDefinition(string TraitId, string Version, IReadOnlyList<TraitSlotDefinition> Slots);

/// <summary>A qualified Trait field slot and the constraint floor every binding must satisfy.</summary>
/// <param name="SlotKey">The stable slot key within the Trait identity.</param>
/// <param name="Constraints">The field constraints supplied by the Trait revision.</param>
/// <param name="Required">Whether a member type must bind this slot before publication.</param>
/// <param name="BlocksRelease">Whether an absent binding of this required slot refuses release with the <c>records.trait.blocking_slot_unbound</c> code (records-auth-35) rather than <c>records.trait.required_slot_unbound</c>.</param>
public sealed record TraitSlotDefinition(
    string SlotKey,
    FieldConstraintDefinition Constraints,
    bool Required,
    bool BlocksRelease);

/// <summary>An exact Trait revision selected by a Record Type and its explicit slot bindings.</summary>
/// <param name="TraitId">The selected Trait identity.</param>
/// <param name="Version">The selected immutable Trait revision.</param>
/// <param name="SlotBindings">The authored mapping from qualified Trait slots to Record Type fields.</param>
public sealed record TraitReference(
    string TraitId,
    string Version,
    IReadOnlyList<TraitSlotBinding> SlotBindings);

/// <summary>One explicit mapping from a Trait Slot to a field owned by the containing Record Type.</summary>
/// <param name="SlotKey">The selected Trait Slot key.</param>
/// <param name="FieldKey">The Record Type field key that fulfils the slot.</param>
public sealed record TraitSlotBinding(string SlotKey, string FieldKey);

/// <summary>Resolves an exact Trait revision without selecting a latest revision or an alternate identity.</summary>
public interface IRecordTraitSource
{
    /// <summary>Returns the requested exact Trait revision, or null when that revision is unavailable.</summary>
    TraitDefinition? Resolve(string traitId, string version);
}

/// <summary>Validates the Records identity contract before a definition can mutate the schema registry.</summary>
public sealed class RecordsIntentValidator
{
    private readonly IRecordTraitSource? _traitSource;

    /// <summary>Creates an intent validator that resolves Trait revisions through the supplied source.</summary>
    public RecordsIntentValidator(IRecordTraitSource? traitSource = null)
    {
        _traitSource = traitSource;
    }

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

        ValidateTraitBindings(candidate, fieldKeys, refusals);

        return refusals;
    }

    internal TraitDefinition? ResolveTrait(string traitId, string version)
        => _traitSource?.Resolve(traitId, version);

    private void ValidateTraitBindings(
        RecordTypeDefinition candidate,
        IReadOnlySet<string> fieldKeys,
        List<FieldRefusal> refusals)
    {
        // A qualified slot is (trait id, slot key), so a second reference to the same Trait may not bind it again.
        var qualifiedBoundSlots = new HashSet<(string TraitId, string SlotKey)>();
        foreach (var (reference, traitIndex) in (candidate.Traits ?? []).Select((trait, index) => (trait, index)))
        {
            var traitPointer = $"/traits/{traitIndex}";
            var definition = _traitSource?.Resolve(reference.TraitId, reference.Version);
            if (definition is null)
            {
                refusals.Add(new(
                    "records.trait.version_unresolved",
                    traitPointer,
                    "The selected Trait identity and version are not available."));
                continue;
            }

            var slots = definition.Slots ?? [];
            var declaredSlots = new HashSet<string>(slots.Select(slot => slot.SlotKey), StringComparer.Ordinal);
            var bindings = reference.SlotBindings ?? [];
            var boundSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (binding, bindingIndex) in bindings.Select((binding, index) => (binding, index)))
            {
                var bindingPointer = traitPointer + "/slot_bindings/" + bindingIndex;
                if (!boundSlots.Add(binding.SlotKey) || !qualifiedBoundSlots.Add((reference.TraitId, binding.SlotKey)))
                {
                    refusals.Add(new(
                        "records.trait.slot_binding_ambiguous",
                        bindingPointer + "/slot_key",
                        "A Trait Slot can bind to only one Record Type field."));
                }
                if (!declaredSlots.Contains(binding.SlotKey))
                {
                    refusals.Add(new(
                        "records.trait.slot_unknown",
                        bindingPointer + "/slot_key",
                        "The binding names no Slot in the selected Trait revision."));
                }
                if (!fieldKeys.Contains(binding.FieldKey))
                {
                    refusals.Add(new(
                        "records.trait.field_unresolved",
                        bindingPointer + "/field_key",
                        "The binding must name a field owned by this Record Type."));
                }
            }

            foreach (var slot in slots.Where(slot => slot.Required && !boundSlots.Contains(slot.SlotKey)))
            {
                refusals.Add(new(
                    slot.BlocksRelease ? "records.trait.blocking_slot_unbound" : "records.trait.required_slot_unbound",
                    traitPointer + "/slot_bindings",
                    "A required Trait Slot must bind to a Record Type field before publication."));
            }
        }
    }
}

/// <summary>The result of compiling one Record Type grammar to a registered schema.</summary>
public sealed record RecordTypeSchemaCompilation(Schema? Schema, IReadOnlyList<FieldRefusal> Refusals);

/// <summary>
/// The result of compiling one Record Type grammar without registering it: the draft 2020-12 schema text the
/// registry would derive its content-addressed identity from, or every refusal and no text.
/// </summary>
public sealed record RecordTypeSchemaDraft(string? JsonSchemaText, IReadOnlyList<FieldRefusal> Refusals);

/// <summary>Compiles admitted Record Type grammar to a draft 2020-12 JSON Schema document.</summary>
public sealed class RecordTypeSchemaCompiler
{
    private readonly RecordsIntentValidator _intentValidator;
    private readonly IFieldKindRuntime? _fieldKindRuntime;
    private readonly IValueDomainAdmission? _valueDomainAdmission;
    private readonly IFieldDomainRuntime? _fieldDomainRuntime;

    /// <summary>Creates a compiler using the supplied identity validator and field-runtime admission.</summary>
    public RecordTypeSchemaCompiler(
        RecordsIntentValidator? intentValidator = null,
        IFieldKindRuntime? fieldKindRuntime = null,
        IValueDomainAdmission? valueDomainAdmission = null,
        IFieldDomainRuntime? fieldDomainRuntime = null)
    {
        _intentValidator = intentValidator ?? new RecordsIntentValidator();
        _fieldKindRuntime = fieldKindRuntime;
        _valueDomainAdmission = valueDomainAdmission;
        _fieldDomainRuntime = fieldDomainRuntime;
    }

    /// <summary>The identity validator this compiler runs first, so a caller's structural admission applies the same rules.</summary>
    public RecordsIntentValidator IntentValidator => _intentValidator;

    /// <summary>
    /// Validates a candidate before registering its derived schema. Refused candidates never mutate the registry.
    /// </summary>
    public async ValueTask<RecordTypeSchemaCompilation> CompileAndRegisterAsync(
        RecordTypeDefinition candidate,
        RecordTypeDefinition? previousVersion,
        ISchemaRegistry schemaRegistry,
        FieldDomainScope? fieldDomainScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(schemaRegistry);

        var draft = await CompileAsync(candidate, previousVersion, fieldDomainScope, cancellationToken);
        if (draft.JsonSchemaText is null)
        {
            return new(null, draft.Refusals);
        }

        var schema = await schemaRegistry.RegisterAsync(draft.JsonSchemaText, cancellationToken: cancellationToken);
        return new(schema, []);
    }

    /// <summary>
    /// Validates and compiles a candidate without touching any registry, so an admission can collect every
    /// refusal before the caller commits anything. Registering the returned text yields the same schema
    /// identity <see cref="CompileAndRegisterAsync"/> would.
    /// </summary>
    public async ValueTask<RecordTypeSchemaDraft> CompileAsync(
        RecordTypeDefinition candidate,
        RecordTypeDefinition? previousVersion,
        FieldDomainScope? fieldDomainScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var refusals = new List<FieldRefusal>(_intentValidator.Validate(candidate, previousVersion));
        // C:/Projects/Harborline/harborline-control/designs/DES-0015-records/design.md:102 (records-ck-37)
        // requires a shared field to satisfy every bound slot's admitted intersection.
        // C:/Projects/Harborline/harborline-control/designs/DES-0015-records/design.md:104 (records-ck-39)
        // permits narrowing a slot floor but forbids widening its domain.
        await NarrowTraitBindingsAsync(candidate, fieldDomainScope, refusals, cancellationToken);
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        var boundKinds = new Dictionary<string, AdmittedFieldKind>(StringComparer.Ordinal);
        foreach (var (field, index) in (candidate.Fields ?? []).Select((field, index) => (field, index)))
        {
            if (BindField(field.Binding, $"/fields/{index}/binding", refusals) is { } compiled)
            {
                boundKinds.TryAdd(field.FieldKey, compiled.Kind);
                if (refusals.Count == 0)
                {
                    properties.Add(field.FieldKey, compiled.Schema);
                }
            }
            else if (field.Binding is null && refusals.Count == 0)
            {
                properties.Add(field.FieldKey, new Dictionary<string, string> { ["type"] = "string" });
            }
        }

        ValidateRetentionClock(candidate, boundKinds, refusals);

        if (refusals.Count != 0)
        {
            return new(null, refusals.AsReadOnly());
        }

        var document = new Dictionary<string, object>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        return new(JsonSerializer.Serialize(document), []);
    }

    // A field without a binding declares no constraint, so each member takes the absent meaning the
    // contract documents (Harborline.Contracts FieldDefinitions.cs FieldConstraintDefinition params):
    // not always required, no minimum, no finite maximum, no read roles, no value domain. The field
    // runtime then decides whether that widens the slot; Records never skips the proof.
    private static readonly FieldConstraintDefinition Unconstrained = new(false, 0, null, [], null);

    private async ValueTask NarrowTraitBindingsAsync(
        RecordTypeDefinition candidate,
        FieldDomainScope? fieldDomainScope,
        List<FieldRefusal> refusals,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal);
        foreach (var field in candidate.Fields ?? [])
        {
            fields.TryAdd(field.FieldKey, field);
        }

        foreach (var (reference, traitIndex) in (candidate.Traits ?? []).Select((trait, index) => (trait, index)))
        {
            var trait = _intentValidator.ResolveTrait(reference.TraitId, reference.Version);
            if (trait is null)
            {
                continue;
            }

            foreach (var (binding, bindingIndex) in (reference.SlotBindings ?? []).Select((binding, index) => (binding, index)))
            {
                var slot = (trait.Slots ?? []).FirstOrDefault(candidateSlot => candidateSlot.SlotKey == binding.SlotKey);
                if (slot is null || !fields.TryGetValue(binding.FieldKey, out var field))
                {
                    continue;
                }

                var pointer = $"/traits/{traitIndex}/slot_bindings/{bindingIndex}";
                if (_fieldDomainRuntime is null || fieldDomainScope is null)
                {
                    refusals.Add(new(
                        "records.field.runtime_required",
                        pointer,
                        "A Trait Slot binding requires the field runtime and its domain scope to prove it does not widen the slot."));
                    continue;
                }

                try
                {
                    await _fieldDomainRuntime.NarrowAsync(
                        slot.Constraints,
                        field.Binding?.Constraints ?? Unconstrained,
                        fieldDomainScope,
                        pointer,
                        cancellationToken);
                }
                catch (FieldAdmissionException exception)
                {
                    refusals.AddRange(exception.Refusals);
                }
            }
        }
    }

    // records-ck-38 / ADR 0095 ruling 3: the clock field must be this type's own and its bound kind revision must
    // declare the retention_clock capability. Records checks the capability's presence only; the date/time
    // meaning belongs to the kind's schema.
    private static void ValidateRetentionClock(
        RecordTypeDefinition candidate,
        IReadOnlyDictionary<string, AdmittedFieldKind> boundKinds,
        List<FieldRefusal> refusals)
    {
        if (candidate.RetentionClockFieldId is not { } clock)
        {
            return;
        }

        if (!(candidate.Fields ?? []).Any(field => string.Equals(field.FieldKey, clock, StringComparison.Ordinal)))
        {
            refusals.Add(new(
                "records.retention.clock_field_unresolved",
                "/retention_clock_field_id",
                "The retention clock must name a field owned by this Record Type."));
            return;
        }

        if (!boundKinds.TryGetValue(clock, out var kind)
            || !(kind.Capabilities ?? []).Contains(FieldKindCapability.RetentionClock))
        {
            refusals.Add(new(
                "records.retention.clock_capability_absent",
                "/retention_clock_field_id",
                "The retention clock field's bound kind revision does not declare the retention_clock capability."));
        }
    }

    // The kind's schema and the value domain's source count are the field runtime's rules;
    // Records only routes each binding to them and keeps the authored pointer. An unbound field returns null.
    private (object Schema, AdmittedFieldKind Kind)? BindField(FieldBindingDefinition? binding, string pointer, List<FieldRefusal> refusals)
    {
        if (binding is null)
        {
            return null;
        }

        if (_fieldKindRuntime is null || _valueDomainAdmission is null)
        {
            refusals.Add(new(
                "records.field.runtime_required",
                pointer,
                "A bound field requires the field runtime to admit its kind and value domain."));
            return null;
        }

        if (binding.Constraints?.ValueDomain is { } domain)
        {
            refusals.AddRange(_valueDomainAdmission.Validate(domain, pointer + "/constraints/value_domain"));
        }

        try
        {
            var compiled = _fieldKindRuntime.Bind(binding.Kind, pointer + "/kind");
            return (compiled.JsonSchema, compiled.Kind);
        }
        catch (FieldAdmissionException exception)
        {
            refusals.AddRange(exception.Refusals);
            return null;
        }
    }
}
