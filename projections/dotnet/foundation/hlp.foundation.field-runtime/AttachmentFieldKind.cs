using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.ContentStore;

namespace Harborline.Foundation.FieldRuntime;

// DES-0054 ck-3, ck-5, ck-7 and cc-1. Store limits are host facts, never guessed defaults.
internal sealed class AttachmentFieldKind : ICompiledAttachmentFieldKind
{
    private static readonly IReadOnlyList<string> StoreMediaTypes = InMemoryContentStore.SupportedMediaTypes;

    internal AttachmentFieldKind(AdmittedFieldKind kind, IReadOnlyDictionary<string, string> parameters,
        long? storeLimit, string pointer)
    {
        Kind = kind;
        if (storeLimit is null)
            throw Refuse("content.store-limit-unavailable", pointer);
        foreach (var name in parameters.Keys)
            if (name is not ("item_limit_bytes" or "accepted_media_types"))
                throw Refuse("field.kind_parameter_unknown", pointer + "/parameters/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
        if (parameters.ContainsKey("item_limit_bytes"))
        {
            var limit = parameters["item_limit_bytes"];
            long parsed = 0;
            if (!long.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed <= 0)
                throw Refuse("field.kind_parameter_invalid", pointer + "/parameters/item_limit_bytes");
            DeclaredLimitBytes = parsed;
        }
        var mediaTypes = StoreMediaTypes.ToArray();
        if (parameters.ContainsKey("accepted_media_types"))
        {
            var media = parameters["accepted_media_types"];
            try { mediaTypes = JsonSerializer.Deserialize<string[]>(media) ?? throw new JsonException(); }
            catch (JsonException) { throw Refuse("field.kind_parameter_invalid", pointer + "/parameters/accepted_media_types"); }
            if (mediaTypes.Length == 0 || mediaTypes.Distinct(StringComparer.Ordinal).Count() != mediaTypes.Length
                || mediaTypes.Any(type => !StoreMediaTypes.Contains(type, StringComparer.Ordinal)))
                throw Refuse("content.field-media-type-not-allowed", pointer + "/parameters/accepted_media_types");
        }
        AcceptedMediaTypes = Array.AsReadOnly(mediaTypes);
        EffectiveLimitBytes = Math.Min(storeLimit.Value, DeclaredLimitBytes ?? storeLimit.Value);
        LimitAboveStore = DeclaredLimitBytes > storeLimit;
        var schema = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,
             "required":["content_id","length","media_type","display_name","owner"],
             "properties":{
               "content_id":{"type":"string","pattern":"^hmac-sha256:[0-9a-f]{64}$"},
               "length":{"type":"integer","minimum":0},
               "media_type":{"type":"string"},"display_name":{"type":"string","minLength":1},
               "owner":{"type":"object","additionalProperties":false,"required":["record","element"],
                 "properties":{"record":{"type":"string","minLength":1},"element":{"type":"string","minLength":1}}}}}
            """)!.AsObject();
        // The size limit is a write-time check (Validate), never schema identity: the effective limit follows the host's
        // store limit, and a host change must not alter the identity a committed record was written under.
        schema["properties"]!["media_type"]!["enum"] = JsonSerializer.SerializeToNode(AcceptedMediaTypes);
        schema["x-harborline-field-kind"] = new JsonObject
        {
            ["kind_id"] = kind.KindId, ["version"] = kind.Version,
            ["parameters"] = JsonSerializer.SerializeToNode(parameters),
        };
        JsonSchema = JsonSerializer.SerializeToElement(schema);
    }

    public AdmittedFieldKind Kind { get; }
    public JsonElement JsonSchema { get; }
    public long? DeclaredLimitBytes { get; }
    public long EffectiveLimitBytes { get; }
    public IReadOnlyList<string> AcceptedMediaTypes { get; }
    public bool LimitAboveStore { get; }

    public IReadOnlyList<FieldRefusal> Validate(JsonElement value, string jsonPointer)
    {
        if (!HasReferenceShape(value))
            return [new("field.value_type_mismatch", jsonPointer, "An attachment value must be a content reference.")];
        var media = value.GetProperty("media_type").GetString()!;
        var length = value.GetProperty("length").GetInt64();
        if (length > EffectiveLimitBytes)
            return [new("content.too-large", jsonPointer, "The item exceeds the effective field size limit.")];
        return AcceptedMediaTypes.Contains(media, StringComparer.Ordinal) ? []
            : [new("content.field-media-type-not-allowed", jsonPointer, "The field does not accept this media type.")];
    }

    public IReadOnlyList<FieldRefusal> ValidateSubmission(JsonElement value, string jsonPointer)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("stage_id", out _))
            return HasStageShape(value) ? []
                : [new("field.value_type_mismatch", jsonPointer, "A staged entry carries exactly a stage id and a display name.")];
        return Validate(value, jsonPointer);
    }

    internal static bool HasStageShape(JsonElement value) => ObjectWith(value, ["stage_id", "display_name"]) && value.GetProperty("stage_id").ValueKind == JsonValueKind.String
                && value.GetProperty("stage_id").TryGetGuid(out _) && Text(value, "display_name", out _);

    internal static bool HasReferenceShape(JsonElement value)
    {
        string id = string.Empty, media = string.Empty;
        long length = 0;
        return ObjectWith(value, ["content_id", "length", "media_type", "display_name", "owner"])
            && Text(value, "content_id", out id) && id.Length == 76 && id.StartsWith("hmac-sha256:", StringComparison.Ordinal)
            && !id.AsSpan(12).ContainsAnyExcept("0123456789abcdef")
            && value.GetProperty("length").ValueKind == JsonValueKind.Number
            && value.GetProperty("length").TryGetInt64(out length) && length >= 0
            && Text(value, "media_type", out media) && Text(value, "display_name", out _)
            && ObjectWith(value.GetProperty("owner"), ["record", "element"])
            && Text(value.GetProperty("owner"), "record", out _) && Text(value.GetProperty("owner"), "element", out _);
    }

    public IReadOnlyList<FieldRefusal> ValidateJson(string json, string jsonPointer)
    {
        try { using var document = JsonDocument.Parse(json); return Validate(document.RootElement, jsonPointer); }
        catch (Exception error) when (error is JsonException or ArgumentNullException)
        { return [new("field.value_malformed", jsonPointer, "The attachment value must be valid JSON.")]; }
    }

    private static bool ObjectWith(JsonElement value, string[] names) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Count() == names.Length && names.All(name => value.TryGetProperty(name, out _));
    private static bool Text(JsonElement value, string name, out string text)
    {
        text = value.GetProperty(name).ValueKind == JsonValueKind.String ? value.GetProperty(name).GetString()! : "";
        return !string.IsNullOrWhiteSpace(text);
    }
    private static FieldAdmissionException Refuse(string code, string pointer) => new([new(code, pointer, "The attachment field declaration is not admitted.")]);
}
