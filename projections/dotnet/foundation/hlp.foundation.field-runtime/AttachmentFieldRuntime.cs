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
    /// content session belonging to the enclosing record transaction; this method never opens or commits another one.
    /// </summary>
    public async ValueTask<JsonArray> ResolveWriteAsync(ICompiledAttachmentFieldKind field, JsonElement entries,
        TenantId tenant, ContentOwner owner, ContentStoreWrite evidence, CancellationToken cancellationToken = default)
    {
        if (evidence.Transaction is null) throw new ArgumentException("Attachment resolution requires the Records transaction.", nameof(evidence));
        if (entries.ValueKind != JsonValueKind.Array) throw Invalid();
        var resolved = new JsonArray();
        foreach (var entry in entries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("stage_id", out var stage))
            {
                if (entry.EnumerateObject().Count() != 2 || stage.ValueKind != JsonValueKind.String || !stage.TryGetGuid(out var stageId)
                    || !entry.TryGetProperty("display_name", out var display) || display.ValueKind != JsonValueKind.String
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
                ValidateReference(field, entry, owner.Element);
                var reference = FromJson(entry);
                var item = await evidence.Transaction.FindItemForUpdateAsync(new(tenant, reference.ContentId), cancellationToken).ConfigureAwait(false);
                if (reference.Owner.Record != owner.Record || !reference.Owner.Element.StartsWith(owner.Element + "/", StringComparison.Ordinal)
                    || item is null || !item.Owners.Contains(reference.Owner) || item.Length != reference.Length || item.MediaType != reference.MediaType)
                    throw Invalid();
                resolved.Add(JsonNode.Parse(entry.GetRawText()));
            }
        }
        return resolved;
    }

    /// <summary>Removes field-owned references through the record's disposal transaction; holds fail toward retention.</summary>
    public async ValueTask RemoveAsync(JsonElement references, TenantId tenant, HoldStatus hold, string run,
        ContentStoreWrite evidence, CancellationToken cancellationToken = default)
    {
        if (evidence.Transaction is null) throw new ArgumentException("Attachment removal requires the Records transaction.", nameof(evidence));
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
                new Dictionary<string, long> { ["limit"] = field.EffectiveLimitBytes, ["observed"] = value.GetProperty("length").GetInt64() });
        if (refusals[0].Code == "content.field-media-type-not-allowed")
            throw new ContentRefusedException("content.field-media-type-not-allowed", "The field does not accept this media type.", declaration);
        throw Invalid();
    }

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
