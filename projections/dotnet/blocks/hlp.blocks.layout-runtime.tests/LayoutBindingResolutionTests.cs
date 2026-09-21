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
    [Fact]
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

    [Fact]
    public void TwoRowsResolveInIsolatedScopesAndCrossRowLookupRefuses()
    {
        var resolution = Resolve(Invoice(), Sources());

        var rows = resolution.Blocks.Where(block => block.BlockId == "line-description").ToArray();
        Assert.Equal(2, rows.Length);
        // Distinct values prove the child resolved once per row against that row alone.
        Assert.Equal(["Cement", "Ballast"], rows.Select(row => row.Value?.ToString()));
        Assert.Equal(["line-1", "line-2"], rows.Select(row => row.RowId));

        // A guard reaching for a row while at the surface root cannot see one: fail-closed.
        var crossRow = Resolve(
            Definition(LayoutMedium.Screen, LayoutIntent.Observe,
                Block("stray", new LayoutRecordFieldBinding("supplier"), showWhen: "{\"==\":[{\"var\":\"row.description\"},\"Cement\"]}")),
            Sources());
        Assert.Equal("stray", Assert.Single(crossRow.Hidden));
        Assert.Empty(crossRow.Blocks);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void AGuardThatHoldsPlacesTheBlockThroughTheSharedEvaluator()
    {
        var resolution = Resolve(
            Definition(LayoutMedium.Screen, LayoutIntent.Observe,
                Block("gated", new LayoutRecordFieldBinding("supplier"), showWhen: "{\"==\":[{\"var\":\"field.status\"},\"open\"]}")),
            Sources());

        Assert.Empty(resolution.Hidden);
        Assert.Equal("Northwind", Assert.Single(resolution.Blocks).Value?.ToString());
    }

    [Fact]
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

    private static LayoutBindingResolution Resolve(LayoutDefinition definition, ILayoutBindingSources sources)
        => new LayoutBindingResolver().Resolve(definition, sources, LayoutBindingScope.Root(new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["supplier"] = JsonValue.Create("Northwind"),
            ["status"] = JsonValue.Create("open"),
        }));

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
            return Fields.TryGetValue(fieldPath, out value) || Related.TryGetValue(fieldPath, out value);
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

        public bool TryResolveRelated(LayoutBindingScope scope, string relationship, out LayoutBindingScope related)
        {
            related = relationship == "invoice.supplier"
                ? new LayoutBindingScope(null, null, Related)
                : default;
            return relationship == "invoice.supplier";
        }
    }
}
