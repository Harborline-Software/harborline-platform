using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.Taxonomy;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// Composes Taxonomy admission (DES-0024) with the shared versioned-definition store under
/// <see cref="DefinitionKind.Taxonomy"/> (T-493 S7). The catalogue key is the tenant and the dotted
/// three-part <see cref="TaxonomyDefinitionId"/>; the immutable version id is the definition's own
/// three-part version, so a pinned <see cref="TaxonomyDefinitionCoordinates"/> is the consumer pin.
/// Resolution is pinned-only: a reference without an exact version never resolves to a head.
/// </summary>
public sealed class TaxonomyDefinitionStore
{
    private readonly IVersionedDefinitionStore _store;

    /// <summary>
    /// Wraps a host's shared store. The host registers <see cref="Admission"/> for
    /// <see cref="DefinitionKind.Taxonomy"/> when it builds that store; this type adds no second catalogue.
    /// </summary>
    public TaxonomyDefinitionStore(IVersionedDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// The store validator for <see cref="DefinitionKind.Taxonomy"/>. Author runs Author-phase
    /// structural admission; Publish runs Publish-phase admission and requires the body to state the
    /// catalogue's tenant, definition id and version (taxonomy-auth-11).
    /// </summary>
    /// <param name="window">The host's application-contract window from the platform seed (T-572).</param>
    public static DefinitionAdmission Admission(DefinitionContractWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (document, phase) =>
        {
            TaxonomyCatalogueCoordinates? catalogue = null;
            if (phase == DefinitionAdmissionPhase.Publish)
            {
                try { catalogue = new(document.Key.Tenant, TaxonomyDefinitionId.Parse(document.Key.DefinitionId), document.Version); }
                catch (FormatException) { return [new("definition.catalogue_mismatch", "/definition_id")]; }
            }
            return TaxonomyDefinitionAdmission
                .AdmitJson(document.BodyJson,
                    phase == DefinitionAdmissionPhase.Publish ? TaxonomyAdmissionPhase.Publish : TaxonomyAdmissionPhase.Author,
                    window, catalogue)
                .Select(refusal => new DefinitionRefusal(refusal.Code, refusal.Pointer))
                .ToArray();
        };
    }

    /// <summary>The shared-store key for one tenant's taxonomy.</summary>
    public static DefinitionKey KeyOf(string tenant, TaxonomyDefinitionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new(tenant, DefinitionKind.Taxonomy, id.ToString());
    }

    /// <summary>Saves the canonical body as a draft at its own version; a published version refuses <c>definition.version_immutable</c>.</summary>
    public ValueTask<DefinitionRevision> SaveDraftAsync(TaxonomyDefinition definition, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return _store.SaveDraftAsync(
            new(KeyOf(definition.Tenant, definition.DefinitionId), definition.Version, definition.Version,
                Encoding.UTF8.GetString(TaxonomyDefinitionJson.SerializeCanonical(definition))),
            expectedRevision, requestId, cancellationToken);
    }

    /// <summary>Re-admits and immutably publishes the draft at the pinned coordinates.</summary>
    public ValueTask<DefinitionRevision> PublishAsync(TaxonomyDefinitionCoordinates coordinates, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        return _store.PublishAsync(KeyOf(coordinates.Tenant, coordinates.DefinitionId), coordinates.Version,
            expectedRevision, requestId, cancellationToken);
    }

    /// <summary>
    /// Resolves a published definition at exactly the pinned version (taxonomy-eng-1). An absent,
    /// draft or other-tenant version is a <see cref="TaxonomyDefinitionNotFound"/>; a reference that
    /// names no exact version refuses <c>definition.reference_unpinned</c> at <c>/version</c>.
    /// </summary>
    public ValueTask<TaxonomyDefinitionResolution> ResolveAsync(TaxonomyDefinitionCoordinates coordinates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (!DefinitionSemanticVersion.TryParse(coordinates.Version, out _))
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new("definition.reference_unpinned", "/version")]);
        return ResolvePinnedAsync(coordinates, cancellationToken);
    }

    private async ValueTask<TaxonomyDefinitionResolution> ResolvePinnedAsync(TaxonomyDefinitionCoordinates coordinates,
        CancellationToken cancellationToken)
    {
        var revision = await _store.ResolvePublishedAsync(
            new(KeyOf(coordinates.Tenant, coordinates.DefinitionId), coordinates.Version), cancellationToken).ConfigureAwait(false);
        if (revision is null) return new TaxonomyDefinitionNotFound(coordinates);
        // Publish admission already bound the body to these coordinates; a body that no longer
        // parses or disagrees is store corruption, surfaced rather than served.
        var definition = TaxonomyDefinitionJson.Deserialize(revision.Document.BodyJson);
        if (new TaxonomyDefinitionCoordinates(definition.Tenant, definition.DefinitionId, definition.Version) != coordinates)
            throw new JsonException("The published taxonomy body disagrees with its catalogue coordinates.");
        return new ResolvedTaxonomyDefinition(definition);
    }
}
