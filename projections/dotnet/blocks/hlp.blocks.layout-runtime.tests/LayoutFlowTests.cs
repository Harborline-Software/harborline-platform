using System.Text.Json;
using System.Text;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.LayoutRuntime;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

public sealed class LayoutFlowTests
{
    [Fact]
    public void ScreenAndPageUseOneTreeOrderAndKeepStaticRegionsOutOfFlow()
    {
        var runtime = new LayoutRuntimeEngine();

        var screen = runtime.Flow(Definition(LayoutMedium.Screen));
        var page = runtime.Flow(Definition(LayoutMedium.Page));

        Assert.Equal(["title", "table", "total"], screen.Flow.Select(block => block.BlockId));
        Assert.Equal(screen.Flow.Select(block => block.BlockId), page.Flow.Select(block => block.BlockId));
        Assert.Equal("screen", screen.Fragmentainer.Kind);
        Assert.Equal("page", page.Fragmentainer.Kind);
        Assert.Equal("header", Assert.Single(page.StaticRegions).BlockId);
    }

    [Fact]
    public async Task ResolvesOnlyTheExactPublishedLayoutVersionFromTheSharedStore()
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "invoice");
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Layout] = (_, _) => [],
        });
        var body = Encoding.UTF8.GetString(LayoutDefinitionJson.SerializeCanonical(ValidScreenDefinition()));
        await store.SaveDraftAsync(new(key, "version-1", "1.0.0", body), 0, "draft-1");
        await store.PublishAsync(key, "version-1", 1, "publish-1");

        var plan = await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(key, "version-1"), GrantsAllAuthor.Instance, GrantsAllAuthor.Instance);

        Assert.Equal("invoice", plan.Definition.Envelope.Identity);
        Assert.Equal("version-1", plan.VersionId);
        Assert.Equal("screen", plan.Plan.Fragmentainer.Kind);
    }

    [Fact]
    public async Task RefusesAnInvalidPersistedBodyBeforeTheRuntimeCanFlowIt()
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "invoice");
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Layout] = (_, _) => [],
        });
        var invalid = ValidScreenDefinition() with
        {
            Blocks = [new("table", "layout.table", new LayoutQueryBinding("open-invoices"), [], Placement: new(Span: 0))],
        };
        await store.SaveDraftAsync(new(key, "version-2", "1.0.0", Encoding.UTF8.GetString(LayoutDefinitionJson.SerializeCanonical(invalid))), 0, "draft-2");
        await store.PublishAsync(key, "version-2", 1, "publish-2");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(key, "version-2"), GrantsAllAuthor.Instance, GrantsAllAuthor.Instance));

        Assert.Equal("layout.persisted_body_invalid", exception.Message);
    }

    [Theory]
    [InlineData("layout.table")]
    [InlineData("layout.list")]
    [InlineData("layout.board")]
    [InlineData("layout.calendar")]
    [InlineData("layout.map")]
    [InlineData("layout.dashboard-widget")]
    [InlineData("layout.file-library")]
    public void PlatformRegisterOffersEveryCanonicalLayoutRendererKind(string kind)
        => Assert.True(LayoutBlockKindRegistry.Platform.Contains(kind));

    [Fact]
    public void HelmWidgetCompositionResolvesOnlyRegisteredIdentifiers()
    {
        IHelmWidgetRegistry widgets = new HelmWidgetRegistry([new("revenue-glance", "Revenue glance")]);

        Assert.Equal("Revenue glance", widgets.Resolve("revenue-glance")!.Label);
        Assert.Null(widgets.Resolve("unregistered-widget"));
    }

    [Fact]
    public void ScreenAdmissionRefusesAnUnwrappableMultiColumnFlowAt320Pixels()
    {
        var definition = ValidScreenDefinition() with
        {
            Blocks = [new("flow", "layout.flow", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Flow")), [], Container: new(LayoutContainerKind.Flow, Wrap: LayoutWrap.NoWrap, ColumnCount: 2))],
        };

        var exception = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(definition, GrantsAllAuthor.Instance));

        Assert.Contains(exception.Refusals, refusal => refusal.Code == LayoutDefinitionCodes.ReflowForbidden);
    }

    [Fact]
    public void PageRunsSplitOnAuthoredBreaksAndSelectFirstThenLeftStaticRegions()
    {
        var provenance = JsonSerializer.SerializeToElement(new { source = "test" });
        var definition = new LayoutDefinition(
            new("statement", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration, provenance, "standard", false, []),
            1, LayoutMedium.Page, LayoutIntent.Observe,
            [
                new("first-header", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("First")), [], FlowRole: LayoutFlowRole.Static, StaticRegion: "first.center"),
                new("left-header", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Left")), [], FlowRole: LayoutFlowRole.Static, StaticRegion: "left.center"),
                new("summary", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Summary")), [], BreakAfter: true),
                new("lines", "layout.table", new LayoutQueryBinding("open-lines"), []),
            ],
            [new("letter", "letter", LayoutPageOrientation.Portrait, new("sm", "sm", "sm", "sm"), new("sm", "sm"))],
            [new("master", "letter", new(null, "first.center", null), new(null, "left.center", null), new(null, "right.center", null))],
            [new("run", "letter", "master", ["summary", "lines"])], null, []);

        var fragments = new LayoutRuntimeEngine().Flow(definition).PageFragments!;

        Assert.Equal(2, fragments.Count);
        Assert.Equal(("first", "summary", "first-header"), (fragments[0].Variant, Assert.Single(fragments[0].Flow).BlockId, Assert.Single(fragments[0].StaticRegions).BlockId));
        Assert.Equal(("left", "lines", "left-header"), (fragments[1].Variant, Assert.Single(fragments[1].Flow).BlockId, Assert.Single(fragments[1].StaticRegions).BlockId));
    }

    [Fact(DisplayName = "layout-bound-7: a page run flows through the page layout and master a pack supplies")]
    public void PageRunFlowsThroughThePackSuppliedMaster()
    {
        var provenance = JsonSerializer.SerializeToElement(new { source = "test" });
        var definition = new LayoutDefinition(
            new("statement", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration, provenance, "standard", false, []),
            1, LayoutMedium.Page, LayoutIntent.Observe,
            [
                new("first-header", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("First")), [], FlowRole: LayoutFlowRole.Static, StaticRegion: "first.center"),
                new("summary", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Summary")), []),
            ],
            [], [], [new("run", "pack.letter", "pack.master", ["summary"])], null, []);

        var fragment = Assert.Single(new LayoutRuntimeEngine().Flow(definition, PackPages()).PageFragments!);

        Assert.Equal(("pack.letter", "pack.master", "first"), (fragment.PageLayoutId, fragment.PageMasterId, fragment.Variant));
        Assert.Equal("summary", Assert.Single(fragment.Flow).BlockId);
        Assert.Equal("first-header", Assert.Single(fragment.StaticRegions).BlockId);
    }

    [Fact(DisplayName = "layout-bound-7: a published surface citing a pack master resolves through the host's page register")]
    public async Task PublishedSurfaceCitingAPackMasterResolvesThroughTheHostRegister()
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "statement");
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Layout] = (_, _) => [],
        });
        var provenance = JsonSerializer.SerializeToElement(new { source = "test" });
        var definition = new LayoutDefinition(
            new("statement", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration, provenance, "standard", false, []),
            1, LayoutMedium.Page, LayoutIntent.Observe,
            [new("summary", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Summary")), [])],
            [], [], [new("run", "pack.letter", "pack.master", ["summary"])], null, []);
        await store.SaveDraftAsync(new(key, "version-1", "1.0.0", Encoding.UTF8.GetString(LayoutDefinitionJson.SerializeCanonical(definition))), 0, "draft-1");
        await store.PublishAsync(key, "version-1", 1, "publish-1");

        var resolved = await new LayoutPublishedSurfaceResolver(store, registers: new(LayoutBlockKindRegistry.Platform, Pages: PackPages())).ResolveAsync(new(key, "version-1"), GrantsAllAuthor.Instance, GrantsAllAuthor.Instance);

        Assert.Equal("pack.master", Assert.Single(resolved.Plan.PageFragments!).PageMasterId);
        // The same body without the pack's register cites nothing it can resolve.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(key, "version-1"), GrantsAllAuthor.Instance, GrantsAllAuthor.Instance));
        Assert.Equal("layout.persisted_body_invalid", refused.Message);
    }

    private static LayoutPageRegistry PackPages() => new(
        [new("pack.letter", "letter", LayoutPageOrientation.Portrait, new("sm", "sm", "sm", "sm"), new("sm", "sm"))],
        [new("pack.master", "pack.letter", new(null, "first.center", null), new(null, "left.center", null), new(null, "right.center", null))]);

    private static LayoutDefinition Definition(LayoutMedium medium) => new(
        new("invoice", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration,
            JsonSerializer.SerializeToElement(new { source = "test" }), "standard", false, []),
        1, medium, LayoutIntent.Observe,
        [
            new("title", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Invoice")), [], Container: new(LayoutContainerKind.Stack)),
            new("table", "layout.table", new LayoutQueryBinding("open-invoices"), [new("total", "layout.metric", new LayoutMeasureBinding("invoice.total"), [])]),
            new("header", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Invoice")), [], FlowRole: LayoutFlowRole.Static, StaticRegion: "header.center"),
        ], [], [], [], null, []);

    private static LayoutDefinition ValidScreenDefinition()
    {
        var definition = Definition(LayoutMedium.Screen);
        return definition with
        {
            Blocks = definition.Blocks
                .Where(block => block.FlowRole == LayoutFlowRole.Flow)
                .Select(block => block with { Children = [] })
                .ToArray(),
        };
    }
}

/// <summary>A principal who may read every source, open every surface and submit; T-583's checks are proved in LayoutAuthorizationTests and LayoutAuthorityGateTests.</summary>
internal sealed class GrantsAllAuthor : ILayoutAccess, ILayoutSubmitAccess
{
    public static readonly GrantsAllAuthor Instance = new();

    public bool CanRead(LayoutBinding binding) => true;

    public bool CanOpen(string surfaceId) => true;

    public bool Satisfies(LayoutSubmitGate gate) => true;

    public bool CanWrite() => true;
}
