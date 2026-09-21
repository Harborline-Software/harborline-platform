using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// Reversible typed Records encoding over the provider-neutral definition document. The document
/// owns tenant, definition, immutable version identity and semantic version; the body owns all
/// remaining Records source metadata and authored grammar.
/// </summary>
public static class RecordsDefinitionCodec
{
    private static readonly HashSet<string> DocumentOwnedEnvelopeMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "definition_id",
        "tenant_id",
        "version",
    };

    /// <summary>Encodes one typed definition without duplicating document-owned metadata in its body.</summary>
    public static DefinitionDocument Encode(RecordTypeDefinition definition, string versionId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Require(definition.Envelope.DefinitionId, "records.source.definition_id_required", "/definitionId");
        Require(definition.Envelope.TenantId, "records.source.tenant_required", "/tenant");
        Require(definition.Envelope.Version, "records.source.version_required", "/version");
        Require(versionId, "records.source.version_id_required", "/versionId");

        var body = JsonNode.Parse(RecordsDefinitionJson.SerializeCanonicalForAdmission(definition))?.AsObject()
            ?? throw Refuse("records.source.object_required", "/body");
        var envelope = body["envelope"]?.AsObject()
            ?? throw Refuse("records.source.envelope_required", "/body/envelope");
        foreach (var member in DocumentOwnedEnvelopeMembers)
        {
            envelope.Remove(member);
        }

        return new(
            new(definition.Envelope.TenantId, DefinitionKind.Records, definition.Envelope.DefinitionId),
            versionId,
            definition.Envelope.Version,
            body.ToJsonString());
    }

    /// <summary>Decodes one Records document and reconstructs typed document-owned metadata.</summary>
    public static RecordTypeDefinition Decode(DefinitionDocument document)
    {
        var json = Reconstruct(document);
        try
        {
            return RecordsDefinitionJson.Deserialize(json);
        }
        catch (JsonException)
        {
            throw Refuse("records.source.shape_invalid", "/body");
        }
    }

    private static string Reconstruct(DefinitionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Key);
        if (document.Key.Kind != DefinitionKind.Records)
            throw Refuse("records.source.registry_invalid", "/registry");
        Require(document.Key.Tenant, "records.source.tenant_required", "/tenant");
        Require(document.Key.DefinitionId, "records.source.definition_id_required", "/definitionId");
        Require(document.VersionId, "records.source.version_id_required", "/versionId");
        Require(document.Version, "records.source.version_required", "/version");

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(document.BodyJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentNullException)
        {
            throw Refuse("records.source.json_invalid", "/body");
        }

        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw Refuse("records.source.object_required", "/body");
            var duplicates = new List<DefinitionRefusal>();
            FindDuplicates(parsed.RootElement, "/body", duplicates);
            if (duplicates.Count > 0) throw new DefinitionRefusalException(duplicates);

            var envelopeProperties = parsed.RootElement.EnumerateObject()
                .Where(property => property.NameEquals("envelope"))
                .ToArray();
            if (envelopeProperties.Length != 1
                || envelopeProperties[0].Value.ValueKind != JsonValueKind.Object)
                throw Refuse("records.source.envelope_required", "/body/envelope");
            foreach (var property in envelopeProperties[0].Value.EnumerateObject())
            {
                if (DocumentOwnedEnvelopeMembers.Contains(property.Name))
                {
                    throw Refuse(
                        "records.source.authoritative_member_forbidden",
                        "/body/envelope/" + Escape(property.Name));
                }
            }
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(document.BodyJson)!.AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Refuse("records.source.shape_invalid", "/body");
        }
        var envelope = root["envelope"]!.AsObject();
        envelope["definition_id"] = document.Key.DefinitionId;
        envelope["version"] = document.Version;
        envelope["tenant_id"] = document.Key.Tenant;
        var reconstructed = root.ToJsonString();
        var shape = RecordsDefinitionJson.ValidateRawShape(reconstructed);
        if (shape.Refusals.Count > 0)
        {
            throw new DefinitionRefusalException(shape.Refusals
                .Select(refusal => new DefinitionRefusal(refusal.Code, refusal.JsonPointer))
                .ToArray());
        }
        return reconstructed;
    }

    private static void FindDuplicates(JsonElement value, string pointer, List<DefinitionRefusal> refusals)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                var child = pointer + "/" + Escape(property.Name);
                if (!seen.Add(property.Name))
                    refusals.Add(new("records.source.member_duplicate", child));
                FindDuplicates(property.Value, child, refusals);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                FindDuplicates(item, pointer + "/" + index++, refusals);
        }
    }

    private static string Escape(string value) => value
        .Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static void Require(string? value, string code, string pointer)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Refuse(code, pointer);
    }

    private static DefinitionRefusalException Refuse(string code, string pointer)
        => new([new(code, pointer)]);
}

