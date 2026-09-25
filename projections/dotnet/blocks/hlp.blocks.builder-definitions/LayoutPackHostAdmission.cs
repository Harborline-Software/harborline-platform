namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// A pack refused whole because one Layout entry needs what this host does not have
/// (DES-0052 layout-ck-42). It names the capability and the minimum platform version.
/// </summary>
/// <param name="code">The stable refusal code.</param>
/// <param name="definitionId">The Layout definition whose sealed requirement refused.</param>
/// <param name="minimumPlatformVersion">The declared minimum platform version, or <see langword="null"/> when undeclared.</param>
public sealed class LayoutPackUnsupportedException(
    string code,
    string definitionId,
    string? minimumPlatformVersion)
    : Exception($"{code}: '{definitionId}' requires {LayoutPackIdentity.Capability} {minimumPlatformVersion ?? "(undeclared)"}; the whole pack is refused.")
{
    /// <summary>Gets the stable refusal code.</summary>
    public string Code { get; } = code;

    /// <summary>Gets the refused Layout definition identity.</summary>
    public string DefinitionId { get; } = definitionId;

    /// <summary>Gets the required capability.</summary>
    public string Capability => LayoutPackIdentity.Capability;

    /// <summary>Gets the required minimum platform version.</summary>
    public string? MinimumPlatformVersion { get; } = minimumPlatformVersion;
}

/// <summary>
/// Install-time capability admission for Layout pack content (DES-0052 layout-ck-42). One
/// unsupported entry refuses the entire pack: Layout content is never skipped, its requirement
/// never stripped, and it is never down-converted to fit an older host.
/// </summary>
public static class LayoutPackHostAdmission
{
    /// <summary>Admits every Layout entry in one pack against this host, or refuses them all.</summary>
    /// <param name="entries">The pack's Layout entries.</param>
    /// <param name="hostCapabilities">The capabilities this host provides, by id, with the platform version providing each.</param>
    /// <exception cref="LayoutPackUnsupportedException">One entry is undeclared or unsupported; nothing is admitted.</exception>
    public static void Admit(
        IEnumerable<LayoutDefinitionPackageEntry> entries,
        IReadOnlyDictionary<string, string> hostCapabilities)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(hostCapabilities);

        foreach (var entry in entries)
        {
            var definition = LayoutDefinitionJson.Deserialize(entry.Content.Payload.Span);
            var index = LayoutPackIdentity.SealedRequirementIndex(definition.Envelope.Requires);
            var sealedRequirement = index < 0 ? null : definition.Envelope.Requires[index];
            if (sealedRequirement?.MinimumPlatformVersion is not { } minimum
                || !LayoutVersionSyntax.IsValid(minimum)
                || !DefinitionSemanticVersion.TryParse(minimum, out var required))
            {
                throw new LayoutPackUnsupportedException(LayoutDefinitionCodes.CapabilityUndeclared,
                    entry.DefinitionId, sealedRequirement?.MinimumPlatformVersion);
            }

            if (!hostCapabilities.TryGetValue(LayoutPackIdentity.Capability, out var provided)
                || !DefinitionSemanticVersion.TryParse(provided, out var host)
                || host.CompareTo(required) < 0)
            {
                throw new LayoutPackUnsupportedException(LayoutDefinitionCodes.CapabilityUnsupported,
                    entry.DefinitionId, minimum);
            }
        }
    }
}
