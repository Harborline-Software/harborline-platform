using System.Text;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// Where a pinned catalogue field came from: the tenant's own authoring, or a pack or the platform package at an
/// exact pack version. The shape follows the api Forms source binding's provenance.
/// </summary>
/// <param name="Kind"><c>tenant</c>, <c>pack</c> or <c>platform</c>.</param>
/// <param name="PackKey">The contributing pack's key, required for <c>pack</c> and <c>platform</c> and absent for <c>tenant</c>.</param>
/// <param name="PackVersion">The contributing pack's exact version, with the same presence rule.</param>
public sealed record CatalogueFieldProvenance(string Kind, string? PackKey = null, string? PackVersion = null);

/// <summary>
/// One field of an exact, immutable Record Type revision (DES-0015 records-ck-40). The coordinate names the
/// Records-owned capability and its schema version, the definition kind, id and immutable version id, the
/// field key, the algorithm-qualified <c>sha256:</c> digest of the stored body, and provenance.
/// </summary>
public sealed record CatalogueFieldCoordinate(
    string CapabilityId,
    int SchemaVersion,
    DefinitionKind Kind,
    string DefinitionId,
    string VersionId,
    string FieldKey,
    string BodyDigest,
    CatalogueFieldProvenance Provenance);

/// <summary>A resolved catalogue field and the exact revision it was read from.</summary>
public sealed record CatalogueFieldResolution(FieldDefinition Field, DefinitionRevision Revision);

/// <summary>
/// The Records-owned catalogue field source for R1 (DES-0015 records-ck-40, ADR 0095 ruling 5, owner rulings
/// 2026-09-21): a read-only adapter over the shared versioned-definition store. It resolves only an exact
/// published revision, and only when the coordinate's digest equals the store's digest of that revision's
/// stored body. A digest of any other representation of the same definition is a mismatch, never an
/// equivalent (records-ck-40). It is not a value-domain source, and it has no write path.
/// </summary>
public sealed class CatalogueFieldSource
{
    /// <summary>The Records-owned capability this source serves.</summary>
    public const string CapabilityId = "records.catalogue-field-source";

    /// <summary>The coordinate schema version this source serves.</summary>
    public const int SchemaVersion = 1;

    private const string DigestPrefix = "sha256:";
    private readonly IVersionedDefinitionStore _store;

    /// <summary>Creates a source over the host's shared store.</summary>
    public CatalogueFieldSource(IVersionedDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>The coordinate's digest of a stored revision: the store's body digest, algorithm-qualified.</summary>
    public static string DigestOf(DefinitionRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return DigestPrefix + revision.Digest;
    }

    /// <summary>
    /// Resolves one field of an exact published Record Type revision in <paramref name="tenant"/>. Every refusal is
    /// a <see cref="DefinitionRefusalException"/> at the Render stage, pointing at the coordinate member at fault.
    /// </summary>
    public async ValueTask<CatalogueFieldResolution> ResolveAsync(string tenant, CatalogueFieldCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        var refusals = Structural(coordinate);
        if (refusals.Count > 0) throw Refuse(refusals);

        var revision = await _store.ResolvePublishedAsync(
            new(RecordTypeDefinitionStore.KeyOf(tenant, coordinate.DefinitionId), coordinate.VersionId),
            cancellationToken).ConfigureAwait(false)
            ?? throw Refuse([new("records.field_source.version_unavailable", "/version_id")]);
        if (!StringComparer.Ordinal.Equals(DigestOf(revision), coordinate.BodyDigest))
            throw Refuse([new("records.field_source.digest_mismatch", "/body_digest")]);

        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(revision.Document.BodyJson));
        var field = (document.Fields ?? []).FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.FieldKey, coordinate.FieldKey))
            ?? throw Refuse([new("records.field_source.field_unknown", "/field_key")]);
        return new(field, revision);
    }

    private static List<DefinitionRefusal> Structural(CatalogueFieldCoordinate coordinate)
    {
        var refusals = new List<DefinitionRefusal>();
        if (!StringComparer.Ordinal.Equals(coordinate.CapabilityId, CapabilityId))
            refusals.Add(new("records.field_source.unsupported_capability", "/capability_id"));
        if (coordinate.SchemaVersion != SchemaVersion)
            refusals.Add(new("records.field_source.unsupported_capability", "/schema_version"));
        if (coordinate.Kind != DefinitionKind.Records)
            refusals.Add(new("records.field_source.unsupported_kind", "/kind"));
        if (string.IsNullOrWhiteSpace(coordinate.DefinitionId))
            refusals.Add(new("records.field_source.coordinate_invalid", "/definition_id"));
        if (!DefinitionSemanticVersion.TryParse(coordinate.VersionId, out _))
            refusals.Add(new("records.field_source.coordinate_invalid", "/version_id"));
        if (string.IsNullOrWhiteSpace(coordinate.FieldKey))
            refusals.Add(new("records.field_source.coordinate_invalid", "/field_key"));
        if (!IsDigest(coordinate.BodyDigest))
            refusals.Add(new("records.field_source.digest_invalid", "/body_digest"));
        if (!IsProvenance(coordinate.Provenance))
            refusals.Add(new("records.field_source.provenance_invalid", "/provenance"));
        return refusals;
    }

    // sha256: then exactly 64 lowercase hex digits, the store's own digest form.
    private static bool IsDigest(string? digest)
        => digest is { Length: 71 } && digest.StartsWith(DigestPrefix, StringComparison.Ordinal)
            && digest.AsSpan(DigestPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static bool IsProvenance(CatalogueFieldProvenance? provenance) => provenance switch
    {
        { Kind: "tenant", PackKey: null, PackVersion: null } => true,
        { Kind: "pack" or "platform", PackKey: { Length: > 0 }, PackVersion: { } version }
            => DefinitionSemanticVersion.TryParse(version, out _),
        _ => false,
    };

    private static DefinitionRefusalException Refuse(IEnumerable<DefinitionRefusal> refusals)
        => new(DefinitionAdmissionPhase.Render, refusals);
}