/// <summary>
/// Pure Records admission for shared-store mutations and installation candidates. It performs no
/// schema registration or persistence.
/// </summary>
public sealed class RecordsDefinitionAdmission
{
    private readonly RecordsIntentValidator _validator;

    /// <summary>Uses the canonical Records validator composed with the host's field runtimes.</summary>
    public RecordsDefinitionAdmission(RecordsIntentValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
    }

    /// <summary>
    /// Admits the store's trusted candidate against its trusted prior same-VersionId revision.
    /// Suitable for direct registration with <see cref="InMemoryVersionedDefinitionStore.CreateAsync"/>.
    /// </summary>
    public async ValueTask<IReadOnlyList<DefinitionRefusal>> AdmitStoreAsync(
        DefinitionAdmissionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var principal = RequirePrincipal(context.PrincipalContext);
            var candidate = RecordsDefinitionCodec.Decode(context.Candidate.Document);
            var prior = context.PriorSameVersionRevision is null
                ? null
                : RecordsDefinitionCodec.Decode(context.PriorSameVersionRevision.Document);
            var result = await _validator.ValidateAsync(
                candidate,
                prior,
                new FieldDomainScope(new TenantId(context.Candidate.Document.Key.Tenant), principal),
                cancellationToken).ConfigureAwait(false);
            return Map(result.Refusals);
        }
        catch (DefinitionRefusalException exception)
        {
            return exception.Refusals;
        }
    }

    /// <summary>
    /// Pure install admission over the persisted Records source shape. Installation side effects
    /// remain the caller's responsibility.
    /// </summary>
    public async ValueTask<RecordTypeDefinition> AdmitInstallAsync(
        DefinitionDocument document,
        DefinitionPrincipalContext principalContext,
        CancellationToken cancellationToken = default)
    {
        var principal = RequirePrincipal(principalContext);
        var definition = RecordsDefinitionCodec.Decode(document);
        var result = await _validator.ValidateAsync(
            definition,
            new FieldDomainScope(new TenantId(document.Key.Tenant), principal),
            cancellationToken).ConfigureAwait(false);
        if (result.Refusals.Count > 0) throw new DefinitionRefusalException(Map(result.Refusals));
        return definition;
    }

    private static string RequirePrincipal(DefinitionPrincipalContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.Principal))
            throw new DefinitionRefusalException([new("definition.principal_required", "/principal")]);
        return context.Principal;
    }

    private static IReadOnlyList<DefinitionRefusal> Map(IReadOnlyList<RecordsRefusal> refusals)
        => Array.AsReadOnly(refusals.Select(refusal =>
            new DefinitionRefusal(refusal.Code, refusal.JsonPointer)).ToArray());
}

/// <summary>A typed Records view of one shared catalogue revision.</summary>
/// <param name="Source">The exact provider-neutral source stored by the catalogue.</param>
/// <param name="Definition">The reconstructed typed Records definition.</param>
/// <param name="Revision">The monotonically increasing definition-stream revision.</param>
/// <param name="Status">The lifecycle state recorded by this event.</param>
/// <param name="Digest">The digest of the exact stored body bytes.</param>
/// <param name="RestoredFromVersionId">The published source version when this event restored a new draft.</param>
public sealed record RecordsDefinitionRevision(
    DefinitionDocument Source,
    RecordTypeDefinition Definition,
    long Revision,
    DefinitionStatus Status,
    string Digest,
    string? RestoredFromVersionId);

