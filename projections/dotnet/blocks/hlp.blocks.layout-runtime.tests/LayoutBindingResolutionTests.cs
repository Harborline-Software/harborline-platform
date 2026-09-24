using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.LayoutRuntime;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

/// <summary>
/// T-582's binding half, proved against the DES-0052 rows it owns: layout-eng-14 (resolve by
/// name, refuse an unresolvable one), layout-eng-16 (fail-closed guards through the shared
/// engine), layout-eng-17 and layout-run-2 (a fresh scope per repeating row), layout-run-4 (a
/// refusal naming block and kind), and layout-eng-28/29 (Layout places a field or measure
/// result without owning capture or compute).
/// </summary>
public sealed class LayoutBindingResolutionTests
{
    [Fact(DisplayName = "layout-eng-28, layout-eng-29: a mixed-binding invoice places a field, a query, a measure and repeated rows")]
    public void MixedBindingInvoicePlacesFieldQueryMeasureAndRepeatedRows()
    {
        var resolution = Resolve(Invoice(), Sources());

        Assert.Empty(resolution.Refusals);
        Assert.Equal(
            ["heading", "supplier", "open-queue", "invoice-total", "lines", "line-description", "line-description"],
            resolution.Blocks.Select(block => block.BlockId));

        // Each kind is named by its authored discriminator, so a consumer can tell them apart.
        Assert.Equal(LayoutBindingKinds.Static, Block(resolution, "heading").BindingKind);
        Assert.Equal(LayoutBindingKinds.RecordField, Block(resolution, "supplier").BindingKind);
        Assert.Equal(LayoutBindingKinds.Query, Block(resolution, "open-queue").BindingKind);
        Assert.Equal(LayoutBindingKinds.Measure, Block(resolution, "invoice-total").BindingKind);

        // Layout places the source's result verbatim: it captured nothing and computed nothing.
        Assert.Equal("Northwind", Block(resolution, "supplier").Value?.ToString());
        Assert.Equal("412.5", Block(resolution, "invoice-total").Value?.ToString());
        Assert.Equal("Invoice", Block(resolution, "heading").Value?.ToString());
    }

    [Fact]
    public void TemplateBindingResolvesOnAPageRunAndStaticContentStaysAuthoredOnTheBlock()
    {
        var resolution = Resolve(
            Definition(LayoutMedium.Page, LayoutIntent.Issue,
                Block("letter", new LayoutTemplateBinding("tpl.remittance")),
                Block("notice", new LayoutStaticBinding(Json("\"Registered office: Leeds\"")))),
            Sources());

        Assert.Empty(resolution.Refusals);
        Assert.Equal("tpl.remittance", Block(resolution, "letter").Name);
        Assert.Equal(LayoutBindingKinds.Template, Block(resolution, "letter").BindingKind);
        // Static content is authored, never looked up: no source was consulted for it.
        Assert.Equal("Registered office: Leeds", Block(resolution, "notice").Value?.ToString());
    }

