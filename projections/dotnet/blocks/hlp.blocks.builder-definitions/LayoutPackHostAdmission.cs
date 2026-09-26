namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// Install-time admission for Layout pack content (DES-0052 §7, layout-ck-42). One unsupported
/// or invalid entry refuses the entire pack: Layout content is never skipped, its requirement never
/// stripped, and it is never down-converted to fit an older host. Every entry's refusals are
/// reported in the shared envelope at the install stage (T-583 item 2, T-724 rulings 61 and 79).
/// </summary>
public static class LayoutPackHostAdmission
{
    /// <summary>Admits every Layout entry in one pack against this host, or refuses them all.</summary>
    /// <param name="entries">The pack's Layout entries.</param>
    /// <param name="hostCapabilities">The capabilities this host provides, by id, with the platform version providing each.</param>
    /// <param name="registers">This host's registers, or the platform grammar when omitted; a kind the register lacks refuses rather than rendering blank.</param>
    /// <exception cref="DefinitionRefusalException">At <see cref="DefinitionAdmissionPhase.Install"/>, with every refusal of every entry, each targeting its entry as <c>id@version</c>; nothing is admitted.</exception>
    public static void Admit(
        IEnumerable<LayoutDefinitionPackageEntry> entries,
        IReadOnlyDictionary<string, string> hostCapabilities,
        LayoutHostRegisters? registers = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(hostCapabilities);

        var refusals = new List<DefinitionRefusal>();
        foreach (var entry in entries)
        {
            // The installer supplied this entry, so naming it back is safe (T-724 ruling 61).
            var target = $"{entry.DefinitionId}@{entry.Version}";
            var definition = LayoutDefinitionJson.Deserialize(entry.Content.Payload.Span);
            try
            {
                LayoutDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Install, registers ?? LayoutHostRegisters.Platform, author: null);
            }
            catch (DefinitionRefusalException refused)
            {
                refusals.AddRange(refused.Refusals.Select(refusal => refusal with { Target = target }));
            }

            var index = LayoutPackIdentity.SealedRequirementIndex(definition.Envelope?.Requires);
            var minimum = index < 0 ? null : definition.Envelope!.Requires[index].MinimumPlatformVersion;
            if (index < 0)
                refusals.Add(new(LayoutDefinitionCodes.CapabilityUndeclared, "/envelope/requires", target));
            else if (!LayoutVersionSyntax.IsValid(minimum) || !DefinitionSemanticVersion.TryParse(minimum!, out var required))
                refusals.Add(new(LayoutDefinitionCodes.CapabilityUndeclared, $"/envelope/requires/{index}/minimum_platform_version", target));
            else if (!hostCapabilities.TryGetValue(LayoutPackIdentity.Capability, out var provided)
                || !DefinitionSemanticVersion.TryParse(provided, out var host)
                || host.CompareTo(required) < 0)
                refusals.Add(new(LayoutDefinitionCodes.CapabilityUnsupported, $"/envelope/requires/{index}/minimum_platform_version", target));
        }

        if (refusals.Count > 0) throw new DefinitionRefusalException(DefinitionAdmissionPhase.Install, refusals);
    }
}
