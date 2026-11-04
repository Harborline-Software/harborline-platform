using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

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
    /// <summary>The definition has no name.</summary>
    public const string NameRequired = "scheduling.definition.name_required";
    /// <summary>The definition does not name what it starts.</summary>
    public const string StartsRequired = "scheduling.definition.starts_required";
    /// <summary>A definition that is not an event-anchored chain has no validity window.</summary>
    public const string WindowRequired = "scheduling.definition.window_required";
    /// <summary>The window's bounds are unreadable, or do not open before they close.</summary>
    public const string WindowInvalid = "scheduling.definition.window_invalid";
    /// <summary>A generator is missing a member or breaks its own rules.</summary>
    public const string BasisInvalid = "scheduling.definition.basis_invalid";
    /// <summary>A duration is not a non-negative ISO 8601 duration.</summary>
    public const string DurationInvalid = "scheduling.definition.duration_invalid";
    /// <summary>The escalation does not name who it goes to.</summary>
    public const string EscalationInvalid = "scheduling.definition.escalation_invalid";
    /// <summary>Modules are named with no problem class to compile into.</summary>
    public const string ModulesWithoutProblemClass = "scheduling.definition.modules_without_problem_class";
    /// <summary>A module has no name.</summary>
    public const string ModuleInvalid = "scheduling.definition.module_invalid";
    /// <summary>The legality citation is blank.</summary>
    public const string LegalityRefInvalid = "scheduling.definition.legality_ref_invalid";
    /// <summary>A definition past authoring carries no envelope.</summary>
    public const string EnvelopeRequired = "definition.envelope_required";
    /// <summary>The envelope's coordinates disagree with the document's.</summary>
    public const string EnvelopeMismatch = "definition.envelope_mismatch";
    /// <summary>An envelope member is missing or malformed.</summary>
    public const string EnvelopeInvalid = "scheduling.definition.envelope_invalid";
}

/// <summary>
/// A consumer's binding to one Scheduling definition. It records the definition id and the immutable
/// version id, never the id alone (ADR 0099 decision 7); a missing version id is an unpinned binding.
/// </summary>
public sealed record ScheduleDefinitionPin(string Tenant, string DefinitionId, string? VersionId);

/// <summary>A stored revision with its typed definition, read in place of the opaque body.</summary>
/// <param name="Revision">The shared store's revision.</param>
/// <param name="Definition">The typed definition, its envelope version filled in from the revision's document.</param>
public sealed record ScheduleDefinitionRevision(DefinitionRevision Revision, ScheduleDefinition Definition);

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
    public static DefinitionAdmission Admission { get; } = Validate;

    /// <summary>The transport content kind (scheduling-ck-1), as the one registry records it.</summary>
    public static PackContentKindEntry ContentKind => PackContentKindRegistry.Kind("ScheduleDefinition");

    /// <summary>The immutable version id of one semantic version of a definition.</summary>
    public static string VersionIdFor(string definitionId, string version) => definitionId + "@" + version;

    /// <summary>Appends a draft of <paramref name="version"/>. A stale fence counts as a stale commit refusal.</summary>
    public async ValueTask<ScheduleDefinitionRevision> SaveDraftAsync(string tenant, string definitionId, string version,
        ScheduleDefinition definition, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // A version that disagrees stays in the body, so admission refuses it by name.
        var stored = definition.Envelope is { } envelope && envelope.Version == version
            ? definition with { Envelope = envelope with { Version = null } }
            : definition;
        var document = new DefinitionDocument(Key(tenant, definitionId), VersionIdFor(definitionId, version), version,
            ScheduleDefinitionJson.Serialize(stored));
        try
        {
            return Typed(await _store.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken).ConfigureAwait(false));
        }
        catch (DefinitionRefusalException refusal) when (refusal.Refusals.Any(item => item.Code == "definition.revision_conflict"))
        {
            SchedulingTelemetry.RecordStaleCommitRefusal();
            throw;
        }
    }

    /// <summary>Publishes the draft of <paramref name="version"/> immutably.</summary>
    public async ValueTask<ScheduleDefinitionRevision> PublishAsync(string tenant, string definitionId, string version,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        => Typed(await _store.PublishAsync(Key(tenant, definitionId), VersionIdFor(definitionId, version), expectedRevision,
            requestId, cancellationToken).ConfigureAwait(false));

    /// <summary>Copies a published version into a new draft (scheduling-ck-21); publishing it moves the head.</summary>
    public async ValueTask<ScheduleDefinitionRevision> RestoreAsDraftAsync(string tenant, string definitionId, string sourceVersion,
        string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        => Typed(await _store.RestoreAsDraftAsync(Key(tenant, definitionId), VersionIdFor(definitionId, sourceVersion),
            VersionIdFor(definitionId, draftVersion), draftVersion, expectedRevision, requestId, cancellationToken).ConfigureAwait(false));

    /// <summary>Pins the published head: the highest published semantic version, never a draft.</summary>
    public async ValueTask<ScheduleDefinitionPin> PinHeadAsync(string tenant, string definitionId,
        CancellationToken cancellationToken = default)
    {
        var head = await _store.GetPublishedHeadAsync(Key(tenant, definitionId), cancellationToken).ConfigureAwait(false)
            ?? throw Refuse(ScheduleDefinitionCodes.NoPublishedHead, "/definitionId");
        return new(tenant, definitionId, head.Document.VersionId);
    }

    /// <summary>Resolves an exact published pin (scheduling-eng-1). An unpinned or unknown version refuses.</summary>
    public async ValueTask<ScheduleDefinitionRevision> ResolveAsync(ScheduleDefinitionPin pin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (string.IsNullOrWhiteSpace(pin.VersionId)) throw Refuse(ScheduleDefinitionCodes.Unpinned, "/versionId");
        return Typed(await _store.ResolvePublishedAsync(new(Key(pin.Tenant, pin.DefinitionId), pin.VersionId), cancellationToken)
                .ConfigureAwait(false)
            ?? throw Refuse(ScheduleDefinitionCodes.NotFound, "/versionId"));
    }

    private static ScheduleDefinitionRevision Typed(DefinitionRevision revision)
    {
        var definition = ScheduleDefinitionJson.Deserialize(revision.Document.BodyJson);
        return new(revision, definition.Envelope is { } envelope
            ? definition with { Envelope = envelope with { Version = revision.Document.Version } }
            : definition);
    }

    private static IReadOnlyList<DefinitionRefusal> Validate(DefinitionDocument document, DefinitionAdmissionPhase phase)
    {
        if (!StringComparer.Ordinal.Equals(document.VersionId, VersionIdFor(document.Key.DefinitionId, document.Version)))
            return [new(ScheduleDefinitionCodes.VersionIdMismatch, "/versionId")];
        if (Parse(document.BodyJson) is not { } body) return [new(ScheduleDefinitionCodes.BodyInvalid, "")];
        var refusals = new List<DefinitionRefusal>();
        Versioning(body, "", document.Version, refusals);
        if (body["envelope"] is JsonObject envelope) Versioning(envelope, "/envelope", document.Version, refusals);
        if (refusals.Count > 0) return refusals;
        ScheduleDefinition definition;
        try
        {
            definition = ScheduleDefinitionJson.Deserialize(document.BodyJson);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return [new(ScheduleDefinitionCodes.BodyInvalid, "")];
        }
        return ScheduleDefinitionAdmission.Validate(definition, document, phase, PlatformPackageSeed.ContractWindow);
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
