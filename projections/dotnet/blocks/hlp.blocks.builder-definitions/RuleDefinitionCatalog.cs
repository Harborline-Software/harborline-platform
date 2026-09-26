using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Registry;
using Harborline.Foundation.RuleEngine.Conformance;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A typed view of one shared definition revision.</summary>
public sealed record RuleDefinitionSnapshot(DefinitionRevision Revision, RuleDefinitionDocument Source);

/// <summary>The result of resolving a Rules policy against shared history.</summary>
public sealed record RuleDefinitionResolution(RuleResolutionStatus Status, RuleDefinitionSnapshot? Snapshot);

/// <summary>An authoring-time Rules resolution choice supplied when preparing released content.</summary>
public sealed record RuleReleaseSelection(DefinitionKey Key, RuleVersionPolicy Policy);

/// <summary>
/// One concrete released Rules dependency. The watermark is the winning semantic version label;
/// the binding is the immutable consumer identity. Neither retains an authoring policy.
/// </summary>
public sealed record RuleReleaseBinding(DefinitionBinding Binding, string WinningWatermark, string CanonicalSource);

/// <summary>
/// Deterministic content that a released consumer can sign. Its digest covers full canonical Rules
/// source, stream watermark and immutable binding; it is intentionally distinct from BodyJson's
/// store digest.
/// </summary>
public sealed record RulesReleaseMaterialization(
    IReadOnlyList<RuleReleaseBinding> Bindings,
    string CanonicalContent,
    string ContentDigest);

/// <summary>Composes Rules admission with the shared definition and archive stores.</summary>
public sealed class RuleDefinitionCatalog
{
    private readonly IVersionedDefinitionStore _store;
    private readonly IDefinitionLifecycleStore _lifecycle;
    private readonly RulesCapabilityCheck _capabilities;