/// <summary>Typed Records lifecycle over the one shared versioned-definition store.</summary>
public sealed class RecordsDefinitionLifecycle
{
    private readonly IVersionedDefinitionStore _store;
    private readonly RecordsFieldKindDefaultMaterializer _defaults;

    /// <summary>Composes typed Records behavior over the shared store and field-kind defaults.</summary>
    public RecordsDefinitionLifecycle(
        IVersionedDefinitionStore store,
        RecordsFieldKindDefaultMaterializer defaults)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(defaults);
        _store = store;
        _defaults = defaults;
    }

    /// <summary>Materializes creation defaults once, then appends the first typed draft.</summary>
    public ValueTask<RecordsDefinitionRevision> CreateDraftAsync(
        DefinitionPrincipalContext principalContext,
        RecordTypeDefinition definition,
        string versionId,
        long expectedRevision,
        string requestId,
        CancellationToken cancellationToken = default)
        => SaveAsync(principalContext, _defaults.Materialize(definition), versionId,
            expectedRevision, requestId, cancellationToken);

    /// <summary>Appends exact authored data without reapplying creation defaults.</summary>
    public ValueTask<RecordsDefinitionRevision> SaveDraftAsync(
        DefinitionPrincipalContext principalContext,
        RecordTypeDefinition definition,
        string versionId,
        long expectedRevision,
        string requestId,
        CancellationToken cancellationToken = default)
        => SaveAsync(principalContext, definition, versionId,
            expectedRevision, requestId, cancellationToken);

    /// <summary>Publishes an existing admitted draft through the shared store.</summary>
    public async ValueTask<RecordsDefinitionRevision> PublishAsync(
        DefinitionPrincipalContext principalContext,
        string tenant,
        string definitionId,
        string versionId,
        long expectedRevision,
        string requestId,
        CancellationToken cancellationToken = default)
        => Project(await _store.PublishAsync(
            principalContext,
            Key(tenant, definitionId),
            versionId,
            expectedRevision,
            requestId,
            cancellationToken).ConfigureAwait(false));

    /// <summary>Restores a published body unchanged into a newly identified draft.</summary>
    public async ValueTask<RecordsDefinitionRevision> RestoreAsDraftAsync(
        DefinitionPrincipalContext principalContext,
        string tenant,
        string definitionId,
        string sourceVersionId,
        string draftVersionId,
        string draftVersion,
        long expectedRevision,
        string requestId,
        CancellationToken cancellationToken = default)
        => Project(await _store.RestoreAsDraftAsync(
            principalContext,
            Key(tenant, definitionId),
            sourceVersionId,
            draftVersionId,
            draftVersion,
            expectedRevision,
            requestId,
            cancellationToken).ConfigureAwait(false));

    /// <summary>Returns typed append-only lifecycle history.</summary>
    public async ValueTask<IReadOnlyList<RecordsDefinitionRevision>> ListHistoryAsync(
        string tenant,
        string definitionId,
        CancellationToken cancellationToken = default)
        => Array.AsReadOnly((await _store.ListHistoryAsync(
            Key(tenant, definitionId), cancellationToken).ConfigureAwait(false))
            .Select(Project)
            .ToArray());

    /// <summary>Returns the highest published semantic-version head, excluding drafts.</summary>
    public async ValueTask<RecordsDefinitionRevision?> GetPublishedHeadAsync(
        string tenant,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        var revision = await _store.GetPublishedHeadAsync(
            Key(tenant, definitionId), cancellationToken).ConfigureAwait(false);
        return revision is null ? null : Project(revision);
    }

    /// <summary>Resolves an exact immutable published Records pin; drafts and unknown pins return null.</summary>
    public async ValueTask<RecordsDefinitionRevision?> ResolvePublishedAsync(
        string tenant,
        string definitionId,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var revision = await _store.ResolvePublishedAsync(
            new(Key(tenant, definitionId), versionId), cancellationToken).ConfigureAwait(false);
        return revision is null ? null : Project(revision);
    }

    private async ValueTask<RecordsDefinitionRevision> SaveAsync(
        DefinitionPrincipalContext principalContext,
        RecordTypeDefinition definition,
        string versionId,
        long expectedRevision,
        string requestId,
        CancellationToken cancellationToken)
        => Project(await _store.SaveDraftAsync(
            principalContext,
            RecordsDefinitionCodec.Encode(definition, versionId),
            expectedRevision,
            requestId,
            cancellationToken).ConfigureAwait(false));

    private static DefinitionKey Key(string tenant, string definitionId)
        => new(tenant, DefinitionKind.Records, definitionId);

    private static RecordsDefinitionRevision Project(DefinitionRevision revision)
        => new(
            revision.Document,
            RecordsDefinitionCodec.Decode(revision.Document),
            revision.Revision,
            revision.Status,
            revision.Digest,
            revision.RestoredFromVersionId);
}

