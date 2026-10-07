using System.Text;
using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A new Record Type as its author describes it. The caller never supplies its id.</summary>
/// <param name="Tenant">The owning tenant.</param>
/// <param name="Section">The catalogue section that scopes the minted id.</param>
/// <param name="Name">The author-facing name the id is derived from.</param>
/// <param name="Version">The first draft's semantic version.</param>
/// <param name="Fields">The fields owned by the type.</param>
/// <param name="Contract">The authored definition contract version.</param>
/// <param name="Traits">The exact Trait revisions and their slot bindings.</param>
/// <param name="RetentionClockFieldId">The <c>field_key</c> of the field whose date starts the retention clock.</param>
/// <param name="ClassId">The type's one Class, required before publication.</param>
/// <param name="RecordClass">Reference, master or transactional, required before publication.</param>
/// <param name="PackageId">The owning package, required before publication.</param>
/// <param name="Requires">The declared package dependencies, each <c>pack-key@interfaceVersion</c>.</param>
/// <param name="Exposes">The target-side declaration, when other packages may reference the type.</param>
public sealed record NewRecordType(
    string Tenant,
    string Section,
    string Name,
    string Version,
    IReadOnlyList<FieldDefinition> Fields,
    DefinitionContractVersion? Contract,
    IReadOnlyList<TraitReference>? Traits = null,
    string? RetentionClockFieldId = null,
    string? ClassId = null,
    RecordClass? RecordClass = null,
    string? PackageId = null,
    IReadOnlyList<RecordsRequirement>? Requires = null,
    RecordsExposure? Exposes = null);

/// <summary>A created Record Type draft and the id the authoring boundary minted for it.</summary>
public sealed record RecordTypeDraft(string RecordTypeId, DefinitionRevision Revision);

/// <summary>A published Record Type and the schema its records validate against.</summary>
/// <param name="Revision">The published catalogue revision.</param>
/// <param name="Schema">The registered schema compiled from exactly the published body.</param>
public sealed record RecordTypePublication(DefinitionRevision Revision, Schema Schema);

/// <summary>
/// Composes Records admission (DES-0015) with the shared versioned-definition store under
/// <see cref="DefinitionKind.Records"/> (T-615). It is the production authoring boundary for Record Types:
/// <see cref="CreateDraftAsync"/> mints each id from a section and a name, and every later save names an id
/// that boundary minted. The catalogue key is the tenant and the <c>record_type_id</c>; the immutable version
/// id is the definition's semantic version. This type adds no second catalogue.
/// </summary>
/// <remarks>
/// The store's own validator (<see cref="Admission"/>) is synchronous and structural. The schema compile is
/// asynchronous because the field runtime proves trait-slot narrowing, so this type runs it before handing a
/// snapshot to the store, fenced on the same expected revision the store commits at. Every refusal, structural
/// or compiled, is raised before the store or the schema registry changes. Publication registers the compiled
/// schema before the store makes the version visible, so a published version always has its schema.
/// </remarks>
public sealed class RecordTypeDefinitionStore
{
    private readonly IVersionedDefinitionStore _store;
    private readonly RecordTypeSchemaCompiler _compiler;
    private readonly RecordFieldDefaults _defaults;
    private readonly ISchemaRegistry _registry;
    private readonly DefinitionContractWindow _window;
    private readonly FieldDomainScope? _fieldDomainScope;

    /// <summary>
    /// Wraps a host's shared store. The host registers <see cref="Admission"/> for
    /// <see cref="DefinitionKind.Records"/> with the same validator the compiler uses.
    /// </summary>
    public RecordTypeDefinitionStore(IVersionedDefinitionStore store, RecordTypeSchemaCompiler compiler,
        RecordFieldDefaults defaults, ISchemaRegistry registry, DefinitionContractWindow window,
        FieldDomainScope? fieldDomainScope = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(defaults);
        _defaults = defaults;
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(window);
        _store = store;
        _compiler = compiler;
        _registry = registry;
        _window = window;
        _fieldDomainScope = fieldDomainScope;
    }