    [Fact(DisplayName = "layout-eng-17, layout-run-2, layout-eng-9 successor of the Documents walker (DocumentRenderWalker.cs:113-117): each row resolves in a fresh scope and cross-row lookup refuses")]
    public void TwoRowsResolveInIsolatedScopesAndCrossRowLookupRefuses()
    {
        var resolution = Resolve(Invoice(), Sources());

        var rows = resolution.Blocks.Where(block => block.BlockId == "line-description").ToArray();
        Assert.Equal(2, rows.Length);
        // Distinct values prove the child resolved once per row against that row alone.
        Assert.Equal(["Cement", "Ballast"], rows.Select(row => row.Value?.ToString()));
        Assert.Equal(["line-1", "line-2"], rows.Select(row => row.RowId));

        // A guard reaching for a row while at the surface root cannot see one: fail-closed. The root
        // carries a field of the same name, so a row reference that leaked to the root would hold.
        var crossRow = new LayoutBindingResolver(new Harborline.Foundation.RuleEngine.GuardEvaluator(TimeProvider.System)).Resolve(
            Definition(LayoutMedium.Screen, LayoutIntent.Observe,
                Block("stray", new LayoutRecordFieldBinding("supplier"), showWhen: "{\"==\":[{\"var\":\"row.description\"},\"Cement\"]}")),
            Sources(),
            LayoutBindingScope.Root(new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["description"] = JsonValue.Create("Cement") }),
            new RecordingTrace(), new LayoutResolutionRequest("request-1", "principal.clerk-4"));
        Assert.Equal("stray", Assert.Single(crossRow.Hidden));
        Assert.Empty(crossRow.Blocks);
    }

    [Fact(DisplayName = "layout-eng-14, layout-run-4: a missing binding yields one refusal naming the block and kind, and its counterpart resolves exactly once")]
    public void AMissingBindingYieldsOneRefusalNamingTheBlockAndKindWhileItsCounterpartResolvesExactlyOnce()
    {
        var resolution = Resolve(
            Definition(LayoutMedium.Screen, LayoutIntent.Observe,
                Block("good-total", new LayoutMeasureBinding("invoice.total")),
                Block("bad-total", new LayoutMeasureBinding("invoice.absent"))),
            Sources());

        var refusal = Assert.Single(resolution.Refusals);
        Assert.Equal("bad-total", refusal.BlockId);
        Assert.Equal(LayoutBindingKinds.Measure, refusal.BindingKind);
        Assert.Equal("invoice.absent", refusal.Name);
        Assert.Contains("bad-total", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(LayoutBindingKinds.Measure, refusal.Message, StringComparison.Ordinal);

        // One bad name does not blank the surface: the counterpart still resolved, once.
        Assert.Equal("good-total", Assert.Single(resolution.Blocks).BlockId);
    }

    [Fact(DisplayName = "layout-eng-14, layout-run-4: every binding kind refuses by its own name when the source cannot resolve it")]
    public void EveryBindingKindRefusesByItsOwnNameWhenTheSourceCannotResolveIt()
    {
        var resolution = Resolve(
            Definition(LayoutMedium.Page, LayoutIntent.Issue,
                Block("f", new LayoutRecordFieldBinding("absent.field")),
                Block("q", new LayoutQueryBinding("views.absent")),
                Block("m", new LayoutMeasureBinding("measure.absent")),
                Block("t", new LayoutTemplateBinding("tpl.absent"))),
            Sources());

        Assert.Empty(resolution.Blocks);
        Assert.Equal(
            [LayoutBindingKinds.RecordField, LayoutBindingKinds.Query, LayoutBindingKinds.Measure, LayoutBindingKinds.Template],
            resolution.Refusals.Select(refusal => refusal.BindingKind));
        Assert.Equal(["absent.field", "views.absent", "measure.absent", "tpl.absent"], resolution.Refusals.Select(refusal => refusal.Name));
    }

    [Fact(DisplayName = "layout-eng-14, layout-run-4: an unresolvable collection refuses once rather than per row")]
    public void AnUnresolvableCollectionRefusesOnceRatherThanPerRow()
    {
        var repeating = new LayoutBlock("absent-lines", "layout.table", new LayoutQueryBinding("views.absent"), [
            Block("cell", new LayoutRecordFieldBinding("description")),
        ], Container: new(LayoutContainerKind.Stack), Repeating: true);

        var resolution = Resolve(Definition(LayoutMedium.Screen, LayoutIntent.Observe, repeating), Sources());

        var refusal = Assert.Single(resolution.Refusals);
        Assert.Equal("absent-lines", refusal.BlockId);
        Assert.Equal(LayoutBindingKinds.Query, refusal.BindingKind);
        Assert.Empty(resolution.Blocks);
    }

    [Theory(DisplayName = "layout-ck-40: a collection at each effective bound renders completely")]
    [InlineData(2, 5)]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    public void ACollectionAtEachEffectiveBoundRendersCompletely(int minimum, int maximum)
    {
        var resolution = Resolve(Definition(LayoutMedium.Screen, LayoutIntent.Observe, Lines(new(minimum, maximum))), Sources());

        Assert.Empty(resolution.Refusals);
        Assert.Equal(["line-1", "line-2"], resolution.Blocks.Where(block => block.BlockId == "cell").Select(block => block.RowId));
    }

    [Theory(DisplayName = "layout-ck-40: runtime cardinality outside the bounds refuses the whole block and returns no partial row set")]
    [InlineData(3, 5)]
    [InlineData(0, 1)]
    public void RuntimeCardinalityOutsideTheBoundsRefusesWithNoPartialRows(int minimum, int maximum)
    {
        var resolution = Resolve(
            Definition(LayoutMedium.Screen, LayoutIntent.Observe, Lines(new(minimum, maximum)), Block("after", new LayoutRecordFieldBinding("supplier"))),
            Sources());

        var refusal = Assert.Single(resolution.Refusals);
        Assert.Equal("lines", refusal.BlockId);
        Assert.Equal(LayoutBindingRefusalCodes.CollectionOutOfBounds, refusal.Code);
        // Neither the container nor any row placed: nothing truncated, nothing partial.
        Assert.DoesNotContain(resolution.Blocks, block => block.BlockId is "lines" or "cell");
        // The rest of the surface still resolves.
        Assert.Equal("after", Assert.Single(resolution.Blocks).BlockId);
    }

    private static LayoutBlock Lines(LayoutCollectionBounds bounds) => new(
        "lines", "layout.table", new LayoutQueryBinding("views.invoice-lines"),
        [Block("cell", new LayoutRecordFieldBinding("description"))],
        Container: new(LayoutContainerKind.Stack), Repeating: true, CollectionBounds: bounds);

    [Fact(DisplayName = "layout-eng-16, layout-eng-9 successor of the Documents walker (DocumentBlock.cs:42): a per-block guard fails closed per row")]
    public void APerRowGuardWithholdsOnlyTheRowItFailsFor()
    {
        var repeating = new LayoutBlock("lines", "layout.table", new LayoutQueryBinding("views.invoice-lines"), [
            Block("cell", new LayoutRecordFieldBinding("description"), showWhen: "{\"==\":[{\"var\":\"row.description\"},\"Cement\"]}"),
        ], Container: new(LayoutContainerKind.Stack), Repeating: true);

        var resolution = Resolve(Definition(LayoutMedium.Screen, LayoutIntent.Observe, repeating), Sources());

        var cells = resolution.Blocks.Where(block => block.BlockId == "cell").ToArray();
        Assert.Equal("Cement", Assert.Single(cells).Value?.ToString());
        Assert.Equal("cell", Assert.Single(resolution.Hidden));
        Assert.Empty(resolution.Refusals);
    }

    [Fact(DisplayName = "layout-eng-16: a guard fails closed on an unknown reference and on a malformed expression")]
    public void AGuardFailsClosedOnAnUnknownReferenceAndOnAMalformedExpression()
    {
        foreach (var expression in new[]
        {
            "{\"==\":[{\"var\":\"field.nothing-here\"},1]}",
            "{\"not-an-operator\":[1]}",
            "{",
        })
        {
            var resolution = Resolve(
                Definition(LayoutMedium.Screen, LayoutIntent.Observe,
                    Block("gated", new LayoutRecordFieldBinding("supplier"), showWhen: expression)),
                Sources());

            Assert.Equal("gated", Assert.Single(resolution.Hidden));
            Assert.Empty(resolution.Blocks);
        }
    }

    [Fact(DisplayName = "layout-eng-16: a guard that holds places the block through the shared evaluator")]
    public void AGuardThatHoldsPlacesTheBlockThroughTheSharedEvaluator()
    {
        var resolution = Resolve(
            Definition(LayoutMedium.Screen, LayoutIntent.Observe,
                Block("gated", new LayoutRecordFieldBinding("supplier"), showWhen: "{\"==\":[{\"var\":\"field.status\"},\"open\"]}")),
            Sources());

        Assert.Empty(resolution.Hidden);
        Assert.Equal("Northwind", Assert.Single(resolution.Blocks).Value?.ToString());
    }

    [Fact(DisplayName = "layout-auth-19 (runtime): a related block observes the second record and an undeclared relationship refuses")]
    public void ARelatedBlockObservesTheSecondRecordAndRefusesAnUndeclaredRelationship()
    {
        var related = new LayoutBlock("supplier-card", "layout.list", new LayoutStaticBinding(Json("\"Supplier\"")), [
            Block("supplier-name", new LayoutRecordFieldBinding("name")),
        ], RelatedRelationship: "invoice.supplier");

        var resolution = Resolve(Definition(LayoutMedium.Screen, LayoutIntent.Observe, related), Sources());
        Assert.Empty(resolution.Refusals);
        // The child read the related record's field, not the invoice's.
        Assert.Equal("Northwind Aggregates Ltd", Block(resolution, "supplier-name").Value?.ToString());

        var undeclared = new LayoutBlock("stray-card", "layout.list", new LayoutStaticBinding(Json("\"x\"")), [],
            RelatedRelationship: "invoice.not-declared");
        var refused = Resolve(Definition(LayoutMedium.Screen, LayoutIntent.Observe, undeclared), Sources());
        Assert.Equal("invoice.not-declared", Assert.Single(refused.Refusals).Name);
    }

    [Fact(DisplayName = "layout-eng-31, layout-run-5: a missing and a denied related target are identical absence; only the protected trace sees the denial")]
    public void AMissingAndADeniedRelatedTargetAreIdenticalAbsenceAndOnlyTheTraceSeesTheDenial()
    {
        var definition = Definition(LayoutMedium.Screen, LayoutIntent.Observe,
            new LayoutBlock("owner-card", "layout.list", new LayoutStaticBinding(Json("\"Owner\"")), [
                Block("owner-name", new LayoutRecordFieldBinding("name")),
            ], RelatedRelationship: "invoice.owner"),
            Block("supplier", new LayoutRecordFieldBinding("supplier")));
        var missingTrace = new RecordingTrace();
        var deniedTrace = new RecordingTrace();

        var request = new LayoutResolutionRequest("request-7", "principal.clerk-4");
        var owner = new LayoutRecordReference("record-type.party", "party-19");
        var missing = Resolve(definition, new RelatedOutcomeSources(LayoutRelatedResult.Absent), missingTrace, request);
        var denied = Resolve(definition, new RelatedOutcomeSources(LayoutRelatedResult.Denied("access.denied", "/grants/owner", owner)), deniedTrace, request);

        // What the viewer receives cannot tell the two apart: no refusal, no marker, same blocks.
        Assert.Equal(Describe(missing), Describe(denied));
        Assert.Empty(denied.Refusals);
        Assert.Equal(["supplier"], denied.Blocks.Select(block => block.BlockId));

        Assert.Empty(missingTrace.Denials);
        Assert.Equal(
            new LayoutRelatedDenial("request-7", "principal.clerk-4", "owner-card", LayoutBindingKinds.Static, "invoice.owner", owner, "access.denied", "/grants/owner"),
            Assert.Single(deniedTrace.Denials));
    }

    [Theory(DisplayName = "layout-run-5: resolution refuses to run without the request and principal that key denial evidence")]
    [InlineData("", "principal.clerk-4")]
    [InlineData(" ", "principal.clerk-4")]
    [InlineData("request-7", "")]
    public void ResolutionRefusesToRunWithoutARequestIdentity(string requestId, string principalId)
    {
        Assert.Throws<ArgumentException>(() => Resolve(Invoice(), Sources(), new RecordingTrace(), new LayoutResolutionRequest(requestId, principalId)));
    }

    [Fact(DisplayName = "layout-run-5: a source that reports a denial without its evidence faults rather than losing it")]
    public void ADenialWithoutEvidenceFaults()
    {
        var definition = Definition(LayoutMedium.Screen, LayoutIntent.Observe,
            new LayoutBlock("owner-card", "layout.list", new LayoutStaticBinding(Json("\"Owner\"")), [], RelatedRelationship: "invoice.owner"));

        Assert.Throws<InvalidOperationException>(() => Resolve(definition,
            new RelatedOutcomeSources(new LayoutRelatedResult(LayoutRelatedOutcome.Denied)), new RecordingTrace(),
            new LayoutResolutionRequest("request-7", "principal.clerk-4")));
    }

    private static string Describe(LayoutBindingResolution resolution) => JsonSerializer.Serialize(new
    {
        blocks = resolution.Blocks.Select(block => new { block.BlockId, block.BindingKind, block.Name, Value = block.Value?.ToJsonString(), block.RowId }),
        resolution.Refusals,
        resolution.Hidden,
    });

    private static LayoutBindingResolution Resolve(LayoutDefinition definition, ILayoutBindingSources sources, ILayoutDecisionTrace trace, LayoutResolutionRequest request)
        => new LayoutBindingResolver(new Harborline.Foundation.RuleEngine.GuardEvaluator(TimeProvider.System))
            .Resolve(definition, sources, LayoutBindingScope.Root(new Dictionary<string, JsonNode?>(StringComparer.Ordinal)), trace, request);

    private sealed class RecordingTrace : ILayoutDecisionTrace
    {
        public List<LayoutRelatedDenial> Denials { get; } = [];

        public void RecordDenial(LayoutRelatedDenial denial) => Denials.Add(denial);
    }

    private sealed class RelatedOutcomeSources(LayoutRelatedResult outcome) : ILayoutBindingSources
    {
        private readonly FixtureSources _fixture = new();

        public bool TryResolveField(LayoutBindingScope scope, string fieldPath, out JsonNode? value) => _fixture.TryResolveField(scope, fieldPath, out value);
        public bool TryResolveQuery(LayoutBindingScope scope, string viewDefinitionId, out JsonNode? value) => _fixture.TryResolveQuery(scope, viewDefinitionId, out value);
        public bool TryResolveMeasure(LayoutBindingScope scope, string measurePath, out JsonNode? value) => _fixture.TryResolveMeasure(scope, measurePath, out value);
        public bool TryResolveTemplate(LayoutBindingScope scope, string templateDefinitionId, out JsonNode? value) => _fixture.TryResolveTemplate(scope, templateDefinitionId, out value);
        public bool TryResolveCollection(LayoutBindingScope scope, string name, out IReadOnlyList<JsonNode?> rows) => _fixture.TryResolveCollection(scope, name, out rows);
        public LayoutRelatedResult ResolveRelated(LayoutBindingScope scope, string relationship) => outcome;
    }

    private static LayoutBindingResolution Resolve(LayoutDefinition definition, ILayoutBindingSources sources)
        => new LayoutBindingResolver(new Harborline.Foundation.RuleEngine.GuardEvaluator(TimeProvider.System)).Resolve(definition, sources, LayoutBindingScope.Root(new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["supplier"] = JsonValue.Create("Northwind"),
            ["status"] = JsonValue.Create("open"),
        }), new RecordingTrace(), new LayoutResolutionRequest("request-1", "principal.clerk-4"));

    private static LayoutResolvedBlock Block(LayoutBindingResolution resolution, string id)
        => resolution.Blocks.First(block => block.BlockId == id);

    private static ILayoutBindingSources Sources() => new FixtureSources();

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static LayoutBlock Block(string id, LayoutBinding binding, string? showWhen = null)
        => new(id, "layout.list", binding, [], ShowWhen: showWhen);

    private static LayoutDefinition Definition(LayoutMedium medium, LayoutIntent intent, params LayoutBlock[] blocks)
        => new(new("invoice", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration, JsonSerializer.SerializeToElement(new { source = "t-582" }), "standard", false, []), 1, medium, intent, blocks, [], [], [], null, []);

    private static LayoutDefinition Invoice() => Definition(LayoutMedium.Screen, LayoutIntent.Observe,
        Block("heading", new LayoutStaticBinding(Json("\"Invoice\""))),
        Block("supplier", new LayoutRecordFieldBinding("supplier")),
        Block("open-queue", new LayoutQueryBinding("views.open-invoices")),
        Block("invoice-total", new LayoutMeasureBinding("invoice.total")),
        new LayoutBlock("lines", "layout.table", new LayoutQueryBinding("views.invoice-lines"), [
            Block("line-description", new LayoutRecordFieldBinding("description")),
        ], Container: new(LayoutContainerKind.Stack), Repeating: true));

    /// <summary>
    /// The named results a host supplies. Every lookup is a dictionary hit: this fixture never
    /// captures a field, runs a query or computes a measure, which is the point of the
    /// layout-eng-28/29 consumer contract.
    /// </summary>
    private sealed class FixtureSources : ILayoutBindingSources
    {
        private static readonly Dictionary<string, JsonNode?> Fields = new(StringComparer.Ordinal)
        {
            ["supplier"] = JsonValue.Create("Northwind"),
            ["status"] = JsonValue.Create("open"),
        };

        private static readonly Dictionary<string, JsonNode?> Related = new(StringComparer.Ordinal)
        {
            ["name"] = JsonValue.Create("Northwind Aggregates Ltd"),
        };

        public bool TryResolveField(LayoutBindingScope scope, string fieldPath, out JsonNode? value)
        {
            // A row scope answers from its own row, never from the surface root.
            if (scope.IsRow) return scope.Values.TryGetValue(fieldPath, out value);
            if (scope.Values.TryGetValue(fieldPath, out value)) return true;
            // Only the related scope answers the related record's fields, so a resolver that forgot to
            // switch scope cannot pass by reading them from the surface root.
            return Fields.TryGetValue(fieldPath, out value);
        }

        public bool TryResolveQuery(LayoutBindingScope scope, string viewDefinitionId, out JsonNode? value)
        {
            value = viewDefinitionId switch
            {
                "views.open-invoices" => JsonValue.Create(3),
                "views.invoice-lines" => JsonValue.Create(2),
                _ => null,
            };
            return value is not null;
        }

        public bool TryResolveMeasure(LayoutBindingScope scope, string measurePath, out JsonNode? value)
        {
            value = measurePath == "invoice.total" ? JsonValue.Create(412.5m) : null;
            return value is not null;
        }

        public bool TryResolveTemplate(LayoutBindingScope scope, string templateDefinitionId, out JsonNode? value)
        {
            value = templateDefinitionId == "tpl.remittance" ? JsonValue.Create("remittance") : null;
            return value is not null;
        }

        public bool TryResolveCollection(LayoutBindingScope scope, string name, out IReadOnlyList<JsonNode?> rows)
        {
            if (name != "views.invoice-lines")
            {
                rows = [];
                return false;
            }
            rows =
            [
                new JsonObject { ["id"] = "line-1", ["description"] = "Cement" },
                new JsonObject { ["id"] = "line-2", ["description"] = "Ballast" },
            ];
            return true;
        }

        public LayoutRelatedResult ResolveRelated(LayoutBindingScope scope, string relationship)
            => relationship == "invoice.supplier"
                ? LayoutRelatedResult.Resolved(new LayoutBindingScope(null, null, Related))
                : LayoutRelatedResult.Undeclared;
    }
}
