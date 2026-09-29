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
}
