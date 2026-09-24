namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Layout's additive pack wire identities, independent of archive namespaces.</summary>
public static class LayoutPackIdentity
{
    /// <summary>Layout content follows the existing content kinds 0 through 16.</summary>
    public const int ContentKind = 17;

    /// <summary>Layout follows primitive buckets 0 through 11; 99 remains Other.</summary>
    public const int Primitive = 12;

    /// <summary>
    /// The capability a published Layout declares, with its minimum platform version, inside the
    /// signed payload (DES-0052 layout-ck-42). Named by the owner on 2026-09-24.
    /// </summary>
    public const string Capability = "platform.layout";

    // Publication and installation read the sealed requirement through this one lookup, so they
    // cannot disagree. A second declaration is ambiguous — a host could satisfy the lower one —
    // so it counts as undeclared.
    internal static int SealedRequirementIndex(IReadOnlyList<LayoutDefinitionRequirement>? requires)
    {
        var found = -1;
        for (var index = 0; index < (requires?.Count ?? 0); index++)
        {
            if (requires![index]?.Capability != Capability) continue;
            if (found >= 0) return -1;
            found = index;
        }
        return found;
    }
}
