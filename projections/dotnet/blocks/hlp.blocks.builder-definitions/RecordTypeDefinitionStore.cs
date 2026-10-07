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
    RecordClass? RecordClass = null);

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
/// or compiled, is raised before the store or the schema registry changes.
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
            new(request.Tenant, request.Section, request.Contract),
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
    /// Compiles the stored version, immutably publishes it, then registers exactly the compiled schema. The
    /// store commits only at <paramref name="expectedRevision"/>, so a body changed after this compile read it
    /// fails the store's fence and the registered schema is always the published body's.
    /// </summary>
    public async ValueTask<RecordTypePublication> PublishAsync(string tenant, string recordTypeId, string version,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        var key = KeyOf(tenant, recordTypeId);
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
        var source = history.LastOrDefault(revision => revision.Document.VersionId == version)
            ?? throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new("definition.not_found", "/versionId")]);

        var admitted = await AdmitAsync(source.Document, DefinitionAdmissionPhase.Publish, cancellationToken).ConfigureAwait(false);
        // records-ck-18: the catalogue home must be a published Class of this tenant. The registered validator
        // cannot see other definitions, so this boundary resolves it before the store publishes.
        var classId = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(source.Document.BodyJson)).ClassId!;
        if (await _store.GetPublishedHeadAsync(ClassDefinitionStore.KeyOf(tenant, classId), cancellationToken)
                .ConfigureAwait(false) is null)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new("records.class.unresolved", "/class_id")]);
        var published = await _store.PublishAsync(key, version, expectedRevision, requestId, cancellationToken)
            .ConfigureAwait(false);
        var schema = await _registry.RegisterAsync(admitted.JsonSchemaText, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new(published, schema);
    }

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
    /// Runs Install-phase admission and the schema compile over an exported entry, as a receiving host would
    /// before it installs it. Returns every refusal; it writes nothing.
    /// </summary>
    public async ValueTask<DefinitionRefusalReport> AdmitInstallAsync(string tenant, RecordTypeDefinitionPackageEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var document = new DefinitionDocument(KeyOf(tenant, entry.DefinitionId), entry.Version, entry.Version,
            Encoding.UTF8.GetString(entry.Content.Payload.Span));
        try
        {
            await AdmitAsync(document, DefinitionAdmissionPhase.Install, cancellationToken).ConfigureAwait(false);
            return new(DefinitionAdmissionPhase.Install, []);
        }
        catch (DefinitionRefusalException refused)
        {
            return new(refused.Stage, refused.Refusals);
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
