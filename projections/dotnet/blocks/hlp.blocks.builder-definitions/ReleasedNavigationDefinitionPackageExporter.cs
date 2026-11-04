using System.Text;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The additive wire identities for DES-0052 layout-eng-24's released navigation producer.</summary>
public static class ReleasedNavigationPackIdentity
{
    /// <summary>
    /// Content kind 21, reserved in <c>PackContentKindRegistry</c>: the next free value after Assistance's 20
    /// (owner ruling, 2026-09-29). 18 is the api's <c>Resource</c> and was a collision (T-738).
    /// </summary>
    public const int ContentKind = 21;

    /// <summary>The Navigation pillar, 3. 13 is Booking's and was a collision (T-738).</summary>
    public const int Primitive = 3;
}

/// <summary>A provider-neutral released-navigation entry selected by the shared catalogue.</summary>
/// <param name="DefinitionId">The stable definition identity.</param>
/// <param name="Version">The immutable producer revision.</param>
/// <param name="Content">The canonical released definition document.</param>
public sealed record ReleasedNavigationDefinitionPackageEntry(
    string DefinitionId,
    string Version,
    PlatformPackageContent Content)
{
    /// <summary>Gets the definition archive namespace.</summary>
    public DefinitionKind Kind => DefinitionKind.Navigation;
    /// <summary>Gets the additive transport content kind.</summary>
    public int ContentKind => ReleasedNavigationPackIdentity.ContentKind;
    /// <summary>Gets the additive primitive bucket.</summary>
    public int Primitive => ReleasedNavigationPackIdentity.Primitive;
}

/// <summary>Admits and exports released navigation without owning transport, signing, or installation.</summary>
public static class ReleasedNavigationDefinitionPackageExporter
{
    /// <summary>Projects one admitted definition to provider-neutral package content.</summary>
    public static ReleasedNavigationDefinitionPackageEntry Export(ReleasedNavigationDefinition definition)
    {
        ReleasedNavigationDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Publish);
        return new(
            definition.Identity,
            definition.Version,
            PlatformPackageContent.PresentJson(ReleasedNavigationDefinitionJson.SerializeCanonical(definition)));
    }

    /// <summary>
    /// Reads the entries a platform package export carries under <paramref name="definitionId"/>: each item of that id,
    /// at the export's revision, with its payload left for <see cref="ReleasedNavigationLookup.Install"/> to admit or
    /// refuse. An export with no such item yields none, and lookup then refuses each entry as missing; nothing compiled
    /// stands in. A malformed export, or an item of that id with no present payload, refuses at install.
    /// </summary>
    public static IReadOnlyList<ReleasedNavigationDefinitionPackageEntry> Read(ReadOnlySpan<byte> platformExport, string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        try
        {
            using var document = JsonDocument.Parse(platformExport.ToArray());
            var revision = document.RootElement.GetProperty("revision").GetString();
            if (string.IsNullOrWhiteSpace(revision)) throw Unreadable(definitionId);
            var entries = new List<ReleasedNavigationDefinitionPackageEntry>();
            foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
            {
                if (item.GetProperty("id").GetString() != definitionId) continue;
                var content = item.GetProperty("content");
                if (content.GetProperty("classification").GetString() != "present") throw Unreadable(definitionId);
                entries.Add(new(definitionId, revision,
                    PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(content.GetProperty("payload").GetRawText()))));
            }
            return entries;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw Unreadable(definitionId);
        }
    }

    private static DefinitionRefusalException Unreadable(string definitionId) =>
        new(DefinitionAdmissionPhase.Install, [new(ReleasedNavigationDefinitionCodes.DefinitionInvalid, "/", definitionId)]);
}
