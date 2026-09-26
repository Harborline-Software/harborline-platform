namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The additive wire identities for DES-0052 layout-eng-24's released navigation producer.</summary>
public static class ReleasedNavigationPackIdentity
{
    /// <summary>Released navigation follows Layout's content kind 17.</summary>
    public const int ContentKind = 18;

    /// <summary>Released navigation follows Layout's primitive bucket 12.</summary>
    public const int Primitive = 13;
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
}
