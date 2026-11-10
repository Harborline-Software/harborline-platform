using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Reports.Definitions;

/// <summary>A typed report and its shared-store lifecycle revision.</summary>
public sealed record ReportDefinitionRevision(DefinitionRevision Revision, ReportDefinition Definition);

/// <summary>A portable report entry. No authority travels in the content.</summary>
public sealed record ReportDefinitionPackEntry(int ContentKind, string DefinitionId, string Version, string BodyJson);

/// <summary>Reports' adapter to the shared catalogue, using the document version as the sole stored version.</summary>
public sealed class ReportDefinitionCatalogue(IVersionedDefinitionStore store)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    /// <summary>The registry-owned report transport kind.</summary>
    public static int ContentKind => PackContentKindRegistry.Kind("ReportDefinition").Value;

    /// <summary>Creates mandatory write-path admission for the shared store's Reports registration.</summary>
    public static DefinitionAdmission Admission(DefinitionContractWindow window, NamedMeasureCatalogue catalogue)
        => (document, phase) =>
        {
            try
            {
                var definition = Decode(document);
                if (document.BodyJson != Document(definition).BodyJson)
                    throw new JsonException("Stored reports must use the typed codec representation.");
                return ReportDefinitionAdmission.AdmitAsync(definition, phase, window, catalogue).AsTask()
                    .GetAwaiter().GetResult().Refusals.Select(item => new DefinitionRefusal(item.Code, item.Pointer)).ToArray();
            }
            catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
            {
                return [new("report.definition.body_invalid", "/body")];
            }
        };

    /// <summary>Appends an admitted report draft; the shared store fences writes and freezes publications.</summary>
    public async ValueTask<ReportDefinitionRevision> SaveDraftAsync(ReportDefinition definition, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
        => Typed(await store.SaveDraftAsync(Document(definition), expectedRevision, requestId, cancellationToken).ConfigureAwait(false));

    /// <summary>Re-admits and publishes a draft immutably.</summary>
    public async ValueTask<ReportDefinitionRevision> PublishAsync(string tenant, string id, string version, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
        => Typed(await store.PublishAsync(Key(tenant, id), VersionId(id, version), expectedRevision, requestId, cancellationToken).ConfigureAwait(false));

    /// <summary>Restores a publication into a new draft without changing any published revision.</summary>
    public async ValueTask<ReportDefinitionRevision> RestoreAsDraftAsync(string tenant, string id, string sourceVersion,
        string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        => Typed(await store.RestoreAsDraftAsync(Key(tenant, id), VersionId(id, sourceVersion), VersionId(id, draftVersion),
            draftVersion, expectedRevision, requestId, cancellationToken).ConfigureAwait(false));

    /// <summary>Resolves the highest published semantic version, or refuses by name.</summary>
    public async ValueTask<ReportDefinitionRevision> ResolveHeadAsync(string tenant, string id, CancellationToken cancellationToken = default)
        => Typed(await store.GetPublishedHeadAsync(Key(tenant, id), cancellationToken).ConfigureAwait(false)
            ?? throw Refuse("report.definition.no_published_head", "/definitionId"));

    /// <summary>Exports only a published report with its full envelope.</summary>
    public static ReportDefinitionPackEntry Export(ReportDefinitionRevision revision)
    {
        if (revision.Revision.Status != DefinitionStatus.Published) throw Refuse("report.definition.published_version_required", "/versionId");
        var definition = Decode(revision.Revision.Document);
        return new(ContentKind, definition.Key, definition.Version, JsonSerializer.Serialize(definition, Json));
    }

    /// <summary>Installs pack content through the admitted draft write path, never as an advisory check.</summary>
    public async ValueTask<ReportDefinitionRevision> ImportDraftAsync(ReportDefinitionPackEntry entry, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        if (entry.ContentKind != ContentKind) throw Refuse("report.definition.content_kind_unsupported", "/contentKind");
        var definition = JsonSerializer.Deserialize<ReportDefinition>(entry.BodyJson, Json)
            ?? throw Refuse("report.definition.body_invalid", "/body");
        if (definition.Key != entry.DefinitionId || definition.Version != entry.Version)
            throw Refuse("definition.envelope_mismatch", "/envelope");
        return await SaveDraftAsync(definition, expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
    }

    private static DefinitionDocument Document(ReportDefinition definition)
    {
        var body = JsonSerializer.SerializeToNode(definition, Json)!.AsObject();
        body.Remove("version");
        // Preserve mismatches for admission. A valid envelope version is supplied by the document on read/restore.
        if (definition.Envelope?.Version == definition.Version) body["envelope"]!.AsObject().Remove("version");
        return new(Key(definition.Tenant, definition.Key), VersionId(definition.Key, definition.Version), definition.Version, body.ToJsonString(Json));
    }

    private static ReportDefinition Decode(DefinitionDocument document)
    {
        if (document.Key.Kind != DefinitionKind.Reports || document.VersionId != VersionId(document.Key.DefinitionId, document.Version))
            throw new JsonException("Report catalogue coordinates are invalid.");
        var body = JsonNode.Parse(document.BodyJson)!.AsObject();
        RequireVersionless(body);
        body["version"] = document.Version;
        if (body["envelope"] is JsonObject envelope)
        {
            RequireVersionless(envelope);
            envelope["version"] = document.Version;
        }
        var definition = body.Deserialize<ReportDefinition>(Json) ?? throw new JsonException("Report body required.");
        if (definition.Tenant != document.Key.Tenant || definition.Key != document.Key.DefinitionId || definition.Measures is null
            || definition.Measures.Any(measure => measure is null)
            || definition.Parameters?.Any(parameter => parameter is null) == true)
            throw new JsonException("Report body coordinates are invalid.");
        return definition;
    }

    private static void RequireVersionless(JsonObject body)
    {
        // Raw shared-store callers must obey the same representation as typed saves: restore copies the body unchanged.
        if (body.ContainsKey("version"))
            throw new JsonException("Stored report bodies must omit versions.");
    }

    private static ReportDefinitionRevision Typed(DefinitionRevision revision) => new(revision, Decode(revision.Document));
    private static DefinitionKey Key(string tenant, string id) => new(tenant, DefinitionKind.Reports, id);
    private static string VersionId(string id, string version) => id + "@" + version;
    private static DefinitionRefusalException Refuse(string code, string pointer)
        => new(DefinitionAdmissionPhase.Publish, [new(code, pointer)]);
}
