using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class ReleasedNavigationDefinitionProducerTests
{
    [Fact]
    public void Released_navigation_exports_the_catalogue_entry_and_editor_destination_and_refuses_unavailable_lookups()
    {
        var definition = new ReleasedNavigationDefinition(
            "platform.navigation.workshop",
            "1.0.0",
            new ReleasedSurfaceCatalogue("platform.surface-catalogue.workshop", [
                new ReleasedSurface("platform.surface.views", "Views"),
            ]),
            [new ReleasedNavigationEntry("platform.navigation.views", "Views", "platform.destination.views")],
            [new ReleasedEditorDestination("platform.destination.views", "platform.surface.views")]);

        var exported = ReleasedNavigationDefinitionPackageExporter.Export(definition);
        var artifact = PublishedArtifact.FromExport(PlatformPackageExporter.Export(new PlatformPackageManifest(
            1,
            "harborline.platform.navigation",
            exported.Version,
            [
                new PlatformPackageItem("platform.navigation.package", PlatformSeedStage.PackageRecord, [],
                    PlatformPackageContent.PresentJson("""{"id":"harborline.platform.navigation"}"""u8)),
                new PlatformPackageItem(exported.DefinitionId, PlatformSeedStage.Views, ["platform.navigation.package"], exported.Content),
            ])));
        var installed = ReleasedNavigationLookup.Install([exported]);
        var resolved = installed.Resolve("platform.navigation.views", AllowsAll);

        Assert.Equal(DefinitionKind.Navigation, exported.Kind);
        Assert.Equal("platform.navigation.workshop", exported.DefinitionId);
        Assert.Equal("1.0.0", exported.Version);
        Assert.Equal("harborline.platform.navigation", artifact.Id);
        Assert.Equal("1.0.0", artifact.Version);
        Assert.Matches("^[0-9a-f]{64}$", artifact.Digest);
        Assert.Equal("platform.surface.views", resolved.Surface.Id);

        var missing = Assert.Throws<DefinitionRefusalException>(() => installed.Resolve("platform.navigation.missing", AllowsAll));
        Assert.Equal(DefinitionAdmissionPhase.Render, missing.Stage);
        Assert.Equal([new DefinitionRefusal(ReleasedNavigationDefinitionCodes.NavigationEntryMissing, "/navigation_entries/platform.navigation.missing")], missing.Refusals);

        var unreadable = Assert.Throws<DefinitionRefusalException>(() => installed.Resolve("platform.navigation.views", DeniesAll));
        Assert.Equal([new DefinitionRefusal(ReleasedNavigationDefinitionCodes.DefinitionUnreadable, "/navigation_entries/platform.navigation.views")], unreadable.Refusals);
    }

    private static readonly IReleasedNavigationAccess AllowsAll = new FixtureAccess(true);
    private static readonly IReleasedNavigationAccess DeniesAll = new FixtureAccess(false);

    private sealed class FixtureAccess(bool allowed) : IReleasedNavigationAccess
    {
        public bool CanRead(ReleasedNavigationDefinition definition, ReleasedNavigationEntry entry, ReleasedEditorDestination destination, ReleasedSurface surface)
            => allowed;
    }
}
