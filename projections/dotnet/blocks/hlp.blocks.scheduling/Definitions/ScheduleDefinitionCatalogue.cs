using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.Scheduling.Definitions;

/// <summary>Stable refusal codes of Scheduling definition binding (DES-0022 scheduling-ck-20, scheduling-eng-1).</summary>
public static class ScheduleDefinitionCodes
{
    /// <summary>The body is not a JSON object.</summary>
    public const string BodyInvalid = "scheduling.definition.body_invalid";
    /// <summary>The body carries an integer revision, retired by DES-0022 ruling 8 (L491 reversed by L783).</summary>
    public const string IntegerRevisionRetired = "scheduling.definition.integer_revision_retired";
    /// <summary>A version the body repeats disagrees with the store's semantic version.</summary>
    public const string VersionMismatch = "scheduling.definition.version_mismatch";
    /// <summary>The immutable version id is not <c>definition-id@semantic-version</c>.</summary>
    public const string VersionIdMismatch = "scheduling.definition.version_id_mismatch";
    /// <summary>A consumer asked for a schedule without an immutable version pin.</summary>
    public const string Unpinned = "scheduling.definition.unpinned";
    /// <summary>The pinned version is absent or was never published.</summary>
    public const string NotFound = "scheduling.definition.not_found";
    /// <summary>The definition has no published version to pin.</summary>
    public const string NoPublishedHead = "scheduling.definition.no_published_head";
}

/// <summary>
/// A consumer's binding to one Scheduling definition. It records the definition id and the immutable
/// version id, never the id alone (ADR 0099 decision 7); a missing version id is an unpinned binding.
/// </summary>
public sealed record ScheduleDefinitionPin(string Tenant, string DefinitionId, string? VersionId);

/// <summary>
/// Scheduling's binding to T-620's shared versioned-definition store under
/// <see cref="DefinitionKind.Schedules"/> (DES-0022 ruling 8). Versions are semantic-version heads;
/// the store's expected revision is its per-stream write fence, not a definition version. A consumer
/// pins the published head when it binds and resolves only that exact pin afterwards.
/// </summary>
public sealed class ScheduleDefinitionCatalogue
{
    private readonly IVersionedDefinitionStore _store;

    /// <summary>Binds the catalogue to the shared store.</summary>
    public ScheduleDefinitionCatalogue(IVersionedDefinitionStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The shared store's validator for <see cref="DefinitionKind.Schedules"/> at author and publish.</summary>
    public static DefinitionAdmission Admission { get; } = (document, _) => Validate(document);

    /// <summary>The immutable version id of one semantic version of a definition.</summary>
    public static string VersionIdFor(string definitionId, string version) => definitionId + "@" + version;

    /// <summary>Appends a draft of <paramref name="version"/>. A stale fence counts as a stale commit refusal.</summary>
    public async ValueTask<DefinitionRevision> SaveDraftAsync(string tenant, string definitionId, string version,
        string bodyJson, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        var document = new DefinitionDocument(Key(tenant, definitionId), VersionIdFor(definitionId, version), version, bodyJson);
        try
        {
            return await _store.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (DefinitionRefusalException refusal) when (refusal.Refusals.Any(item => item.Code == "definition.revision_conflict"))
        {
            SchedulingTelemetry.RecordStaleCommitRefusal();
            throw;
        }
    }

    /// <summary>Publishes the draft of <paramref name="version"/> immutably.</summary>
    public ValueTask<DefinitionRevision> PublishAsync(string tenant, string definitionId, string version,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        => _store.PublishAsync(Key(tenant, definitionId), VersionIdFor(definitionId, version), expectedRevision,
            requestId, cancellationToken);

    /// <summary>Copies a published version into a new draft (scheduling-ck-21); publishing it moves the head.</summary>
    public ValueTask<DefinitionRevision> RestoreAsDraftAsync(string tenant, string definitionId, string sourceVersion,
        string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        => _store.RestoreAsDraftAsync(Key(tenant, definitionId), VersionIdFor(definitionId, sourceVersion),
            VersionIdFor(definitionId, draftVersion), draftVersion, expectedRevision, requestId, cancellationToken);

    /// <summary>Pins the published head: the highest published semantic version, never a draft.</summary>
    public async ValueTask<ScheduleDefinitionPin> PinHeadAsync(string tenant, string definitionId,
        CancellationToken cancellationToken = default)
    {
        var head = await _store.GetPublishedHeadAsync(Key(tenant, definitionId), cancellationToken).ConfigureAwait(false)
            ?? throw Refuse(ScheduleDefinitionCodes.NoPublishedHead, "/definitionId");
        return new(tenant, definitionId, head.Document.VersionId);
    }

    /// <summary>Resolves an exact published pin (scheduling-eng-1). An unpinned or unknown version refuses.</summary>
    public async ValueTask<DefinitionRevision> ResolveAsync(ScheduleDefinitionPin pin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (string.IsNullOrWhiteSpace(pin.VersionId)) throw Refuse(ScheduleDefinitionCodes.Unpinned, "/versionId");
        return await _store.ResolvePublishedAsync(new(Key(pin.Tenant, pin.DefinitionId), pin.VersionId), cancellationToken)
                .ConfigureAwait(false)
            ?? throw Refuse(ScheduleDefinitionCodes.NotFound, "/versionId");
    }

    private static IReadOnlyList<DefinitionRefusal> Validate(DefinitionDocument document)
    {
        if (!StringComparer.Ordinal.Equals(document.VersionId, VersionIdFor(document.Key.DefinitionId, document.Version)))
            return [new(ScheduleDefinitionCodes.VersionIdMismatch, "/versionId")];
        if (Parse(document.BodyJson) is not { } body) return [new(ScheduleDefinitionCodes.BodyInvalid, "")];
        var refusals = new List<DefinitionRefusal>();
        Versioning(body, "", document.Version, refusals);
        if (body["envelope"] is JsonObject envelope) Versioning(envelope, "/envelope", document.Version, refusals);
        return refusals;
    }

    // The store has already parsed the body; a duplicate member is the one shape JsonObject still refuses.
    private static JsonObject? Parse(string json)
    {
        try
        {
            var body = JsonNode.Parse(json) as JsonObject;
            _ = body?.Count;
            return body;
        }
        catch (ArgumentException) { return null; }
    }

    // L491's integer revision is refused wherever a body still carries it; a repeated semantic version must agree.
    private static void Versioning(JsonObject node, string pointer, string version, List<DefinitionRefusal> refusals)
    {
        if (node.ContainsKey("revision")) refusals.Add(new(ScheduleDefinitionCodes.IntegerRevisionRetired, pointer + "/revision"));
        if (!node.TryGetPropertyValue("version", out var value)) return;
        if (value?.GetValueKind() == JsonValueKind.Number)
            refusals.Add(new(ScheduleDefinitionCodes.IntegerRevisionRetired, pointer + "/version"));
        else if (value?.GetValueKind() != JsonValueKind.String || !StringComparer.Ordinal.Equals(value.GetValue<string>(), version))
            refusals.Add(new(ScheduleDefinitionCodes.VersionMismatch, pointer + "/version"));
    }

    private static DefinitionKey Key(string tenant, string definitionId) => new(tenant, DefinitionKind.Schedules, definitionId);

    private static DefinitionRefusalException Refuse(string code, string pointer)
        => new(DefinitionAdmissionPhase.Publish, [new DefinitionRefusal(code, pointer)]);
}
