namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A provider-neutral Layout entry; publication lifecycle belongs to the shared catalogue.</summary>
/// <param name="DefinitionId">The stable definition identity.</param>
/// <param name="Version">The immutable version declared by the producer.</param>
/// <param name="Content">The canonical definition bytes.</param>
public sealed record LayoutDefinitionPackageEntry(
    string DefinitionId,
    string Version,
    PlatformPackageContent Content)
{
    /// <summary>Gets the definition's archive namespace.</summary>
    public DefinitionKind Kind => DefinitionKind.Layout;
    /// <summary>Gets the additive pack content kind.</summary>
    public int ContentKind => LayoutPackIdentity.ContentKind;
    /// <summary>Gets the additive Layout primitive bucket.</summary>
    public int Primitive => LayoutPackIdentity.Primitive;
}

/// <summary>Admits and projects Layout content without owning persistence or publication state.</summary>
public static class LayoutDefinitionPackageExporter
{
    /// <summary>Projects one definition after structural publication admission.</summary>
    /// <param name="definition">The definition selected by the shared catalogue.</param>
    /// <param name="kinds">The host kind register, or the platform grammar.</param>
    /// <returns>The provider-neutral entry. This operation does not publish a version.</returns>
    public static LayoutDefinitionPackageEntry Export(LayoutDefinition definition, LayoutBlockKindRegistry? kinds = null)
    {
        LayoutDefinitionAdmission.ValidateForPublish(definition, kinds ?? LayoutBlockKindRegistry.Platform);
        return new(definition.Envelope.Identity, definition.Envelope.Version,
            PlatformPackageContent.PresentJson(LayoutDefinitionJson.SerializeCanonical(definition)));
    }
}
