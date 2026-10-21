using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// One authored Class: the catalogue home a Record Type names exactly once (DES-0004 §2; DES-0015 records-ck-18,
/// records-auth-2). A Class is data, never a tag or a sealed list, and a reference may target one.
/// </summary>
/// <param name="Envelope">The provider-neutral envelope.</param>
/// <param name="Name">The author-facing name; duplicates across sections are legal.</param>
/// <param name="ClassId">The section-scoped identity, immutable across every version of the definition.</param>
public sealed record ClassDocument(RecordsDefinitionEnvelope Envelope, string Name, string ClassId);

/// <summary>A new Class as its author describes it. The caller never supplies its id.</summary>
public sealed record NewClass(string Tenant, string Section, string Name, string Version, DefinitionContractVersion? Contract,
    string? PackageId = null, IReadOnlyList<RecordsRequirement>? Requires = null, RecordsExposure? Exposes = null);

/// <summary>
/// Composes Class admission with the shared versioned-definition store under <see cref="DefinitionKind.Classes"/>
/// (T-615). Ids are minted and scoped exactly as Record Type ids are, through the same authoring boundary rules.
/// </summary>
public sealed class ClassDefinitionStore
{
    private readonly IVersionedDefinitionStore _store;

    /// <summary>Wraps a host's shared store; the host registers <see cref="Admission"/> for <see cref="DefinitionKind.Classes"/>.</summary>
    public ClassDefinitionStore(IVersionedDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>The store validator for <see cref="DefinitionKind.Classes"/>: the shared Records identity rules.</summary>
    public static DefinitionAdmission Admission(DefinitionContractWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (document, phase) =>
        {
            ClassDocument parsed;
            try { parsed = RecordsJson.Deserialize<ClassDocument>(Encoding.UTF8.GetBytes(document.BodyJson ?? "")); }
            catch (JsonException) { return [new("records.document_invalid", "")]; }
            var refusals = new List<DefinitionRefusal>(RecordsCatalogueIdentity.CheckEnvelope(
                parsed.Envelope, parsed.Name, parsed.ClassId, "class_id", document.Key, phase, window));
            if (parsed.Envelope is not null && string.IsNullOrWhiteSpace(parsed.ClassId))
                refusals.Add(new("records.identity.class_id_required", "/class_id"));
            return refusals;
        };
    }

    /// <summary>The shared-store key for one tenant's Class.</summary>
    public static DefinitionKey KeyOf(string tenant, string classId) => new(tenant, DefinitionKind.Classes, classId);

    /// <summary>Creates a Class, minting <c>section.name-slug</c> and refusing a collision loudly.</summary>
    public ValueTask<DefinitionRevision> CreateDraftAsync(NewClass request, string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var classId = RecordsCatalogueIdentity.Mint(request.Section, request.Name);
        return RecordsCatalogueIdentity.CreateAsync(_store,
            Catalogue(new(new(request.Tenant, request.Section, request.Contract, request.PackageId, request.Requires, request.Exposes),
                request.Name, classId), classId, request.Version),
            requestId, "class_id", cancellationToken);
    }

    /// <summary>Saves a draft of a minted Class; an id with no catalogue stream refuses <c>records.identity.class_id_unminted</c>.</summary>
    public async ValueTask<DefinitionRevision> SaveDraftAsync(string classId, ClassDocument document, string version,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var catalogue = Catalogue(document, classId, version);
        await RecordsCatalogueIdentity.RequireMintedAsync(_store, catalogue.Key, "class_id", cancellationToken).ConfigureAwait(false);
        return await _store.SaveDraftAsync(catalogue, expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-admits and immutably publishes the Class draft at <paramref name="version"/>.</summary>
    public ValueTask<DefinitionRevision> PublishAsync(string tenant, string classId, string version, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
        => _store.PublishAsync(KeyOf(tenant, classId), version, expectedRevision, requestId, cancellationToken);

    private static DefinitionDocument Catalogue(ClassDocument document, string classId, string version)
        => new(KeyOf(document.Envelope?.Tenant ?? "", classId), version, version,
            Encoding.UTF8.GetString(RecordsJson.SerializeCanonical(document)));
}
