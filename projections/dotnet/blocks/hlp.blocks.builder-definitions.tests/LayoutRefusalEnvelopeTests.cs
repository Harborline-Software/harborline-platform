using System.Text.Json;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-583 item 2: Layout refuses in the shared envelope, <c>{ stage, refusals: [{ code, pointer, target? }] }</c>
/// (T-724 ruling 61), at every boundary it admits content, with the stage that refused stated explicitly.
/// </summary>
public sealed class LayoutRefusalEnvelopeTests
{
    // One unregistered kind, one measure binding that names no measure, one row that cannot reflow at 320 CSS pixels.
    private static readonly LayoutDefinition ThreeFaults = LayoutBoundRegisterTests.Sealed(new(
        new("surface.orders", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { source = "test" }), "standard", false, []),
        1, LayoutMedium.Screen, LayoutIntent.Observe,
        [
            new("chart", "layout.unregistered", new LayoutQueryBinding("view.orders"), []),
            new("total", "layout.text", new LayoutMeasureBinding(""), []),
            new("row", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Row")), [],
                Container: new LayoutContainer(LayoutContainerKind.Flow, Wrap: LayoutWrap.NoWrap, ColumnCount: 3)),
        ],
        [], [], [], null, []));

    private static readonly DefinitionRefusal[] Expected =
    [
        new(LayoutDefinitionCodes.BlockKindUnknown, "/blocks/0/kind"),
        new(LayoutDefinitionCodes.BindingInvalid, "/blocks/1/binding"),
        new(LayoutDefinitionCodes.ReflowForbidden, "/blocks/2/container"),
    ];

    [Fact(DisplayName = "T-583 item 2: an unregistered kind, a missing measure and a reflow-breaking row report exactly three code and pointer refusals in the shared envelope, with the stage that refused, at author, publish and render")]
    public void ThreeFaultsReportThreeRefusalsAtEveryStage()
    {
        foreach (var (stage, admit) in new (DefinitionAdmissionPhase, Action)[]
        {
            (DefinitionAdmissionPhase.Author, () => LayoutDefinitionAdmission.ValidateForAuthoring(ThreeFaults, LayoutTestAccess.GrantsAll)),
            (DefinitionAdmissionPhase.Publish, () => LayoutDefinitionAdmission.ValidateForPublish(ThreeFaults, LayoutTestAccess.GrantsAll)),
            (DefinitionAdmissionPhase.Render, () => LayoutPersistedValueAdmission.ValidateForRuntime(ThreeFaults)),
            (DefinitionAdmissionPhase.Render, () => LayoutPersistedValueAdmission.ValidateForRuntime(ThreeFaults, LayoutHostRegisters.Platform)),
            (DefinitionAdmissionPhase.Render, () => LayoutPersistedValueAdmission.ValidateForReact(ThreeFaults)),
            (DefinitionAdmissionPhase.Render, () => LayoutPersistedValueAdmission.ValidateForBlazor(ThreeFaults)),
        })
        {
            var refused = Assert.Throws<DefinitionRefusalException>(admit);
            Assert.Equal(stage, refused.Stage);
            Assert.Equal(Expected, refused.Refusals);
        }
    }

    [Fact(DisplayName = "T-583 item 2: the valid control surface reports zero refusals at author, publish and render")]
    public void TheValidControlReportsNoRefusals()
    {
        var valid = ThreeFaults with
        {
            Blocks =
            [
                ThreeFaults.Blocks[0] with { Kind = "layout.table" },
                ThreeFaults.Blocks[1] with { Binding = new LayoutMeasureBinding("orders.total") },
                ThreeFaults.Blocks[2] with { Container = ThreeFaults.Blocks[2].Container! with { Wrap = LayoutWrap.Wrap } },
            ],
        };
        LayoutDefinitionAdmission.ValidateForAuthoring(valid, LayoutTestAccess.GrantsAll);
        LayoutDefinitionAdmission.ValidateForPublish(valid, LayoutTestAccess.GrantsAll);
        LayoutPersistedValueAdmission.ValidateForRuntime(valid);
    }

    [Fact(DisplayName = "T-583 item 2: authorless pack export refuses in the shared envelope at the publish stage")]
    public void PackExportRefusesAtPublish()
    {
        var exported = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionPackageExporter.Export(ThreeFaults));
        Assert.Equal(DefinitionAdmissionPhase.Publish, exported.Stage);
        Assert.Equal(Expected, exported.Refusals);
    }
}
