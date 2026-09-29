using System.Text.Json;

using Harborline.Foundation.Definitions;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-572 slice 3 (T-724 rulings 85 to 88): Layout refuses a missing or out-of-window envelope contract through the
/// shared check against the platform seed's window, at author, publish, install and render.
/// </summary>
public sealed class LayoutContractWindowTests
{
    private const string Target = "surface.contract@1.0.0";
    private static readonly IReadOnlyDictionary<string, string> Host = new Dictionary<string, string> { [LayoutPackIdentity.Capability] = "1.0.0" };

    private static LayoutDefinition Surface(DefinitionContractVersion? contract, string minimumPlatformVersion = "1.0.0") => new(
        new("surface.contract", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { source = "t-572" }), "standard", false,
            [new(LayoutPackIdentity.Capability, minimumPlatformVersion)], contract),
        1, LayoutMedium.Screen, LayoutIntent.Observe,
        [new("title", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Orders")), [])],
        [], [], [], null, []);

    private static LayoutDefinitionPackageEntry Entry(LayoutDefinition definition) => new(definition.Envelope.Identity, definition.Envelope.Version,
        PlatformPackageContent.PresentJson(LayoutDefinitionJson.SerializeCanonical(definition)));

    private static IEnumerable<(DefinitionAdmissionPhase Stage, Action Admit)> Stages(LayoutDefinition definition) =>
    [
        (DefinitionAdmissionPhase.Author, () => LayoutDefinitionAdmission.ValidateForAuthoring(definition, LayoutTestAccess.GrantsAll)),
        (DefinitionAdmissionPhase.Publish, () => LayoutDefinitionAdmission.ValidateForPublish(definition, LayoutTestAccess.GrantsAll)),
        (DefinitionAdmissionPhase.Publish, () => LayoutDefinitionPackageExporter.Export(definition)),
        (DefinitionAdmissionPhase.Install, () => LayoutPackHostAdmission.Admit([Entry(definition)], Host)),
        (DefinitionAdmissionPhase.Render, () => LayoutPersistedValueAdmission.ValidateForRuntime(definition)),
        (DefinitionAdmissionPhase.Render, () => LayoutPersistedValueAdmission.ValidateForReact(definition)),
        (DefinitionAdmissionPhase.Render, () => LayoutPersistedValueAdmission.ValidateForBlazor(definition)),
    ];

    public static TheoryData<int?, int?, string> Refused => new()
    {
        { null, null, "definition.contract.missing" },
        { -1, 0, "definition.contract.missing" },
        { 2, 0, "definition.contract.out_of_window" },
        { 1, 1, "definition.contract.out_of_window" },
        { 0, 0, "definition.contract.out_of_window" },
    };

    [Theory(DisplayName = "T-572 slice 3 (rulings 85-88): a Layout definition whose contract is missing or outside the seed window is refused at author, publish, install and render at /envelope/contract")]
    [MemberData(nameof(Refused))]
    public void MissingOrOutOfWindowContractRefusesAtEveryStage(int? major, int? minor, string code)
    {
        var definition = Surface(major is null ? null : new(major.Value, minor!.Value));
        foreach (var (stage, admit) in Stages(definition))
        {
            var refused = Assert.Throws<DefinitionRefusalException>(admit);
            Assert.Equal(stage, refused.Stage);
            var target = stage == DefinitionAdmissionPhase.Install ? Target : null;
            Assert.Equal([new DefinitionRefusal(code, "/envelope/contract", target)], refused.Refusals);
        }
    }

    [Fact(DisplayName = "T-572 slice 3: the seed's own contract {1, 0} is admitted at author, publish, install and render")]
    public void TheSeedContractIsAdmittedAtEveryStage()
    {
        Assert.Equal(new DefinitionContractWindow(1, 0, 1), PlatformPackageSeed.ContractWindow);
        foreach (var (_, admit) in Stages(Surface(new(1, 0)))) admit();
    }

    [Fact(DisplayName = "T-572 slice 3 (ruling 85): a Layout pack entry that fails both its minimum_platform_version floor and the contract window reports both refusals at install")]
    public void FloorAndWindowBothRefuseAtInstall()
    {
        var refused = Assert.Throws<DefinitionRefusalException>(() =>
            LayoutPackHostAdmission.Admit([Entry(Surface(new(2, 0), minimumPlatformVersion: "9.0.0"))], Host));

        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal(
            [
                new DefinitionRefusal("definition.contract.out_of_window", "/envelope/contract", Target),
                new DefinitionRefusal(LayoutDefinitionCodes.CapabilityUnsupported, "/envelope/requires/0/minimum_platform_version", Target),
            ],
            refused.Refusals);
    }
}