    /// <summary>
    /// The store validator for <see cref="DefinitionKind.Records"/>: the body parses as a Record Type, its
    /// contract is inside the window, it names the catalogue's tenant (Author and Publish; an installing host
    /// supplies its own tenant), its id is the catalogue key and is scoped by its section, and the Records
    /// identity rules hold. It never resolves a schema or touches a registry.
    /// </summary>
    public static DefinitionAdmission Admission(DefinitionContractWindow window, RecordsIntentValidator validator)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(validator);
        return (document, phase) => Structural(document, phase, window, validator).Refusals;
    }

    /// <summary>The shared-store key for one tenant's Record Type.</summary>
    public static DefinitionKey KeyOf(string tenant, string recordTypeId) => new(tenant, DefinitionKind.Records, recordTypeId);

    /// <summary>
    /// The authoring boundary that creates a Record Type (L102, DES-0015 K5). It mints the id as
    /// <c>section.name-slug</c>, so the same name in two sections is two types, and refuses
    /// <c>records.identity.record_type_id_collision</c> when the section already holds that id rather than
    /// sequencing a near-duplicate. A replay of the same request returns the original draft.
    /// </summary>
    public async ValueTask<RecordTypeDraft> CreateDraftAsync(NewRecordType request, string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recordTypeId = RecordsCatalogueIdentity.Mint(request.Section, request.Name);
        var document = Materialize(new RecordTypeDocument(
            new(request.Tenant, request.Section, request.Contract, request.PackageId, request.Requires, request.Exposes),
            request.Name!, recordTypeId, request.Fields, request.Traits, request.RetentionClockFieldId,
            request.ClassId, request.RecordClass));
        var candidate = await AdmitAsync(Catalogue(document, recordTypeId, request.Version), DefinitionAdmissionPhase.Author,
            cancellationToken).ConfigureAwait(false);
        return new(recordTypeId, await RecordsCatalogueIdentity.CreateAsync(_store, candidate.Document, requestId,
            "record_type_id", cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Saves a draft of the Record Type <paramref name="recordTypeId"/>, which the authoring boundary must
    /// already have minted: an id with no catalogue stream, constructed by a client, refuses
    /// <c>records.identity.record_type_id_unminted</c>. A body naming any other id refuses
    /// <c>records.identity.record_type_id_immutable</c>; a different id is a different definition.
    /// </summary>
    public async ValueTask<DefinitionRevision> SaveDraftAsync(string recordTypeId, RecordTypeDocument document,
        string version, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var catalogue = Catalogue(Materialize(document), recordTypeId, version);
        var candidate = await AdmitAsync(catalogue, DefinitionAdmissionPhase.Author, cancellationToken).ConfigureAwait(false);
        await RecordsCatalogueIdentity.RequireMintedAsync(_store, catalogue.Key, "record_type_id", cancellationToken)
            .ConfigureAwait(false);
        return await _store.SaveDraftAsync(candidate.Document, expectedRevision, requestId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compiles the stored version, registers exactly the compiled schema, then immutably publishes it. A registry
    /// failure (a fault, cancellation, or a schema over the registry's size limit) therefore leaves nothing
    /// published. Registration is content-addressed and idempotent, so a replay re-registers harmlessly. A stale
    /// <paramref name="expectedRevision"/> refuses before registration; one that goes stale between that check and
    /// the store's own fence is still refused by the store, leaving at most an unreferenced registered schema. The
    /// store commits only at <paramref name="expectedRevision"/>, so the published body is the one compiled.
    /// Referenced published heads are fenced atomically at that commit. A head that changes after schema
    /// registration refuses publication and may leave an unreferenced content-addressed schema. Exact request
    /// replays recover the committed body and original target observations before consulting live heads.
    /// </summary>
    public async ValueTask<RecordTypePublication> PublishAsync(string tenant, string recordTypeId, string version,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        var key = KeyOf(tenant, recordTypeId);
        var replay = await _store.GetPublicationReplayAsync(key, version, expectedRevision, requestId, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            // Conditions are observations belonging to the first committed operation, not new input
            // from a retry. Recover its immutable body before consulting any live target catalogue.
            var replayAdmission = await AdmitAsync(replay.Document, DefinitionAdmissionPhase.Publish, cancellationToken)
                .ConfigureAwait(false);
            var replaySchema = await _registry.RegisterAsync(replayAdmission.JsonSchemaText, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new(replay, replaySchema);
        }
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
        var source = history.LastOrDefault(revision => revision.Document.VersionId == version)
            ?? throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new("definition.not_found", "/versionId")]);

        var admitted = await AdmitAsync(source.Document, DefinitionAdmissionPhase.Publish, cancellationToken).ConfigureAwait(false);
        // The registered validator cannot see other definitions, so this boundary resolves the type's catalogue
        // edges before the store publishes: its home (records-ck-18) and every reference target (records-ck-10).
        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(source.Document.BodyJson));
        var conditions = new List<DefinitionPublishedHeadCondition>();
        var unresolved = await ResolveCatalogueEdgesAsync(tenant, document, cancellationToken, conditions).ConfigureAwait(false);
        if (unresolved.Count > 0)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, unresolved);
        // A published source is a replay, which the store answers from its record at any revision.
        if (source.Status != DefinitionStatus.Published && history[^1].Revision != expectedRevision)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new("definition.revision_conflict", "/expectedRevision")]);
        var schema = await _registry.RegisterAsync(admitted.JsonSchemaText, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var published = await _store.PublishAsync(key, version, expectedRevision, requestId, conditions, cancellationToken)
                .ConfigureAwait(false);
            return new(published, schema);
        }
        catch (DefinitionRefusalException refused) when (refused.Refusals.Any(item => item.Code == "definition.published_head_conflict"))
        {
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, refused.Refusals.Select(item =>
                item.Code == "definition.published_head_conflict" ? new DefinitionRefusal("records.reference.pin_stale", item.Pointer) : item).ToArray());
        }
    }

    /// <summary>
    /// Authoring diagnostics (records-ck-41): every catalogue-edge refusal publication would raise against current state,
    /// reported at the Author stage without writing anything.
    /// </summary>
    public async ValueTask<DefinitionRefusalReport> DiagnoseAsync(string tenant, RecordTypeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(DefinitionAdmissionPhase.Author,
            await ResolveCatalogueEdgesAsync(tenant, document, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Writes each cross-package reference's pin from current state (records-ck-41): the target's package, id, published
    /// version, body digest and exposed interface version. A same-package reference loses any pin; a target that is
    /// unpublished or unexposed gets none, for publication to refuse. The author saves the result as a draft, and
    /// publication seals it by refusing any pin that no longer matches.
    /// </summary>
    public async ValueTask<RecordTypeDocument> PinReferencesAsync(string tenant, RecordTypeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fields = new List<FieldDefinition>();
        foreach (var field in document.Fields ?? [])
        {
            if (field.Reference is not { } reference)
            {
                fields.Add(field);
                continue;
            }
            var target = await TargetAsync(tenant, document, reference, cancellationToken).ConfigureAwait(false);
            var pin = target is { Revision: { } revision, Envelope.Exposes: { } exposes }
                && !SamePackage(document.Envelope, target.Envelope)
                ? new RecordReferencePin(target.Envelope.PackageId!, revision.Document.Key.DefinitionId, revision.Document.Version,
                    CatalogueFieldSource.DigestOf(revision), exposes.InterfaceVersion)
                : null;
            fields.Add(field with { Reference = reference with { Pin = pin } });
        }
        return document with { Fields = fields };
    }

    private async ValueTask<List<DefinitionRefusal>> ResolveCatalogueEdgesAsync(string tenant, RecordTypeDocument document,
        CancellationToken cancellationToken, List<DefinitionPublishedHeadCondition>? conditions = null)
    {
        var refusals = new List<DefinitionRefusal>();
        if (document.ClassId is { } classId
            && await _store.GetPublishedHeadAsync(ClassDefinitionStore.KeyOf(tenant, classId), cancellationToken).ConfigureAwait(false) is null)
            refusals.Add(new("records.class.unresolved", "/class_id"));
        foreach (var (field, index) in (document.Fields ?? []).Select((field, index) => (field, index)))
        {
            if (field.Reference is not { } reference)
                continue;
            var pointer = $"/fields/{index}/reference";
            var target = await TargetAsync(tenant, document, reference, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                refusals.Add(new("records.reference.target_unresolved",
                    pointer + (string.IsNullOrWhiteSpace(reference.TargetClassId) ? "/target_type_id" : "/target_class_id")));
                continue;
            }

            if (target.Revision is { } observed)
                conditions?.Add(new(observed.Document.Key, observed.Revision, pointer));

            // A required trait is checked against a target type; Class membership is derived (ADR-0054), so a Class
            // target's trait is checked per record at write time.
            if (reference.RequiredTraitId is { } trait && target.Traits is { } traits
                && !traits.Any(declared => StringComparer.Ordinal.Equals(declared.TraitId, trait)))
                refusals.Add(new("records.reference.trait_absent", pointer + "/required_trait_id"));
            refusals.AddRange(CrossPackage(document, reference, target, pointer));
        }
        return refusals;
    }

    // records-ck-41 / records-auth-38: an edge into another package needs this definition's requires entry for that
    // package, the target's exposure at the same interface version, and a pin equal to the target's current published
    // version, digest and exposure. An edge inside one package carries no pin.
    // The shared K9 check reports at the author stage; Records raises its refusals at its own stage.
    private static IEnumerable<DefinitionRefusal> CrossPackage(RecordTypeDocument document, RecordReferenceDefinition reference,
        EdgeTarget target, string pointer)
    {
        if (SamePackage(document.Envelope, target.Envelope))
        {
            if (reference.Pin is not null)
                yield return new("records.reference.pin_unexpected", pointer + "/pin");
            yield break;
        }

        var requires = (document.Envelope?.Requires ?? [])
            .Select(requirement => RecordsRequirement.TryParse(requirement?.Capability, out var package, out var version)
                ? (Package: package, Version: version) : (Package: "", Version: 0))
            .Where(requirement => requirement.Package.Length > 0)
            .ToArray();
        var targetPackage = target.Envelope.PackageId ?? "";
        var revision = target.Revision;
        var current = revision is null ? null
            : new CrossPackageEndpoint(targetPackage, revision.Document.Key.DefinitionId, revision.Document.Version, CatalogueFieldSource.DigestOf(revision));
        if (reference.Pin is not { } pin)
        {
            yield return new("records.reference.pin_required", pointer + "/pin");
            yield break;
        }

        var report = CrossPackageEdges.Check(requires.Select(requirement => requirement.Package),
            [new(pointer, new(document.Envelope?.PackageId ?? "", document.RecordTypeId, "", ""),
                new(pin.PackageId, pin.DefinitionId, pin.Version, pin.Digest))],
            target.Envelope.Exposes is null || current is null ? [] : [new(targetPackage, [current])],
            "records.reference.dependency_undeclared", "records.reference.not_exposed", "records.reference.pin_stale");
        foreach (var refusal in report.Refusals)
            yield return refusal;
        if (report.Refusals.Count > 0)
            yield break;

        if (!StringComparer.Ordinal.Equals(pin.PackageId, targetPackage) || pin.InterfaceVersion != target.Envelope.Exposes!.InterfaceVersion)
            yield return new("records.reference.pin_stale", pointer);
        else if (!requires.Any(requirement => requirement.Package == targetPackage && requirement.Version == pin.InterfaceVersion))
            yield return new("records.reference.interface_incompatible", pointer);
    }

    private static bool SamePackage(RecordsDefinitionEnvelope? source, RecordsDefinitionEnvelope target)
        => StringComparer.Ordinal.Equals(source?.PackageId ?? "", target.PackageId ?? "");

    // The target's published head, its envelope, and for a type target its declared traits. A self-reference resolves
    // to the type being authored, which has no published revision of its own to pin and is always in its own package.
    private async ValueTask<EdgeTarget?> TargetAsync(string tenant, RecordTypeDocument document, RecordReferenceDefinition reference,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(reference.TargetClassId))
        {
            var head = await _store.GetPublishedHeadAsync(ClassDefinitionStore.KeyOf(tenant, reference.TargetClassId), cancellationToken).ConfigureAwait(false);
            return head is null ? null
                : new(head, RecordsJson.Deserialize<ClassDocument>(Encoding.UTF8.GetBytes(head.Document.BodyJson)).Envelope, null);
        }
        if (StringComparer.Ordinal.Equals(reference.TargetTypeId, document.RecordTypeId))
            return new(null, document.Envelope, document.Traits ?? []);
        var typeHead = await _store.GetPublishedHeadAsync(KeyOf(tenant, reference.TargetTypeId!), cancellationToken).ConfigureAwait(false);
        if (typeHead is null)
            return null;
        var type = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(typeHead.Document.BodyJson));
        return new(typeHead, type.Envelope, type.Traits ?? []);
    }

    private sealed record EdgeTarget(DefinitionRevision? Revision, RecordsDefinitionEnvelope Envelope, IReadOnlyList<TraitReference>? Traits);

    /// <summary>Copies a published body into a new draft version through the shared store.</summary>
    public ValueTask<DefinitionRevision> RestoreAsDraftAsync(string tenant, string recordTypeId, string sourceVersion,
        string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        => _store.RestoreAsDraftAsync(KeyOf(tenant, recordTypeId), sourceVersion, draftVersion, draftVersion,
            expectedRevision, requestId, cancellationToken);

    /// <summary>Returns the highest published version of a Record Type, or null when none is published.</summary>
    public async ValueTask<RecordTypeDocument?> GetPublishedHeadAsync(string tenant, string recordTypeId,
        CancellationToken cancellationToken = default)
    {
        var head = await _store.GetPublishedHeadAsync(KeyOf(tenant, recordTypeId), cancellationToken).ConfigureAwait(false);
        return head is null ? null : RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(head.Document.BodyJson));
    }

    /// <summary>
    /// The installation gate (records-ck-41): Install-phase admission and the schema compile over an exported entry, then
    /// every reference checked against what the installing host was handed, never its live catalogue. An unpinned
    /// reference must target a definition the same pack ships; a pinned one needs this definition's <c>requires</c> entry
    /// at the pin's interface version and must equal the exposure its pinned dependency closure declares. Every refusal
    /// comes back together and nothing is written, so a host installs the edge set whole or not at all.
    /// </summary>
    public async ValueTask<DefinitionRefusalReport> AdmitInstallAsync(string tenant, RecordTypeDefinitionPackageEntry entry,
        RecordsInstallClosure closure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(closure);
        var document = new DefinitionDocument(KeyOf(tenant, entry.DefinitionId), entry.Version, entry.Version,
            Encoding.UTF8.GetString(entry.Content.Payload.Span));
        try
        {
            await AdmitAsync(document, DefinitionAdmissionPhase.Install, cancellationToken).ConfigureAwait(false);
        }
        catch (DefinitionRefusalException refused)
        {
            return new(refused.Stage, refused.Refusals);
        }
        var parsed = RecordTypeDefinitionJson.Deserialize(entry.Content.Payload.Span);
        return new(DefinitionAdmissionPhase.Install, InstallEdges(parsed, closure).ToArray());
    }

    private static IEnumerable<DefinitionRefusal> InstallEdges(RecordTypeDocument document, RecordsInstallClosure closure)
    {
        var own = document.Envelope?.PackageId ?? "";
        var requires = (document.Envelope?.Requires ?? [])
            .Select(requirement => RecordsRequirement.TryParse(requirement?.Capability, out var package, out var version)
                ? (Package: package, Version: version) : (Package: "", Version: 0))
            .ToArray();
        foreach (var (field, index) in (document.Fields ?? []).Select((field, index) => (field, index)))
        {
            if (field.Reference is not { } reference)
                continue;
            var pointer = $"/fields/{index}/reference";
            var classTarget = !string.IsNullOrWhiteSpace(reference.TargetClassId);
            var targetKind = classTarget ? DefinitionKind.Classes : DefinitionKind.Records;
            var targetId = classTarget ? reference.TargetClassId! : reference.TargetTypeId!;
            if (reference.Pin is not { } pin)
            {
                // An edge inside the pack: its target ships beside it, or is this type itself.
                if (!(targetKind == DefinitionKind.Records && StringComparer.Ordinal.Equals(targetId, document.RecordTypeId))
                    && !closure.PackDefinitions.Contains(new InstallDefinitionTarget(targetKind, targetId)))
                    yield return new("records.reference.target_unresolved", pointer + (classTarget ? "/target_class_id" : "/target_type_id"));
                continue;
            }

            if (StringComparer.Ordinal.Equals(pin.PackageId, own))
            {
                yield return new("records.reference.pin_unexpected", pointer + "/pin");
                continue;
            }
            if (!StringComparer.Ordinal.Equals(pin.DefinitionId, targetId))
            {
                yield return new("records.reference.pin_stale", pointer);
                continue;
            }
            if (!requires.Any(requirement => requirement.Package == pin.PackageId))
                yield return new("records.reference.dependency_undeclared", pointer);
            else if (!requires.Any(requirement => requirement.Package == pin.PackageId && requirement.Version == pin.InterfaceVersion))
                yield return new("records.reference.interface_incompatible", pointer);

            var declared = closure.Exposures.FirstOrDefault(exposure =>
                StringComparer.Ordinal.Equals(exposure.PackageId, pin.PackageId) && exposure.Kind == targetKind
                && StringComparer.Ordinal.Equals(exposure.DefinitionId, pin.DefinitionId));
            if (declared is null)
                yield return new("records.reference.closure_missing", pointer + "/pin");
            else if (declared != new PinnedExposure(pin.PackageId, targetKind, pin.DefinitionId, pin.Version, pin.Digest, pin.InterfaceVersion))
                yield return new("records.reference.closure_changed", pointer + "/pin");
        }
    }

    private async ValueTask<(DefinitionDocument Document, string JsonSchemaText)> AdmitAsync(DefinitionDocument document,
        DefinitionAdmissionPhase phase, CancellationToken cancellationToken)
    {
        var structural = Structural(document, phase, _window, _compiler.IntentValidator);
        if (structural.Refusals.Count > 0)
            throw new DefinitionRefusalException(phase, structural.Refusals);
        var draft = await _compiler.CompileAsync(structural.Parsed!.ToDefinition(), previousVersion: null,
            _fieldDomainScope, cancellationToken).ConfigureAwait(false);
        if (draft.JsonSchemaText is null)
            throw new DefinitionRefusalException(phase, draft.Refusals.Select(refusal => new DefinitionRefusal(refusal.Code, refusal.JsonPointer)));
        return (document, draft.JsonSchemaText);
    }

    private static (RecordTypeDocument? Parsed, IReadOnlyList<DefinitionRefusal> Refusals) Structural(
        DefinitionDocument document, DefinitionAdmissionPhase phase, DefinitionContractWindow window,
        RecordsIntentValidator validator)
    {
        RecordTypeDocument parsed;
        try { parsed = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(document.BodyJson ?? "")); }
        catch (JsonException) { return (null, [new("records.document_invalid", "")]); }
        if (RecordTypeDefinitionJson.FirstMissing(parsed) is { } missing)
            return (null, [new("records.document_invalid", missing)]);

        if (parsed.Envelope is null)
            return (null, [new("records.envelope_required", "/envelope")]);

        var refusals = new List<DefinitionRefusal>(RecordsCatalogueIdentity.CheckEnvelope(
            parsed.Envelope, parsed.Name, parsed.RecordTypeId, "record_type_id", document.Key, phase, window));
        refusals.AddRange(validator.Validate(parsed.ToDefinition(), previousVersion: null)
            .Select(refusal => new DefinitionRefusal(refusal.Code, refusal.JsonPointer)));
        // A draft may still be choosing its home and its data category; a published type has exactly one of each
        // (records-ck-17, records-ck-18, records-auth-1), with no default (DES-0046 ck-1, ADR 0025).
        if (phase != DefinitionAdmissionPhase.Author)
        {
            if (string.IsNullOrWhiteSpace(parsed.ClassId))
                refusals.Add(new("records.class.required", "/class_id"));
            if (parsed.RecordClass is null)
                refusals.Add(new("records.record_class.required", "/record_class"));
        }
        return (parsed, refusals);
    }

    // The creation path for fields: a field saved for the first time takes its kind's defaults here, before
    // admission, so the stored draft already carries them with their provenance (records-ck-38).
    private RecordTypeDocument Materialize(RecordTypeDocument document)
        => document with { Fields = _defaults.Materialize(document.ToDefinition()).Fields };

    private static DefinitionDocument Catalogue(RecordTypeDocument document, string recordTypeId, string version)
        => new(KeyOf(document.Envelope?.Tenant ?? "", recordTypeId), version, version,
            Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document)));

}

