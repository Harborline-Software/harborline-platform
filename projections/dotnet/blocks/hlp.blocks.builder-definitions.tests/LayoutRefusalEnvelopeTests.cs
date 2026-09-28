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
        LayoutPackHostAdmission.Admit([Entry(valid)], Host);
    }

    private static readonly IReadOnlyDictionary<string, string> Host = new Dictionary<string, string> { [LayoutPackIdentity.Capability] = "1.0.0" };

    private static LayoutDefinitionPackageEntry Entry(LayoutDefinition definition) => new(definition.Envelope.Identity, definition.Envelope.Version,
        PlatformPackageContent.PresentJson(LayoutDefinitionJson.SerializeCanonical(definition)));

    [Fact(DisplayName = "T-583 item 2: install refuses the same three code and pointer refusals in the shared envelope at the install stage, each naming its fetchable definition target")]
    public void ThreeFaultsReportThreeRefusalsAtInstall()
    {
        var refused = Assert.Throws<DefinitionRefusalException>(() => LayoutPackHostAdmission.Admit([Entry(ThreeFaults)], Host));

        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal(Expected.Select(refusal => refusal with { Target = "surface.orders@1.0.0" }), refused.Refusals);

        // Both editor lanes display _shared/layout/refusal-envelope.json; it is this payload, verbatim.
        var shared = JsonSerializer.Deserialize<DefinitionRefusalReport>(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "refusal-envelope.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } })!;
        Assert.Equal(refused.Stage, shared.Stage);
        Assert.Equal(refused.Refusals, shared.Refusals);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "_shared", "layout"))) return directory.FullName;
        throw new DirectoryNotFoundException("_shared/layout");
    }

    [Fact(DisplayName = "T-583 item 2: install reports every refused entry's tuples across the whole pack, each under its own target, rather than stopping at the first")]
    public void InstallAccumulatesAcrossThePack()
    {
        var stripped = ThreeFaults with
        {
            Envelope = ThreeFaults.Envelope with { Identity = "surface.stripped", Requires = [] },
            Blocks = [ThreeFaults.Blocks[0] with { Kind = "layout.table" }],
        };

        var refused = Assert.Throws<DefinitionRefusalException>(() => LayoutPackHostAdmission.Admit([Entry(ThreeFaults), Entry(stripped)], Host));

        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal(
            [.. Expected.Select(refusal => refusal with { Target = "surface.orders@1.0.0" }),
             new DefinitionRefusal(LayoutDefinitionCodes.CapabilityUndeclared, "/envelope/requires", "surface.stripped@1.0.0")],
            refused.Refusals);
    }

    [Fact(DisplayName = "T-583 item 2: a kind this host's register lacks refuses at install with the common layout.block.kind_unknown code instead of rendering blank; the host that registers it installs")]
    public void AnUnregisteredKindRefusesAtInstall()
    {
        var hosted = ThreeFaults with { Blocks = [ThreeFaults.Blocks[0] with { Kind = "host.component" }] };

        LayoutPackHostAdmission.Admit([Entry(hosted)], Host, new LayoutHostRegisters(new LayoutBlockKindRegistry(["host.component"])));
        var refused = Assert.Throws<DefinitionRefusalException>(() => LayoutPackHostAdmission.Admit([Entry(hosted)], Host));

        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal([new DefinitionRefusal(LayoutDefinitionCodes.BlockKindUnknown, "/blocks/0/kind", "surface.orders@1.0.0")], refused.Refusals);
    }

    [Fact(DisplayName = "T-583 item 2: authorless pack export refuses in the shared envelope at the publish stage")]
    public void PackExportRefusesAtPublish()
    {
        var exported = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionPackageExporter.Export(ThreeFaults));
        Assert.Equal(DefinitionAdmissionPhase.Publish, exported.Stage);
        Assert.Equal(Expected, exported.Refusals);
    }
}
