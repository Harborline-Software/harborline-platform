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
    // documents-auth-18: owner, tenant, pack key and provenance are never authored.
    private static readonly string[] AuthorityFields = ["owner", "tenant", "pack_key", "provenance"];

    /// <summary>Projects one template into pack content after publish admission. Publishes nothing.</summary>
    /// <exception cref="TemplateAdmissionException">The template was refused.</exception>
    public static TemplatePackEntry Export(TemplateDefinition template, TemplateSurfaces surfaces)
    {
        TemplateDefinitionAdmission.ValidateForPublish(template, surfaces);
        return new(template.Envelope.Identity, template.Envelope.Version, Authored(template));
    }

    // The authored body: canonical JSON without the server-derived members.
    private static byte[] Authored(TemplateDefinition template)
    {
        var node = TemplateDefinitionJson.ToNode(template);
        var envelope = node["envelope"]!.AsObject();
        foreach (var member in ServerDerived) envelope.Remove(member);
        return TemplateDefinitionJson.Canonical(node);
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
        if (body?["envelope"] is not JsonObject envelope)
        {
            miss = new(TemplateDefinitionCodes.BodyInvalid, "");
            return false;
        }
        foreach (var (node, pointer) in new[] { (body, ""), (envelope, "/envelope") })
            if (AuthorityFields.FirstOrDefault(node.ContainsKey) is { } field)
            {
                miss = new(TemplateDefinitionCodes.AuthorityFieldForbidden, $"{pointer}/{field}");
                return false;
            }
        try { template = TemplateDefinitionJson.FromNode(Stamp(body, tenant, provenance)); }
        catch (JsonException) { miss = new(TemplateDefinitionCodes.BodyInvalid, ""); }
        return template is not null;
    }

    /// <summary>
    /// Install-time admission of a pack's templates. Each entry is parsed, admitted and published on its own:
    /// a refused entry is reported by name and skipped, and every valid sibling still installs.
    /// </summary>
    public static async ValueTask<IReadOnlyList<TemplateInstallOutcome>> InstallAsync(
        IReadOnlyList<TemplatePackEntry> entries, TemplateInstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(target);
        var outcomes = new List<TemplateInstallOutcome>(entries.Count);
        foreach (var entry in entries)
        {
            TemplateInstallOutcome Refused(params TemplateRefusal[] refusals)
                => new(entry.DefinitionId, entry.Version, TemplateInstallOutcomeKind.Refused, refusals);
            if (!TryParse(entry.Content, target.Tenant, target.Provenance, out var template, out var miss))
            {
                outcomes.Add(Refused(miss!));
                continue;
            }
            if (template!.Envelope.Identity != entry.DefinitionId || template.Envelope.Version != entry.Version)
            {
                outcomes.Add(Refused(new TemplateRefusal(TemplateDefinitionCodes.EnvelopeMismatch, "/envelope")));
                continue;
            }
            var refusals = TemplateDefinitionAdmission.Validate(template, target.Surfaces);
            if (refusals.Count > 0)
            {
                outcomes.Add(Refused([.. refusals]));
                continue;
            }
            // documents-auth-26: a stored key and version is a pinned tuple. The same body replays; a different
            // body refuses once by name and the stored body is never replaced.
            if (await target.ResolveStored(template).ConfigureAwait(false) is { } stored)
            {
                outcomes.Add(Authored(stored).AsSpan().SequenceEqual(Authored(template))
                    ? new(entry.DefinitionId, entry.Version, TemplateInstallOutcomeKind.AlreadyPresent, [])
                    : Refused(new TemplateRefusal(TemplateDefinitionCodes.PinnedTupleConflict, "/envelope/version")));
                continue;
            }
            await target.Publish(template).ConfigureAwait(false);
            outcomes.Add(new(entry.DefinitionId, entry.Version, TemplateInstallOutcomeKind.Published, []));
        }
        return outcomes;
    }

    private static JsonObject Stamp(JsonObject body, string tenant, JsonElement provenance)
    {
        var envelope = body["envelope"]!.AsObject();
        envelope["tenant"] = tenant;
        envelope["provenance"] = JsonSerializer.SerializeToNode(provenance);
        return body;
    }
}

/// <summary>What happened to one pack entry at install.</summary>
public enum TemplateInstallOutcomeKind
{
    /// <summary>The template was admitted and published.</summary>
    Published,
    /// <summary>The same key, version and body is already stored; nothing was written.</summary>
    AlreadyPresent,
    /// <summary>The entry was refused by name and skipped; its siblings are unaffected.</summary>
    Refused,
}

/// <summary>One entry's install outcome.</summary>
public sealed record TemplateInstallOutcome(
    string DefinitionId, string Version, TemplateInstallOutcomeKind Kind, IReadOnlyList<TemplateRefusal> Refusals);

/// <summary>The installing host: its tenant and provenance stamp, the surface binding and the shared catalogue.</summary>
/// <param name="Tenant">The installing tenant, stamped on every template.</param>
/// <param name="Provenance">The pack provenance, stamped on every template.</param>
/// <param name="Surfaces">The surface binding admission uses.</param>
/// <param name="ResolveStored">The template already stored under the same key and version, or null.</param>
/// <param name="Publish">Publishes an admitted template into the shared catalogue.</param>
public sealed record TemplateInstallTarget(
    string Tenant,
    JsonElement Provenance,
    TemplateSurfaces Surfaces,
    Func<TemplateDefinition, ValueTask<TemplateDefinition?>> ResolveStored,
    Func<TemplateDefinition, ValueTask> Publish);
