using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Harborline.Foundation.Documents;

/// <summary>Documents' pack wire identity (DES-0021 documents-ck-1). Kept at 6; no number is allocated here.</summary>
public static class DocumentsPackIdentity
{
    /// <summary>Content kind 6, <c>TemplateDefinition</c>.</summary>
    public const int ContentKind = 6;
}

/// <summary>The definition layer that supplied a template revision.</summary>
public enum TemplateCascadeLayer
{
    /// <summary>The kernel-owned baseline.</summary>
    KernelCore,
    /// <summary>A platform subsystem baseline.</summary>
    Subsystem,
    /// <summary>A platform package contribution.</summary>
    PlatformPackage,
    /// <summary>A domain package contribution.</summary>
    DomainPackage,
    /// <summary>A tenant-owned configuration contribution.</summary>
    TenantConfiguration,
}

/// <summary>A platform capability a template requires.</summary>
/// <param name="Capability">The stable capability identifier.</param>
/// <param name="MinimumPlatformVersion">The optional minimum platform version.</param>
public sealed record TemplateRequirement(string Capability, string? MinimumPlatformVersion = null);

/// <summary>The definition envelope (ADR 0006, documents-ck-2).</summary>
/// <param name="Identity">The stable template key.</param>
/// <param name="Version">The immutable semantic version.</param>
/// <param name="Tenant">The owning tenant; server-derived, never authored pack content.</param>
/// <param name="CascadeLayer">The contributing cascade layer.</param>
/// <param name="Provenance">The producer-supplied provenance; server-derived, never authored pack content.</param>
/// <param name="Requires">The required platform capabilities.</param>
public sealed record TemplateDefinitionEnvelope(
    string Identity,
    string Version,
    string Tenant,
    TemplateCascadeLayer CascadeLayer,
    JsonElement Provenance,
    IReadOnlyList<TemplateRequirement> Requires);

/// <summary>The record type and pinned record-type version the template merges from.</summary>
public sealed record TemplateRecordTypeBinding(string RecordType, string Version);

/// <summary>How the document's render locale resolves; distinct from the authoring locale.</summary>
public enum TemplateLocaleKind
{
    /// <summary>A fixed BCP-47 tag.</summary>
    Fixed,
    /// <summary>The recipient locale carried on the record.</summary>
    FromRecord,
    /// <summary>The instance default.</summary>
    FromInstance,
}

/// <summary>The document locale policy.</summary>
public sealed record TemplateLocalePolicy(TemplateLocaleKind Kind, string? Tag = null);

/// <summary>Template data for the masthead; never product chrome.</summary>
public sealed record TemplateStyle(string? BrandName = null, string? LogoAssetRef = null);

/// <summary>
/// The exact pin of the published Layout page surface this template composes (ADR 0094, documents-ck-7).
/// The surface replaces the legacy flat block list; the template keeps its own artefact and pins, never copies.
/// </summary>
public sealed record TemplateSurfacePin(string SurfaceDefinitionId, string SurfaceVersion);

/// <summary>
/// The Documents composition's own definition (DES-0021 documents-ck-1): a named, versioned artefact over one
/// Layout page surface with intent issue. Its CLR name is shared on purpose with the distinct catalogue
/// <c>TemplateDefinition</c> (CONTEXT.md); this one is the record-to-issued-document template.
/// </summary>
public sealed record TemplateDefinition(
    TemplateDefinitionEnvelope Envelope,
    string DocumentType,
    TemplateRecordTypeBinding RecordType,
    TemplateLocalePolicy Locale,
    TemplateStyle? Style,
    TemplateSurfacePin Surface);

/// <summary>Canonical, projection-neutral template JSON: snake_case, ordinal-sorted keys, one trailing newline.</summary>
public static class TemplateDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a template to canonical UTF-8 JSON.</summary>
    public static byte[] SerializeCanonical(TemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Canonical(ToNode(definition));
    }

    internal static byte[] Canonical(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonicalize(node).WriteTo(writer, Options);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Deserializes canonical template JSON. Unknown members refuse.</summary>
    /// <exception cref="JsonException">The payload is not a template.</exception>
    public static TemplateDefinition Deserialize(ReadOnlySpan<byte> json)
        => JsonSerializer.Deserialize<TemplateDefinition>(json, Options)
            ?? throw new JsonException("The template payload is null.");

    internal static JsonObject ToNode(TemplateDefinition definition)
        => JsonSerializer.SerializeToNode(definition, Options)?.AsObject()
            ?? throw new JsonException("The template serialized to no JSON value.");

    internal static TemplateDefinition FromNode(JsonObject node)
        => node.Deserialize<TemplateDefinition>(Options) ?? throw new JsonException("The template payload is null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }

    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value.Select(item => item is null ? null : Canonicalize(item)).ToArray()),
        _ => node.DeepClone(),
    };
}