/// <summary>A provider-neutral Record Type entry; publication lifecycle belongs to the shared catalogue.</summary>
/// <param name="DefinitionId">The section-scoped <c>record_type_id</c>.</param>
/// <param name="Version">The immutable published version.</param>
/// <param name="Content">The canonical definition bytes.</param>
public sealed record RecordTypeDefinitionPackageEntry(string DefinitionId, string Version, PlatformPackageContent Content)
{
    /// <summary>Gets the definition's archive namespace.</summary>
    public DefinitionKind Kind => DefinitionKind.Records;
    /// <summary>Gets the pack content kind: <c>AssetTypeDefinition</c> in the shipped register.</summary>
    public int ContentKind => 5;
    /// <summary>Gets the Records pillar.</summary>
    public int Primitive => 0;
}

/// <summary>Projects a published Record Type without owning persistence or publication state.</summary>
public static class RecordTypeDefinitionPackageExporter
{
    /// <summary>Projects one published revision. A draft or another kind's revision is refused, never exported.</summary>
    public static RecordTypeDefinitionPackageEntry Export(DefinitionRevision published)
    {
        ArgumentNullException.ThrowIfNull(published);
        if (published.Document.Key.Kind != DefinitionKind.Records || published.Status != DefinitionStatus.Published)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish,
                [new("definition.published_version_required", "/versionId")]);
        return new(published.Document.Key.DefinitionId, published.Document.Version,
            PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(published.Document.BodyJson)));
    }
}

/// <summary>A definition the installing pack ships, identified by its catalogue kind and id.</summary>
public sealed record InstallDefinitionTarget(DefinitionKind Kind, string DefinitionId);

/// <summary>
/// One exposure the pinned dependency closure declares (records-ck-41): a producer package's definition kind and id, at the exact
/// version and <c>sha256:</c> body digest its signed pack carries, exposed at an interface version.
/// </summary>
public sealed record PinnedExposure(string PackageId, DefinitionKind Kind, string DefinitionId, string Version, string Digest, int InterfaceVersion);

/// <summary>
/// What an installing host hands the Records install gate: the definition kinds and ids the pack itself ships, and the exposures
/// declared by the exact producer versions in the pack's pinned dependency closure. It is read from the signed packs,
/// never from the node's live catalogue.
/// </summary>
public sealed record RecordsInstallClosure(IReadOnlyCollection<InstallDefinitionTarget> PackDefinitions, IReadOnlyList<PinnedExposure> Exposures);
