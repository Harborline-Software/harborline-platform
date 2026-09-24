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
}
