using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Foundation.Documents;

/// <summary>One template as provider-neutral pack content (DES-0021 section 7).</summary>
/// <param name="DefinitionId">The template key.</param>
/// <param name="Version">The immutable version.</param>
/// <param name="Content">Canonical authored content: the template without its server-derived members.</param>
public sealed record TemplatePackEntry(string DefinitionId, string Version, byte[] Content)
{
    /// <summary>The transport content kind, 6.</summary>
    public int ContentKind => DocumentsPackIdentity.ContentKind;

    /// <summary>SHA-256 over the exact content bytes, lower-case hex.</summary>
    public string Digest => Convert.ToHexStringLower(SHA256.HashData(Content));
}

/// <summary>Documents' pack path: export after publish admission, and parse at install.</summary>
public static class TemplatePack
{
    // Server-derived: the installing host stamps them and authored content never carries them.
    private static readonly string[] ServerDerived = ["tenant", "provenance"];

    /// <summary>Projects one template into pack content after publish admission. Publishes nothing.</summary>
    /// <exception cref="TemplateAdmissionException">The template was refused.</exception>
    public static TemplatePackEntry Export(TemplateDefinition template, TemplateSurfaces surfaces)
    {
        TemplateDefinitionAdmission.ValidateForPublish(template, surfaces);
        var node = TemplateDefinitionJson.ToNode(template);
        var envelope = node["envelope"]!.AsObject();
        foreach (var member in ServerDerived) envelope.Remove(member);
        return new(template.Envelope.Identity, template.Envelope.Version, TemplateDefinitionJson.Canonical(node));
    }

    /// <summary>
    /// Parses one content item against the pinned canonical contract, stamping the installing tenant and
    /// provenance. A malformed item is a named miss, never an exception.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> content, string tenant, JsonElement provenance,
        out TemplateDefinition? template, out TemplateRefusal? miss)
    {
        template = null;
        miss = null;
        JsonObject? body;
        try { body = JsonNode.Parse(content) as JsonObject; }
        catch (JsonException) { body = null; }
        if (body?["envelope"] is not JsonObject)
        {
            miss = new(TemplateDefinitionCodes.BodyInvalid, "");
            return false;
        }
        try { template = TemplateDefinitionJson.FromNode(Stamp(body, tenant, provenance)); }
        catch (JsonException) { miss = new(TemplateDefinitionCodes.BodyInvalid, ""); }
        return template is not null;
    }

    private static JsonObject Stamp(JsonObject body, string tenant, JsonElement provenance)
    {
        var envelope = body["envelope"]!.AsObject();
        envelope["tenant"] = tenant;
        envelope["provenance"] = JsonSerializer.SerializeToNode(provenance);
        return body;
    }
}
