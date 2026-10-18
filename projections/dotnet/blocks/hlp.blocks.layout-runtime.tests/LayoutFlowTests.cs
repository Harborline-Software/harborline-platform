using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.LayoutRuntime;
using Harborline.Contracts.Authorization;
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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingBindingKindInAPublishedRootOrDescendantRefusesBeforeFlow(int depth)
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "invoice");
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Layout] = (_, _) => [],
        });
        var root = JsonNode.Parse(LayoutDefinitionJson.SerializeCanonical(ValidScreenDefinition()))!;
        var block = root["blocks"]![0]!;
        for (var index = 0; index < depth; index++)
        {
            var child = block.DeepClone();
            child["id"] = "nested-" + index;
            child["children"] = new JsonArray();
            block["container"] = JsonNode.Parse("{\"kind\":\"stack\"}");
            block["children"] = new JsonArray(child);
            block = child;
        }
        Assert.True(block["binding"]!.AsObject().Remove("binding_kind"));
        await store.SaveDraftAsync(new(key, "version-bad", "1.0.0", root.ToJsonString()), 0, "draft-bad");
        await store.PublishAsync(key, "version-bad", 1, "publish-bad");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(key, "version-bad"), GrantsAllAuthor.Instance, GrantsAllAuthor.Instance));

        Assert.Equal("layout.persisted_body_invalid", exception.Message);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task AnUnrelatedOpenOperationFailurePropagatesTheOriginalInstance()
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Layout] = (_, _) => [],
        });
        var failure = new NotSupportedException("operational-marker");
        var reader = new FailingOpenAccess(failure);

        var observed = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await new LayoutPublishedSurfaceResolver(store).ResolveAsync(
                new(new("tenant-a", DefinitionKind.Layout, "invoice"), "version-1"), reader, GrantsAllAuthor.Instance));

        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task ANullProviderBodyFailsInsideTheDecodeTryWithoutBeingWrapped()
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "invoice");
        var binding = new DefinitionBinding(key, "version-corrupt");
        // Deliberately corrupt provider data: the reference store rejects null JSON at write time.
        // The provider returns a public record; Encoding inside the resolver creates the exception.
        var revision = new DefinitionRevision(new(key, "version-corrupt", "1.0.0", null!),
            2, DefinitionStatus.Published, "");
        var store = new ReturnedRevisionStore(revision);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await new LayoutPublishedSurfaceResolver(store).ResolveAsync(binding, GrantsAllAuthor.Instance, GrantsAllAuthor.Instance));

        Assert.Equal("s", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.Equal(1, store.ResolveCalls);
        Assert.Equal(binding, store.RequestedBinding);
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
            new("statement", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration, provenance, "standard", false, [], new(1, 0)),
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
            new("statement", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration, provenance, "standard", false, [], new(1, 0)),
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
            new("statement", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration, provenance, "standard", false, [], new(1, 0)),
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OutOfOrderBindingKindInAStoredPublishedBodyRefusesBeforeFlow(int depth)
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "invoice");
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Layout] = (_, _) => [],
        });
        var body = BindingOrderBody(depth, discriminatorFirst: false);
        await store.SaveDraftAsync(new(key, "version-order-bad", "1.0.0", body), 0, "draft-order-bad");
        await store.PublishAsync(key, "version-order-bad", 1, "publish-order-bad");
        var binding = new DefinitionBinding(key, "version-order-bad");

        var published = await store.ResolvePublishedAsync(binding);
        Assert.NotNull(published);
        Assert.Equal(body, published.Document.BodyJson);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new LayoutPublishedSurfaceResolver(store).ResolveAsync(binding, GrantsAllAuthor.Instance, GrantsAllAuthor.Instance));

        Assert.Equal("layout.persisted_body_invalid", exception.Message);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FirstBindingKindInAnAuthoredPublishedBodyStillResolves(int depth)
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "invoice");
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Layout] = (_, _) => [],
        });
        var body = BindingOrderBody(depth, discriminatorFirst: true);
        await store.SaveDraftAsync(new(key, "version-order-good", "1.0.0", body), 0, "draft-order-good");
        await store.PublishAsync(key, "version-order-good", 1, "publish-order-good");

        var resolved = await new LayoutPublishedSurfaceResolver(store).ResolveAsync(
            new(key, "version-order-good"), GrantsAllAuthor.Instance, GrantsAllAuthor.Instance);

        Assert.Equal("version-order-good", resolved.VersionId);
        Assert.Equal("invoice", resolved.Definition.Envelope.Identity);
        Assert.Equal("screen", resolved.Plan.Fragmentainer.Kind);
        var block = Assert.Single(resolved.Definition.Blocks);
        for (var index = 0; index < depth; index++) block = Assert.Single(block.Children);
        Assert.Equal("leaf", block.Id);
        Assert.Equal("control", Assert.IsType<LayoutStaticBinding>(block.Binding).Content.GetString());
    }

    private static string BindingOrderBody(int depth, bool discriminatorFirst)
    {
        // Independently authored member order is the Q16 oracle; canonical serialization must
        // not manufacture the negative input. The real shared store retains these exact bytes.
        const string document = """
            {"envelope":{"identity":"invoice","version":"1.0.0","tenant":"tenant-a",
            "cascade_layer":"domain_package","provenance":{},"retention_class":"standard",
            "legal_hold":false,"requires":[{"capability":"platform.layout","minimum_platform_version":"1.0.0"}],
            "contract":{"major":1,"minor":0}},"schema_version":1,"medium":"screen","default_intent":"observe",
            "blocks":[BLOCK],"page_layouts":[],"page_masters":[],"page_runs":[],"drill_through_targets":[]}
            """;
        var binding = discriminatorFirst
            ? """{"binding_kind":"static","content":"control"}"""
            : """{"content":"control","binding_kind":"static"}""";
        var block = "{\"id\":\"leaf\",\"kind\":\"layout.text\",\"binding\":" + binding
            + ",\"children\":[],\"container\":{\"kind\":\"stack\"}}";
        for (var index = 0; index < depth; index++)
            block = "{\"id\":\"parent-" + index
                + "\",\"kind\":\"layout.text\",\"binding\":{\"binding_kind\":\"static\",\"content\":\"parent\"},\"children\":["
                + block + "],\"container\":{\"kind\":\"stack\"}}";
        return document.Replace("BLOCK", block, StringComparison.Ordinal);
    }

    private static LayoutPageRegistry PackPages() => new(
        [new("pack.letter", "letter", LayoutPageOrientation.Portrait, new("sm", "sm", "sm", "sm"), new("sm", "sm"))],
        [new("pack.master", "pack.letter", new(null, "first.center", null), new(null, "left.center", null), new(null, "right.center", null))]);

    private static LayoutDefinition Definition(LayoutMedium medium) => new(
        new("invoice", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration,
            JsonSerializer.SerializeToElement(new { source = "test" }), "standard", false, [], new(1, 0)),
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

    private sealed class ReturnedRevisionStore(DefinitionRevision revision) : IVersionedDefinitionStore
    {
        public int ResolveCalls { get; private set; }
        public DefinitionBinding? RequestedBinding { get; private set; }

        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCalls++;
            RequestedBinding = binding;
            return ValueTask.FromResult<DefinitionRevision?>(revision);
        }

        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind,
            CancellationToken cancellationToken = default) => throw Unexpected();
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default) => throw Unexpected();
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default) => throw Unexpected();
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
            string draftVersionId, string draftVersion, long expectedRevision, string requestId,
            CancellationToken cancellationToken = default) => throw Unexpected();
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key,
            CancellationToken cancellationToken = default) => throw Unexpected();
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key,
            CancellationToken cancellationToken = default) => throw Unexpected();

        private static Exception Unexpected()
            => new InvalidOperationException("Only exact published resolution is used by this provider-corruption fixture.");
    }

    private sealed class FailingOpenAccess(NotSupportedException failure) : ILayoutAccess
    {
        public bool CanAuthor() => true;
        public bool CanPublish() => true;
        public bool CanRead(LayoutBinding binding) => true;
        public bool CanOpen(string surfaceId) => throw failure;
    }
}

/// <summary>A principal who may read every source, open every surface and submit; T-583's checks are proved in LayoutAuthorizationTests and LayoutAuthorityGateTests.</summary>
internal sealed class GrantsAllAuthor : ILayoutAccess, ILayoutSubmitAccess
{
    public static readonly GrantsAllAuthor Instance = new();

    public bool CanAuthor() => true;

    public bool CanPublish() => true;

    public bool CanRead(LayoutBinding binding) => true;

    public bool CanOpen(string surfaceId) => true;

    public bool Satisfies(SubmitGate gate) => true;

    public bool CanWrite() => true;
}
