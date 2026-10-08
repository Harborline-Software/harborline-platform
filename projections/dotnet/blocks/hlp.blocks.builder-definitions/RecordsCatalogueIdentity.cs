using System.Text.RegularExpressions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The identity rules every Records definition family shares (Record Type, Class): ids minted once from a
/// catalogue section and a name (L102, DES-0015 K5), an envelope naming the catalogue's tenant and a valid
/// section, and an id that equals its catalogue key and is scoped by its section. Each family names its own id
/// member, such as <c>record_type_id</c> or <c>class_id</c>, and the refusal codes carry that name.
/// </summary>
internal static partial class RecordsCatalogueIdentity
{
    /// <summary>
    /// Mints <c>section.name-slug</c>. A name of punctuation alone refuses <c>records.identity.name_required</c>,
    /// since it would mint the id <c>section.</c>. The section itself is checked by <see cref="CheckEnvelope"/>.
    /// </summary>
    public static string Mint(string section, string? name)
    {
        var slug = Slug(name ?? "");
        if (slug.Length == 0)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Author, [new("records.identity.name_required", "/name")]);
        return $"{section}.{slug}";
    }

    /// <summary>
    /// Creates a minted definition's first draft. Creation is the zero-revision fence: a stream already at this
    /// key fails it, so the collision check and the write are one atomic step and an identical replay still
    /// returns its draft. A collision refuses <c>records.identity.{idMember}_collision</c>, never a renumbering.
    /// </summary>
    public static async ValueTask<DefinitionRevision> CreateAsync(IVersionedDefinitionStore store, DefinitionDocument document,
        string requestId, string idMember, CancellationToken cancellationToken)
    {
        try
        {
            return await store.SaveDraftAsync(document, 0, requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (DefinitionRefusalException refused) when (refused.Refusals is [{ Code: "definition.revision_conflict" }])
        {
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Author,
                [new($"records.identity.{idMember}_collision", "/" + idMember)]);
        }
    }

    /// <summary>
    /// Refuses a later save of an id the authoring boundary never minted: one with no catalogue stream, which only
    /// a client could have constructed (<c>records.identity.{idMember}_unminted</c>).
    /// </summary>
    public static async ValueTask RequireMintedAsync(IVersionedDefinitionStore store, DefinitionKey key, string idMember,
        CancellationToken cancellationToken)
    {
        if ((await store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false)).Count == 0)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Author,
                [new($"records.identity.{idMember}_unminted", "/" + idMember)]);
    }

    /// <summary>
    /// The shared structural checks: the envelope is present, its contract is inside the window, it names the
    /// catalogue's tenant (Author and Publish; an installing host supplies its own tenant), its section is valid,
    /// the name is present, and the id is present, equals the catalogue key and is scoped by its section.
    /// </summary>
    public static IReadOnlyList<DefinitionRefusal> CheckEnvelope(RecordsDefinitionEnvelope? envelope, string? name, string? id,
        string idMember, DefinitionKey key, DefinitionAdmissionPhase phase, DefinitionContractWindow window)
    {
        if (envelope is null)
            return [new("records.envelope_required", "/envelope")];
        var refusals = new List<DefinitionRefusal>();
        if (window.Check(envelope.Contract, null) is { } contract)
            refusals.Add(contract);
        if (phase != DefinitionAdmissionPhase.Install && !StringComparer.Ordinal.Equals(envelope.Tenant, key.Tenant))
            refusals.Add(new("definition.catalogue_mismatch", "/envelope/tenant"));
        var sectionValid = IsSection(envelope.Section);
        if (!sectionValid)
            refusals.Add(new("records.identity.section_invalid", "/envelope/section"));
        if (string.IsNullOrWhiteSpace(name))
            refusals.Add(new("records.identity.name_required", "/name"));
        CheckPackage(envelope, phase, refusals);
        if (string.IsNullOrWhiteSpace(id))
            return refusals;
        // The catalogue key is the id every earlier version was stored under, so a body naming any other id
        // changes the definition's identity, whether inside one version or across versions.
        if (!StringComparer.Ordinal.Equals(id, key.DefinitionId))
            refusals.Add(new($"records.identity.{idMember}_immutable", "/" + idMember));
        else if (sectionValid && !id.StartsWith(envelope.Section + ".", StringComparison.Ordinal))
            refusals.Add(new("records.identity.section_mismatch", "/" + idMember));
        return refusals;
    }

    // records-ck-2 / ADR-0006: a published definition names its package, and its declarations parse.
    private static void CheckPackage(RecordsDefinitionEnvelope envelope, DefinitionAdmissionPhase phase, List<DefinitionRefusal> refusals)
    {
        if (phase != DefinitionAdmissionPhase.Author && string.IsNullOrWhiteSpace(envelope.PackageId))
            refusals.Add(new("records.package.required", "/envelope/package_id"));
        foreach (var (requirement, index) in (envelope.Requires ?? []).Select((requirement, index) => (requirement, index)))
            if (!RecordsRequirement.TryParse(requirement?.Capability, out _, out _))
                refusals.Add(new("records.package.requirement_invalid", $"/envelope/requires/{index}"));
        if (envelope.Exposes is { InterfaceVersion: <= 0 })
            refusals.Add(new("records.package.exposure_invalid", "/envelope/exposes/interface_version"));
    }

    private static bool IsSection(string? section) => section is not null && SectionPattern().IsMatch(section);

    // The same lowercase ASCII kebab slug the builders suggest keys with (DefinitionKeySuggester), without its
    // version suffix or collision sequencing: an id collision is loud, never renumbered.
    private static string Slug(string name) => NonAsciiAlphaNumeric().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionPattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAsciiAlphaNumeric();
}
