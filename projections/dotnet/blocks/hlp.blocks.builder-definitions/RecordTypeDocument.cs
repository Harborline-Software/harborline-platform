using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The provider-neutral envelope of one Record Type definition (DES-0015 records-ck-1). The definition's
/// identity is its body's <c>record_type_id</c>, which the minting caller derives once from this section and
/// the type's name; the envelope therefore carries no second identity to disagree with it. The version is
/// the catalogue's, not the body's: restore-as-draft copies a published body under a new version unchanged,
/// so a version inside the body would contradict the draft it was copied into.
/// </summary>
/// <param name="Tenant">The owning tenant identifier.</param>
/// <param name="Section">The catalogue section that scopes the Record Type id (L102), such as <c>finance</c> or <c>eam</c>.</param>
/// <param name="Contract">The authored definition contract version.</param>
public sealed record RecordTypeDefinitionEnvelope(
    string Tenant,
    string Section,
    DefinitionContractVersion? Contract);

/// <summary>
/// One authored Record Type as the shared catalogue stores it: its envelope, its author-facing name and the
/// typed Records grammar. The grammar's members sit at the document root, so the Records validator's RFC 6901
/// pointers (<c>/record_type_id</c>, <c>/fields/0/field_key</c>) address the stored body directly.
/// </summary>
/// <param name="Envelope">The provider-neutral envelope.</param>
/// <param name="Name">The author-facing name; duplicates across sections are legal and never an identity.</param>
/// <param name="RecordTypeId">The section-scoped identity, immutable across every version of the definition.</param>
/// <param name="Fields">The fields owned by this Record Type.</param>
/// <param name="Traits">The exact Trait revisions and their slot-to-field bindings.</param>
public sealed record RecordTypeDocument(
    RecordTypeDefinitionEnvelope Envelope,
    string Name,
    string RecordTypeId,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<TraitReference>? Traits = null)
{
    /// <summary>The typed Records grammar the validator and schema compiler read.</summary>
    public RecordTypeDefinition ToDefinition() => new(RecordTypeId, Fields, Traits);
}

/// <summary>Canonical, projection-neutral Record Type definition JSON.</summary>
public static class RecordTypeDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a Record Type definition to canonical UTF-8 JSON with a trailing newline.</summary>
    public static byte[] SerializeCanonical(RecordTypeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var source = JsonSerializer.SerializeToNode(document, Options)
            ?? throw new JsonException("The Record Type definition serialized to no JSON value.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonicalize(source).WriteTo(writer, Options);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>
    /// Deserializes a Record Type definition. An unknown member, a null payload or an explicit null for a non-nullable
    /// member is a <see cref="JsonException"/>; an absent required member is reported by <see cref="FirstMissing"/>.
    /// </summary>
    public static RecordTypeDocument Deserialize(ReadOnlySpan<byte> json)
        => JsonSerializer.Deserialize<RecordTypeDocument>(json, Options)
            ?? throw new JsonException("The Record Type definition payload is null.");

    /// <summary>
    /// The RFC 6901 pointer of the first required member the JSON left absent, or of a null list element; null when
    /// the document is whole. The serializer leaves an absent constructor member null rather than refusing it, because
    /// optional members are omitted from the canonical form; this walk names the required ones, so admission refuses
    /// a partial document instead of dereferencing it.
    /// </summary>
    public static string? FirstMissing(RecordTypeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Envelope is null) return null;
        if (document.Fields is null) return "/fields";
        foreach (var (field, index) in document.Fields.Select((field, index) => (field, index)))
        {
            var pointer = $"/fields/{index}";
            if (field is null) return pointer;
            if (field.FieldKey is null) return pointer + "/field_key";
            if (field.DisplayName is null) return pointer + "/display_name";
            if (field.Binding is not { } binding) continue;
            if (binding.Kind is null) return pointer + "/binding/kind";
            if (binding.Kind.KindId is null) return pointer + "/binding/kind/kind_id";
            if (binding.Kind.Version is null) return pointer + "/binding/kind/version";
            if (binding.Kind.Parameters is null) return pointer + "/binding/kind/parameters";
            if (binding.Constraints is null) return pointer + "/binding/constraints";
            if (binding.Constraints.ReadRoleIds is null) return pointer + "/binding/constraints/read_role_ids";
        }
        foreach (var (trait, index) in (document.Traits ?? []).Select((trait, index) => (trait, index)))
        {
            var pointer = $"/traits/{index}";
            if (trait is null) return pointer;
            if (trait.TraitId is null) return pointer + "/trait_id";
            if (trait.Version is null) return pointer + "/version";
            foreach (var (binding, slot) in (trait.SlotBindings ?? []).Select((binding, slot) => (binding, slot)))
            {
                if (binding is null) return $"{pointer}/slot_bindings/{slot}";
                if (binding.SlotKey is null) return $"{pointer}/slot_bindings/{slot}/slot_key";
                if (binding.FieldKey is null) return $"{pointer}/slot_bindings/{slot}/field_key";
            }
        }
        return null;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        // No dictionary key policy: field-kind parameter names are the kind's own wire names and must
        // round-trip unchanged.
        // The grammar has no enum members, so it needs no enum converter.
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // An explicit null for a non-nullable member is malformed. Absent members are named by FirstMissing,
            // since the canonical form omits optional ones and required-parameter enforcement would refuse those too.
            RespectNullableAnnotations = true,
        };
    }

    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(
                property.Key,
                property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value
            .Select(item => item is null ? null : Canonicalize(item))
            .ToArray()),
        _ => node.DeepClone(),
    };
}
