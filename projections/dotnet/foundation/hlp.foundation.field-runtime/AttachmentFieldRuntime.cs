using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ContentStore;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>Resolves ADR-0142 write lists through the existing store, in the enclosing Records transaction.</summary>
public sealed class AttachmentFieldRuntime(InMemoryContentStore store)
{
    /// <summary>The host's store limit used by the same field-kind binder.</summary>
    public long StoreLimitBytes => store.ItemLimitBytes;

    /// <summary>Uploads through the store's one streamed put, applying the bound field's narrowing.</summary>
    public ValueTask<Guid> PutAsync(ICompiledAttachmentFieldKind field, TenantId tenant, string declaration,
        Stream bytes, string? mediaType, string? sha256, ContentStoreWrite evidence, CancellationToken cancellationToken = default)
        => store.PutAsync(tenant, bytes, mediaType, sha256, evidence,
            new(declaration, field.DeclaredLimitBytes, field.AcceptedMediaTypes.ToHashSet(StringComparer.Ordinal)), cancellationToken);

    /// <summary>
    /// Preserves kept references and order, promotes stages once, and returns references only. The host supplies the
    /// content session belonging to the enclosing record transaction and the field's references from its prior committed
    /// version (undefined on create); this method never opens or commits another transaction.
    /// </summary>
    public async ValueTask<JsonArray> ResolveWriteAsync(ICompiledAttachmentFieldKind field, JsonElement entries,
        TenantId tenant, ContentOwner owner, ContentStoreWrite evidence, JsonElement priorReferences, CancellationToken cancellationToken = default)
    {
        if (evidence.Transaction is null) throw new ArgumentException("Attachment resolution requires the Records transaction.", nameof(evidence));
        if (entries.ValueKind != JsonValueKind.Array) throw Invalid();
        await LockEntriesAsync([entries], tenant, evidence, cancellationToken).ConfigureAwait(false);
        IEnumerable<ContentReference> prior = priorReferences.ValueKind == JsonValueKind.Array
            ? priorReferences.EnumerateArray().Select(FromJson) : [];
        var priorByIdentity = prior.ToLookup(value => (value.ContentId, value.Length, value.Owner));
        var resolved = new JsonArray();
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryPointer = FieldPointer(owner.Element) + "/" + (index++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            JsonElement stage = default, display = default;
            Guid stageId = Guid.Empty;
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("stage_id", out stage))
            {
                if (entry.EnumerateObject().Count() != 2 || stage.ValueKind != JsonValueKind.String || !stage.TryGetGuid(out stageId)
                    || !entry.TryGetProperty("display_name", out display) || display.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(display.GetString())) throw Invalid();
                var reference = await store.CommitReferenceAsync(tenant, stageId,
                    new(owner.Record, owner.Element + "/" + stageId.ToString("N")), display.GetString()!, evidence with
                    {
                        Operation = new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), evidence.Operation.Fingerprint),
                        Audit = evidence.Audit with { AuditId = Guid.NewGuid().ToString("N") },
                    }, cancellationToken).ConfigureAwait(false);
                var value = ToJson(reference);
                ValidateReference(field, JsonSerializer.SerializeToElement(value), owner.Element);
                resolved.Add(value);
            }
            else
            {
                var reference = FromJson(entry);
                var matches = priorByIdentity[(reference.ContentId, reference.Length, reference.Owner)];
                if (!matches.Any()) throw MissingKept(entryPointer);
                if (!matches.Any(value => value.MediaType == reference.MediaType))
                    throw new ContentRefusedException(ContentRefusals.MediaTypeMismatch, "The kept reference's media type changed.", owner.Element) { JsonPointer = entryPointer };
                ValidateReference(field, entry, owner.Element);
                var item = await evidence.Transaction.FindItemForUpdateAsync(new(tenant, reference.ContentId), cancellationToken).ConfigureAwait(false);
                // The final separator precedes the opaque slot; field keys themselves may contain separators.
                var fieldEnd = reference.Owner.Element.LastIndexOf('/');
                if (reference.Owner.Record != owner.Record || fieldEnd < 0 || reference.Owner.Element[..fieldEnd] != owner.Element
                    || item is null || !item.Owners.Contains(reference.Owner) || item.Length != reference.Length)
                    throw MissingKept(entryPointer);
                resolved.Add(JsonNode.Parse(entry.GetRawText()));
            }
        }
        return resolved;
    }

    /// <summary>
    /// Locks, in the enclosing transaction, every row the given write lists will promote or keep, in one canonical order:
    /// staged rows by stage id, then committed rows by content id, both ordinal. That matches the store's own order (a
    /// promotion and a rewrap lock staged before committed), so two writes naming the same content in opposite orders, in
    /// one field or across fields, cannot deadlock. Every entry is shape-checked before any row lock; another tenant's
    /// stage contributes no content row. Row locks are re-entrant, so resolution re-reads the same rows.
    /// </summary>
    public async ValueTask LockEntriesAsync(IReadOnlyList<JsonElement> lists, TenantId tenant, ContentStoreWrite evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var session = evidence.Transaction ?? throw new ArgumentException("Attachment resolution requires the Records transaction.", nameof(evidence));
        var stages = new SortedSet<string>(StringComparer.Ordinal);
        var contents = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (list.ValueKind != JsonValueKind.Array) throw Invalid();
            foreach (var entry in list.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("stage_id", out var stage))
                {
                    if (!AttachmentFieldKind.HasStageShape(entry)) throw Invalid();
                    stages.Add(stage.GetGuid().ToString("N"));
                }
                else
                {
                    if (!AttachmentFieldKind.HasReferenceShape(entry)) throw Invalid();
                    contents.Add(entry.GetProperty("content_id").GetString()!);
                }
            }
        }
        foreach (var stage in stages)
        {
            var staged = await session.FindStagedForUpdateAsync(Guid.ParseExact(stage, "N"), cancellationToken).ConfigureAwait(false);
            if (staged is not null && staged.Tenant == tenant) contents.Add(staged.ContentId.Value);
        }
        foreach (var content in contents)
        {
            ContentStorageItemKey key;
            try { key = new(tenant, new(content)); }
            catch (ArgumentException) { continue; }
            await session.FindItemForUpdateAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Removes field-owned references through the record's disposal transaction; holds fail toward retention.</summary>
    public async ValueTask RemoveAsync(JsonElement references, TenantId tenant, HoldStatus hold, string run,
        ContentStoreWrite evidence, CancellationToken cancellationToken = default)
    {
        if (evidence.Transaction is null) throw new ArgumentException("Attachment removal requires the Records transaction.", nameof(evidence));
        // The same canonical row order as a write, so two disposals sharing content cannot wait on each other.
        await LockEntriesAsync([references], tenant, evidence, cancellationToken).ConfigureAwait(false);
        foreach (var reference in references.EnumerateArray())
            await store.RemoveReferenceAsync(tenant, FromJson(reference), hold, run, evidence with
            {
                Operation = new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), evidence.Operation.Fingerprint),
                Audit = evidence.Audit with { AuditId = Guid.NewGuid().ToString("N") },
            }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateReference(ICompiledAttachmentFieldKind field, JsonElement value, string declaration)
    {
        var refusals = field.Validate(value, "");
        if (refusals.Count == 0) return;
        if (refusals[0].Code == "content.too-large")
            throw new ContentRefusedException("content.too-large", "The item exceeds the effective field limit.", declaration,
                new Dictionary<string, long> { ["limit"] = field.EffectiveLimitBytes, ["observed"] = value.GetProperty("length").GetInt64() }) { JsonPointer = FieldPointer(declaration) };
        if (refusals[0].Code == "content.field-media-type-not-allowed")
            throw new ContentRefusedException("content.field-media-type-not-allowed", "The field does not accept this media type.", declaration) { JsonPointer = FieldPointer(declaration) };
        throw Invalid();
    }

    private static string FieldPointer(string field) => "/" + field.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static ContentRefusedException MissingKept(string pointer) =>
        new(ContentRefusals.StagingExpired, "The staged item is no longer available.", InMemoryContentStore.StoreDeclaration) { JsonPointer = pointer };

    private static FieldAdmissionException Invalid() => new([new("field.value_type_mismatch", "", "An attachment write requires kept references or staged entries.")]);

    private static JsonObject ToJson(ContentReference reference) => new()
    {
        ["content_id"] = reference.ContentId.Value, ["length"] = reference.Length,
        ["media_type"] = reference.MediaType, ["display_name"] = reference.DisplayName,
        ["owner"] = new JsonObject { ["record"] = reference.Owner.Record, ["element"] = reference.Owner.Element },
    };

    private static ContentReference FromJson(JsonElement reference) => new(new(reference.GetProperty("content_id").GetString()!),
        reference.GetProperty("length").GetInt64(), reference.GetProperty("media_type").GetString()!, reference.GetProperty("display_name").GetString()!,
        new(reference.GetProperty("owner").GetProperty("record").GetString()!, reference.GetProperty("owner").GetProperty("element").GetString()!));
}
