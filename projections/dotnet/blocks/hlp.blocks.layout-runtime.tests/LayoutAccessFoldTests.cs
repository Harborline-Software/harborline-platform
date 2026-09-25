using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.RuleEngine;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

/// <summary>
/// T-583's render half of "a block never widens the read" (DES-0052 layout-eng-15, §6 and the §9
/// row "A block never widens the read"): the reader's Access is folded in before a query or
/// measure is read, so a denied source is never read or counted and its block renders empty.
/// </summary>
public sealed class LayoutAccessFoldTests
{
    [Fact(DisplayName = "layout-eng-15: a query or measure the reader may not read is never read or counted, and its block renders empty")]
    public void ADeniedQueryOrMeasureIsNeverReadAndItsBlockRendersEmpty()
    {
        var sources = new RecordingSources();
        var resolution = Resolve(Dashboard(), sources, new ReaderAccess(unreadable: ["views.payroll", "payroll.total"]));

        // Nothing was asked of the source for the denied bindings: no read, so no row and no count.
        Assert.Equal(["query:views.orders", "measure:orders.total"], sources.Reads);
        // The blocks still place, empty, and nothing tells the reader a read was denied.
        Assert.Empty(resolution.Refusals);
        Assert.Equal(["orders", "payroll", "order-total", "payroll-total"], resolution.Blocks.Select(block => block.BlockId));
        Assert.Null(Value(resolution, "payroll"));
        Assert.Null(Value(resolution, "payroll-total"));
        Assert.Equal("5", Value(resolution, "orders")?.ToString());
        Assert.Equal("1200", Value(resolution, "order-total")?.ToString());
    }

    [Fact(DisplayName = "layout-eng-15: a grant narrowed after publish empties a repeating query block without reading its rows")]
    public void AGrantNarrowedAfterPublishEmptiesARepeatingBlockWithoutReadingItsRows()
    {
        var definition = Surface(new LayoutBlock("lines", "layout.table", new LayoutQueryBinding("views.lines"),
            [new LayoutBlock("cost", "layout.text", new LayoutRecordFieldBinding("cost"), [])],
            Container: new LayoutContainer(LayoutContainerKind.Stack), Repeating: true));
        // The author could read every source when the surface was published.
        LayoutDefinitionAdmission.ValidateForPublish(definition,
            new LayoutHostRegisters(LayoutBlockKindRegistry.Platform, Access: new ReaderAccess()));

        var sources = new RecordingSources();
        var narrowed = Resolve(definition, sources, new ReaderAccess(unreadable: ["views.lines"]));

        Assert.Empty(sources.Reads);
        Assert.Empty(narrowed.Refusals);
        Assert.Equal(["lines"], narrowed.Blocks.Select(block => block.BlockId));

        // The same surface, for a reader who still holds the grant, reads and places every row.
        var granted = Resolve(definition, new RecordingSources(), new ReaderAccess());
        Assert.Equal(["lines", "cost", "cost"], granted.Blocks.Select(block => block.BlockId));
    }

    [Fact(DisplayName = "layout-eng-15: resolution refuses to run without the reader's Access")]
    public void ResolutionRefusesToRunWithoutTheReadersAccess()
        => Assert.Throws<ArgumentNullException>(() => Resolve(Dashboard(), new RecordingSources(), null!));

    private static LayoutBindingResolution Resolve(LayoutDefinition definition, ILayoutBindingSources sources, ILayoutAccess access)
        => new LayoutBindingResolver(new GuardEvaluator(TimeProvider.System)).Resolve(
            definition, sources, LayoutBindingScope.Root(new Dictionary<string, JsonNode?>(StringComparer.Ordinal)),
            new NoTrace(), new LayoutResolutionRequest("request-1", "principal.clerk-4"), access);

    private static JsonNode? Value(LayoutBindingResolution resolution, string blockId)
        => resolution.Blocks.Single(block => block.BlockId == blockId).Value;

    private static LayoutDefinition Dashboard() => Surface(
        new LayoutBlock("orders", "layout.list", new LayoutQueryBinding("views.orders"), []),
        new LayoutBlock("payroll", "layout.list", new LayoutQueryBinding("views.payroll"), []),
        new LayoutBlock("order-total", "layout.metric", new LayoutMeasureBinding("orders.total"), []),
        new LayoutBlock("payroll-total", "layout.metric", new LayoutMeasureBinding("payroll.total"), []));

    private static LayoutDefinition Surface(params LayoutBlock[] blocks) => new(
        new("surface.dashboard", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration,
            JsonSerializer.SerializeToElement(new { source = "t-583" }), "standard", false,
            [new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "1.0.0")]),
        1, LayoutMedium.Screen, LayoutIntent.Observe, blocks, [], [], [], null, []);

    /// <summary>The reader's Access, as the host builds it for one principal at one instant.</summary>
    private sealed class ReaderAccess(IEnumerable<string>? unreadable = null) : ILayoutAccess
    {
        private readonly HashSet<string> _unreadable = new(unreadable ?? [], StringComparer.Ordinal);

        public bool CanRead(LayoutBinding binding) => !_unreadable.Contains(binding switch
        {
            LayoutQueryBinding value => value.ViewDefinitionId,
            LayoutMeasureBinding value => value.MeasurePath,
            LayoutRecordFieldBinding value => value.FieldPath,
            LayoutTemplateBinding value => value.TemplateDefinitionId,
            _ => string.Empty,
        });

        public bool CanOpen(string surfaceId) => true;
    }

    /// <summary>A host source that records every set-scoped read Layout asks of it.</summary>
    private sealed class RecordingSources : ILayoutBindingSources
    {
        public List<string> Reads { get; } = [];

        public bool TryResolveField(LayoutBindingScope scope, string fieldPath, out JsonNode? value)
            => scope.Values.TryGetValue(fieldPath, out value);

        public bool TryResolveQuery(LayoutBindingScope scope, string viewDefinitionId, out JsonNode? value)
        {
            Reads.Add($"query:{viewDefinitionId}");
            value = JsonValue.Create(viewDefinitionId == "views.payroll" ? 12 : 5);
            return true;
        }

        public bool TryResolveMeasure(LayoutBindingScope scope, string measurePath, out JsonNode? value)
        {
            Reads.Add($"measure:{measurePath}");
            value = JsonValue.Create(measurePath == "payroll.total" ? 98000 : 1200);
            return true;
        }

        public bool TryResolveTemplate(LayoutBindingScope scope, string templateDefinitionId, out JsonNode? value)
        {
            value = null;
            return false;
        }

        public bool TryResolveCollection(LayoutBindingScope scope, string name, out IReadOnlyList<JsonNode?> rows)
        {
            Reads.Add($"collection:{name}");
            rows = [new JsonObject { ["id"] = "line-1", ["cost"] = 40 }, new JsonObject { ["id"] = "line-2", ["cost"] = 60 }];
            return true;
        }

        public LayoutRelatedResult ResolveRelated(LayoutBindingScope scope, string relationship) => LayoutRelatedResult.Undeclared;
    }

    private sealed class NoTrace : ILayoutDecisionTrace
    {
        public void RecordDenial(LayoutRelatedDenial denial) => throw new InvalidOperationException("No related binding is denied here.");
    }
}