/// <summary>A successfully resolved and runtime-bound published Records definition.</summary>
/// <param name="Pin">The exact immutable catalogue pin that resolved.</param>
/// <param name="Definition">The reconstructed typed Records definition.</param>
/// <param name="FieldKinds">Exact field-kind revisions bound for every field in authored order.</param>
/// <param name="Policies">The four authored type policies returned to downstream consumers.</param>
/// <param name="Schema">The content-addressed registered runtime schema.</param>
/// <param name="SourceDigest">The digest of the exact published Records source body.</param>
public sealed record RecordsPublishedDefinitionBinding(
    DefinitionBinding Pin,
    RecordTypeDefinition Definition,
    IReadOnlyList<ICompiledFieldKind> FieldKinds,
    RecordTypePolicies? Policies,
    Schema Schema,
    string SourceDigest);

/// <summary>
/// Resolves immutable published catalogue state and only then establishes schema-runtime binding.
/// Catalogue publication and runtime binding are deliberately separate operations.
/// </summary>
public sealed class RecordsPublishedDefinitionBinder
{
    private readonly IVersionedDefinitionStore _store;
    private readonly RecordsDefinitionCompiler _compiler;

    /// <summary>Composes the shared store with the canonical compiler-owned admitted result.</summary>
    public RecordsPublishedDefinitionBinder(
        IVersionedDefinitionStore store,
        RecordsDefinitionCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        _store = store;
        _compiler = compiler;
    }

    /// <summary>Resolves and binds the highest published semantic-version head.</summary>
    public async ValueTask<RecordsPublishedDefinitionBinding> BindPublishedHeadAsync(
        DefinitionPrincipalContext principalContext,
        string tenant,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        var revision = await _store.GetPublishedHeadAsync(
            new(tenant, DefinitionKind.Records, definitionId), cancellationToken).ConfigureAwait(false);
        if (revision is null) throw PublishedRequired("/definitionId");
        return await BindAsync(principalContext, revision, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves and binds one exact immutable published version pin.</summary>
    public async ValueTask<RecordsPublishedDefinitionBinding> BindPublishedAsync(
        DefinitionPrincipalContext principalContext,
        string tenant,
        string definitionId,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var pin = new DefinitionBinding(new(tenant, DefinitionKind.Records, definitionId), versionId);
        var revision = await _store.ResolvePublishedAsync(pin, cancellationToken).ConfigureAwait(false);
        if (revision is null) throw PublishedRequired("/versionId");
        return await BindAsync(principalContext, revision, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RecordsPublishedDefinitionBinding> BindAsync(
        DefinitionPrincipalContext principalContext,
        DefinitionRevision revision,
        CancellationToken cancellationToken)
    {
        if (principalContext is null || string.IsNullOrWhiteSpace(principalContext.Principal))
            throw new DefinitionRefusalException([new("definition.principal_required", "/principal")]);
        var definition = RecordsDefinitionCodec.Decode(revision.Document);
        var compilation = await _compiler.CompileAndRegisterResultAsync(
            definition,
            new FieldDomainScope(
                new TenantId(revision.Document.Key.Tenant),
                principalContext.Principal),
            cancellationToken).ConfigureAwait(false);
        return new(
            new(revision.Document.Key, revision.Document.VersionId),
            compilation.Definition,
            compilation.FieldKinds,
            compilation.Policies,
            compilation.Schema,
            revision.Digest);
    }

    private static DefinitionRefusalException PublishedRequired(string pointer)
        => new([new("records.binding.published_required", pointer)]);
}