    /// <summary>
    /// Composes shared history and lifecycle state behind the host's Access verdicts: authoring operations need
    /// <c>rules:author</c>, publication and release materialisation need <c>rules:publish</c> (DES-0018 §6).
    /// </summary>
    public RuleDefinitionCatalog(IVersionedDefinitionStore store, IDefinitionLifecycleStore lifecycle, RulesCapabilityCheck capabilities)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(capabilities);
        _store = store;
        _lifecycle = lifecycle;
        _capabilities = capabilities;
    }

    private async ValueTask RequireAsync(string permission, DefinitionAdmissionPhase stage, CancellationToken cancellationToken)
    {
        if (!await _capabilities(permission, cancellationToken).ConfigureAwait(false))
            throw new DefinitionRefusalException(stage, [new(RulesPermissions.DeniedCode, "")]);
    }

    /// <summary>Creates a new stream at revision zero using caller-supplied version identity.</summary>
    public ValueTask<DefinitionRevision> CreateJsonAsync(string json, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevision != 0) throw Refuse("definition.revision_conflict", "/expectedRevision", DefinitionAdmissionPhase.Author);
        return SaveDraftJsonAsync(json, versionId, 0, requestId, cancellationToken);
    }

    /// <summary>Loads the latest open draft, or the shared published head when no draft remains.</summary>
    public async ValueTask<RuleDefinitionSnapshot?> LoadAsync(DefinitionKey key,
        CancellationToken cancellationToken = default)
    {
        // LoadAsync's only callers in this file (ListAsync, DuplicateAsync) are authoring reads.
        RequireRules(key, DefinitionAdmissionPhase.Author, cancellationToken);
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
        var current = history.GroupBy(item => item.Document.VersionId).Select(group => group.Last())
            .Where(item => item.Status == DefinitionStatus.Draft)
            .OrderByDescending(item => item.Revision).FirstOrDefault()
            ?? await _store.GetPublishedHeadAsync(key, cancellationToken).ConfigureAwait(false);
        return current is null ? null : Decode(current);
    }

    /// <summary>Lists detached typed source in exact tenant scope, ordered by name and identity.</summary>
    public async ValueTask<IReadOnlyList<RuleDefinitionSnapshot>> ListAsync(string tenant, bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var keys = await _store.ListKeysAsync(tenant, DefinitionKind.Rules, cancellationToken).ConfigureAwait(false);
        var rows = new List<RuleDefinitionSnapshot>();
        foreach (var key in keys)
        {
            if (!includeArchived && await _lifecycle.IsArchivedAsync(new(key.Tenant, key.Kind, key.DefinitionId),
                cancellationToken).ConfigureAwait(false)) continue;
            var snapshot = await LoadAsync(key, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null) rows.Add(snapshot);
        }
        return rows.OrderBy(row => row.Source.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Source.Envelope.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Copies exact source into a new identity and history with caller-supplied version metadata.</summary>
    public async ValueTask<DefinitionRevision> DuplicateAsync(DefinitionKey key, string sourceVersionId,
        string newId, string name, string versionId, string version, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
    {
        var original = await LoadVersionAsync(key, sourceVersionId, cancellationToken).ConfigureAwait(false)
            // DuplicateAsync always goes on to create a new draft (CreateJsonAsync): Author.
            ?? throw Refuse("definition.not_found", "/sourceVersionId", DefinitionAdmissionPhase.Author);
        var source = original.Source with
        {
            Name = name,
            Envelope = original.Source.Envelope with { Id = newId, Version = version },
        };
        return await CreateJsonAsync(RuleDefinitionCodec.SerializeCanonical(source), versionId,
            expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes list visibility through the shared lifecycle without revoking published pins.</summary>
    public async ValueTask ArchiveAsync(DefinitionKey key, CancellationToken cancellationToken = default)
    {
        await RequireAsync(RulesPermissions.Author, DefinitionAdmissionPhase.Author, cancellationToken).ConfigureAwait(false);
        if (await LoadAsync(key, cancellationToken).ConfigureAwait(false) is null)
            throw Refuse("definition.not_found", "/definitionId", DefinitionAdmissionPhase.Author);
        await _lifecycle.ArchiveAsync(new(key.Tenant, key.Kind, key.DefinitionId), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Admits and saves authored Rules JSON as a shared draft.</summary>
    public async ValueTask<DefinitionRevision> SaveDraftJsonAsync(string json, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RequireAsync(RulesPermissions.Author, DefinitionAdmissionPhase.Author, cancellationToken).ConfigureAwait(false);
        var source = RequireSource(RuleIntentValidator.ValidateJson(json, RuleIntentPhase.Author));
        var envelope = source.Envelope;
        var document = new DefinitionDocument(new(envelope.Tenant, DefinitionKind.Rules, envelope.Id),
            versionId, envelope.Version, RuleDefinitionCodec.SerializeBody(source));
        return await _store.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Publishes a fenced shared revision.</summary>
    public async ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        RequireRules(key, DefinitionAdmissionPhase.Publish, cancellationToken);
        if (string.IsNullOrWhiteSpace(versionId)) throw Refuse("definition.version_id_required", "/versionId", DefinitionAdmissionPhase.Publish);
        await RequireAsync(RulesPermissions.Publish, DefinitionAdmissionPhase.Publish, cancellationToken).ConfigureAwait(false);
        // The registered Rules admission runs inside VersionedDefinitionStore.Apply:
        // replay/fence -> immutable candidate -> Publish admission -> second fence.  A
        // history pre-read here would validate a stale body and undermine that atomic seam.
        return await _store.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Decodes an exact version from shared history.</summary>
    public async ValueTask<RuleDefinitionSnapshot?> LoadVersionAsync(DefinitionKey key, string versionId,
        CancellationToken cancellationToken = default)
    {
        // LoadVersionAsync's only caller in this file (DuplicateAsync) is an authoring read.
        RequireRules(key, DefinitionAdmissionPhase.Author, cancellationToken);
        if (string.IsNullOrWhiteSpace(versionId)) throw Refuse("definition.version_id_required", "/versionId", DefinitionAdmissionPhase.Author);
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
        var revision = history.LastOrDefault(item => item.Document.VersionId == versionId);
        return revision is null ? null : Decode(revision);
    }

    /// <summary>Restores published body bytes under new shared metadata.</summary>
    public async ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
        string draftVersionId, string draftVersion, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
    {
        RequireRules(key, DefinitionAdmissionPhase.Author, cancellationToken);
        await RequireAsync(RulesPermissions.Author, DefinitionAdmissionPhase.Author, cancellationToken).ConfigureAwait(false);
        return await _store.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion,
            expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves a caller policy against shared history, independently of list visibility.</summary>
    public async ValueTask<RuleDefinitionResolution> ResolveAsync(DefinitionKey key, RuleVersionPolicy policy,
        RuleResolveScope scope, CancellationToken cancellationToken = default)
    {
        // ResolveAsync only guards its own key/policy shape here; MaterializeReleaseAsync already
        // asserted rules:publish before reaching this call, so this guard reports Author, matching
        // ResolveAsync's other (authoring-preview) callers.
        RequireRules(key, DefinitionAdmissionPhase.Author, cancellationToken);
        RequirePolicy(policy, DefinitionAdmissionPhase.Author);
        if (!Enum.IsDefined(scope)) throw Refuse(RuleDefinitionCodes.InvalidDocument, "/scope", DefinitionAdmissionPhase.Author);
        DefinitionRevision? selected;
        switch (policy.Kind)
        {
            case RuleVersionPolicyKind.Latest:
                selected = await _store.GetPublishedHeadAsync(key, cancellationToken).ConfigureAwait(false);
                break;
            case RuleVersionPolicyKind.Pinned:
                // Bind an exact admitted label to its immutable shared version identity. Prefer
                // publication if another draft has the same label; drafts cannot shadow a pin.
                var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
                var versions = history.GroupBy(item => item.Document.VersionId).Select(group => group.Last());
                selected = versions.Where(item => item.Document.Version == policy.Version)
                    .OrderByDescending(item => item.Status == DefinitionStatus.Published)
                    .ThenByDescending(item => item.Revision).FirstOrDefault();
                if (selected?.Status == DefinitionStatus.Published)
                    selected = await _store.ResolvePublishedAsync(new(key, selected.Document.VersionId), cancellationToken)
                        .ConfigureAwait(false);
                break;
            case RuleVersionPolicyKind.Draft:
                if (scope == RuleResolveScope.Production) return new(RuleResolutionStatus.DraftRefused, null);
                history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
                selected = history.GroupBy(item => item.Document.VersionId).Select(group => group.Last())
                    .Where(item => item.Status == DefinitionStatus.Draft)
                    .OrderByDescending(item => item.Revision).FirstOrDefault();
                break;
            default: throw Refuse(RuleDefinitionCodes.InvalidVersionPolicy, "/versionPolicy/kind", DefinitionAdmissionPhase.Author);
        }
        if (selected is null) return new(RuleResolutionStatus.NotFound, null);
        if (selected.Status == DefinitionStatus.Draft && scope == RuleResolveScope.Production)
            return new(RuleResolutionStatus.DraftRefused, null);
        return new(RuleResolutionStatus.Resolved, Decode(selected));
    }

    /// <summary>
    /// Converts authoring resolution choices into only concrete released bindings. Latest is
    /// resolved through the shared store at this point and cannot survive in returned content;
    /// every result is re-read through <see cref="IVersionedDefinitionStore.ResolvePublishedAsync"/>.
    /// </summary>
    public async ValueTask<RulesReleaseMaterialization> MaterializeReleaseAsync(
        IReadOnlyList<RuleReleaseSelection> selections, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        await RequireAsync(RulesPermissions.Publish, DefinitionAdmissionPhase.Publish, cancellationToken).ConfigureAwait(false);
        var materialized = new List<RuleReleaseBinding>(selections.Count);
        var seen = new HashSet<DefinitionKey>();
        for (int index = 0; index < selections.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // MaterializeReleaseAsync is entirely Publish-gated (rules:publish already asserted above).
            var selection = selections[index] ?? throw Refuse(RuleDefinitionCodes.InvalidDocument,
                "/selections/" + index + "/key", DefinitionAdmissionPhase.Publish);
            RequireRules(selection.Key, DefinitionAdmissionPhase.Publish, cancellationToken);
            if (!seen.Add(selection.Key)) throw Refuse(RuleDefinitionCodes.InvalidDocument,
                "/selections/" + index + "/key", DefinitionAdmissionPhase.Publish);
            if (selection.Policy.Kind == RuleVersionPolicyKind.Draft)
                throw Refuse("rules.release.draft_refused", "/selections/" + index + "/policy", DefinitionAdmissionPhase.Publish);

            var resolution = await ResolveAsync(selection.Key, selection.Policy, RuleResolveScope.Production,
                cancellationToken).ConfigureAwait(false);
            if (resolution.Status == RuleResolutionStatus.DraftRefused)
                throw Refuse("rules.release.draft_refused", "/selections/" + index + "/policy", DefinitionAdmissionPhase.Publish);
            if (resolution.Status != RuleResolutionStatus.Resolved || resolution.Snapshot is null)
                throw Refuse("definition.not_found", "/selections/" + index, DefinitionAdmissionPhase.Publish);

            var binding = new DefinitionBinding(selection.Key, resolution.Snapshot.Revision.Document.VersionId);
            var published = await _store.ResolvePublishedAsync(binding, cancellationToken).ConfigureAwait(false);
            if (published is null || published.Revision != resolution.Snapshot.Revision.Revision)
                throw Refuse("definition.not_found", "/selections/" + index, DefinitionAdmissionPhase.Publish);
            materialized.Add(new(binding, published.Document.Version,
                RuleDefinitionCodec.SerializeCanonical(Decode(published).Source)));
        }

        var content = new JsonObject
        {
            ["kind"] = "RulesRelease",
            ["bindings"] = new JsonArray(materialized.OrderBy(item => item.Binding.Key.Tenant, StringComparer.Ordinal)
                .ThenBy(item => item.Binding.Key.DefinitionId, StringComparer.Ordinal)
                .ThenBy(item => item.Binding.VersionId, StringComparer.Ordinal)
                .Select(item => (JsonNode?)new JsonObject
                {
                    ["tenant"] = item.Binding.Key.Tenant,
                    ["definitionId"] = item.Binding.Key.DefinitionId,
                    ["versionId"] = item.Binding.VersionId,
                    ["winningWatermark"] = item.WinningWatermark,
                    ["source"] = JsonNode.Parse(item.CanonicalSource),
                }).ToArray()),
        };
        var builder = new StringBuilder();
        CanonicalJson.Write(content, builder);
        string canonicalContent = builder.ToString();
        return new(materialized.AsReadOnly(), canonicalContent,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalContent))));
    }

    /// <summary>Validates reconstructed Rules source at the shared admission boundary.</summary>
    public static IReadOnlyList<DefinitionRefusal> Admit(DefinitionDocument document, DefinitionAdmissionPhase phase)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Key.Kind != DefinitionKind.Rules) return [new("definition.registry_unknown", "/registry")];
        if (!Enum.IsDefined(phase)) return [new(RuleDefinitionCodes.InvalidDocument, "/phase")];
        var result = ValidateSource(document, phase == DefinitionAdmissionPhase.Publish
            ? RuleIntentPhase.Publish : RuleIntentPhase.Author);
        return Refusals(result);
    }

    private static RuleIntentResult ValidateSource(DefinitionDocument document, RuleIntentPhase phase)
    {
        if (!DefinitionSemanticVersion.TryParse(document.Version, out _))
            return new(null, [new("definition.version_invalid", "/version", phase)]);
        var result = RuleIntentValidator.ValidateBodyJson(document.BodyJson, document.Key.DefinitionId,
            document.Key.Tenant, document.Version, phase);
        return result;
    }

    private static RuleDefinitionSnapshot Decode(DefinitionRevision revision)
        => new(revision, RequireSource(ValidateSource(revision.Document, RuleIntentPhase.Persisted)));

    private static RuleDefinitionDocument RequireSource(RuleIntentResult result)
        => result.IsValid ? result.Document! : throw new DefinitionRefusalException(StageOf(result), Refusals(result));

    private static DefinitionRefusal[] Refusals(RuleIntentResult result)
        => result.Diagnostics.Select(item => new DefinitionRefusal(item.Code, item.Location)).ToArray();

    private static void RequirePolicy(RuleVersionPolicy policy, DefinitionAdmissionPhase stage)
    {
        if (!Enum.IsDefined(policy.Kind)) throw Refuse(RuleDefinitionCodes.InvalidVersionPolicy, "/versionPolicy/kind", stage);
        if (policy.Kind == RuleVersionPolicyKind.Pinned && !DefinitionSemanticVersion.TryParse(policy.Version, out _))
            throw Refuse("definition.version_invalid", "/versionPolicy/version", stage);
    }

    private static void RequireRules(DefinitionKey key, DefinitionAdmissionPhase stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        if (key.Kind != DefinitionKind.Rules) throw Refuse("definition.registry_unknown", "/registry", stage);
    }

    private static DefinitionRefusalException Refuse(string code, string pointer, DefinitionAdmissionPhase stage)
        => new(stage, [new(code, pointer)]);

    private static DefinitionAdmissionPhase StageOf(RuleIntentResult result)
        => result.Diagnostics.Any(item => item.Phase == RuleIntentPhase.Publish) ? DefinitionAdmissionPhase.Publish : DefinitionAdmissionPhase.Author;
}
