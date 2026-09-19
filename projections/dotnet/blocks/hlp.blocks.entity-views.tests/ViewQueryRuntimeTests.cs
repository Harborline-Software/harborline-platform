using Harborline.Blocks.EntityViews;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewQueryRuntimeTests
{
    [Fact(DisplayName = "a denied open decision prevents Access evaluation and every row read")]
    public async Task DeniedOpenPreventsAccessAndEveryRowRead()
    {
        var calls = new List<string>();
        var definition = Definition();
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, definition),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: false, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new RecordingRows(calls),
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var error = await Assert.ThrowsAsync<ViewQueryException>(async () =>
            await runtime.ExecuteAsync(Request()));

        Assert.Equal(ViewQueryCodes.OpenForbidden, error.Code);
        Assert.Equal(["resolve", "open"], calls);
    }

    [Fact(DisplayName = "an unknown Layout view kind refuses before Access or rows")]
    public async Task UnknownLayoutViewKindRefusesBeforeAccessOrRows()
    {
        var calls = new List<string>();
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, Definition()),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: false),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new RecordingRows(calls),
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var error = await Assert.ThrowsAsync<ViewQueryException>(async () =>
            await runtime.ExecuteAsync(Request()));

        Assert.Equal(ViewDefinitionCodes.KindUnknown, error.Code);
        Assert.Equal(["resolve", "open", "kind"], calls);
    }

    [Fact(DisplayName = "a required shape role whose field is missing refuses before Access or rows")]
    public async Task MissingShapeRoleFieldRefusesBeforeAccessOrRows()
    {
        var calls = new List<string>();
        var request = Request() with
        {
            Binding = new(
                Kind: "layout.timeline",
                ShapeRoles: new Dictionary<ViewShapeRole, string>
                {
                    [ViewShapeRole.Title] = "title",
                    [ViewShapeRole.PlacedBy] = "missing_at",
                }),
        };
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, Definition()),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true, RequiredRoles: [ViewShapeRole.Title, ViewShapeRole.PlacedBy]),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new RecordingRows(calls),
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var error = await Assert.ThrowsAsync<ViewQueryException>(async () =>
            await runtime.ExecuteAsync(request));

        Assert.Equal(ViewDefinitionCodes.ShapeRoleFieldMissing, error.Code);
        Assert.Equal(["resolve", "open", "kind", "record-type"], calls);
    }

    [Fact(DisplayName = "open authority and Access filtering precede every row read")]
    public async Task OpenAuthorityAndAccessFilteringPrecedeEveryRowRead()
    {
        var calls = new List<string>();
        var definition = Definition();
        var authority = new ViewAuthority(
            CanOpen: true,
            Actions: [new("work.open", true)]);
        var rows = new RecordingRows(calls);
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, definition),
            new RecordingOpenGate(calls, authority),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            rows,
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-09-17T12:00:00Z")));

        var result = await runtime.ExecuteAsync(Request());

        Assert.Equal(["resolve", "open", "kind", "record-type", "access", "rows"], calls);
        Assert.Collection(
            Assert.IsType<ViewQueryPlan>(rows.Plan).Predicates,
            predicate => Assert.Equal(ViewPredicateSource.Access, predicate.Source),
            predicate => Assert.Equal(ViewPredicateSource.Authored, predicate.Source));
        Assert.Equal(authority, result.Authority);
        Assert.Equal("2026-09-17T12:00:00.0000000+00:00", result.EvaluatedAt.ToString("O"));
    }

    [Fact(DisplayName = "hidden rows are removed before totals and page boundaries")]
    public async Task HiddenRowsAreRemovedBeforeTotalsAndPageBoundaries()
    {
        var calls = new List<string>();
        var rows = new InMemoryViewRowSource([
            Row("one", "A", "party:operator-1", "open"),
            Row("hidden", "B", "party:someone-else", "open"),
            Row("three", "C", "party:operator-1", "open"),
            Row("closed", "D", "party:operator-1", "closed"),
        ]);
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, Definition()),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            rows,
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await runtime.ExecuteAsync(Request() with { Page = new(Offset: 1, Limit: 1) });

        Assert.Equal(2, result.Total);
        Assert.Collection(result.Rows, row => Assert.Equal("three", row.Id));
    }

    [Fact(DisplayName = "text identifiers remain ordinally distinct in the Access predicate")]
    public async Task TextIdentifiersRemainOrdinallyDistinctInTheAccessPredicate()
    {
        var calls = new List<string>();
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, Definition()),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new InMemoryViewRowSource([
                Row("hidden", "A", "01", "open"),
                Row("visible", "B", "1", "open"),
            ]),
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await runtime.ExecuteAsync(Request() with { Principal = "1" });

        Assert.Equal(1, result.Total);
        Assert.Collection(result.Rows, row => Assert.Equal("visible", row.Id));
    }

    [Fact(DisplayName = "typed numeric sort keys order rows before paging")]
    public async Task TypedNumericSortKeysOrderRowsBeforePaging()
    {
        var calls = new List<string>();
        var definition = Definition() with
        {
            Parameters = Definition().Parameters with
            {
                Sort = [new("priority", ViewSortDirection.Ascending)],
            },
        };
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, definition),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new InMemoryViewRowSource([
                RichRow("ten", "A", 10, []),
                RichRow("two", "B", 2, []),
            ]),
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await runtime.ExecuteAsync(Request() with { Page = new(0, 1) });

        Assert.Collection(result.Rows, row => Assert.Equal("two", row.Id));
    }

    [Fact(DisplayName = "the measure catalogue evaluates only the rows currently true")]
    public async Task MeasureCatalogueEvaluatesOnlyRowsCurrentlyTrue()
    {
        var calls = new List<string>();
        var measures = new RecordingMeasures(calls);
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, Definition(new(
                "work.open-count",
                new Dictionary<string, string> { ["format"] = "integer" }))),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new InMemoryViewRowSource([
                Row("one", "A", "party:operator-1", "open"),
                Row("hidden", "B", "party:someone-else", "open"),
                Row("closed", "C", "party:operator-1", "closed"),
            ]),
            measures,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await runtime.ExecuteAsync(Request());

        Assert.Equal(["one"], measures.RowIds);
        Assert.Equal(new ViewMeasureResult("work.open-count", 1), result.Measure);
        Assert.Equal("measure", calls[^1]);
    }

    [Fact(DisplayName = "the fixed filter grammar evaluates comparisons, functions, and collection quantifiers")]
    public async Task FixedFilterGrammarEvaluatesComparisonsFunctionsAndCollectionQuantifiers()
    {
        var calls = new List<string>();
        var filter = ViewFilter.All(
            ViewFilter.Compare("priority", ViewComparisonOperator.GreaterThan, 1),
            ViewFilter.Call(
                "contains",
                ViewFilter.FieldValue("title"),
                ViewFilter.Literal("pump")),
            ViewFilter.Any("tags", ViewFilter.Equal("$", "urgent")));
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, Definition(filter: filter)),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new InMemoryViewRowSource([
                RichRow("match", "pump repair", 2, ["urgent", "mechanical"]),
                RichRow("low", "pump inspection", 1, ["urgent"]),
                RichRow("wrong-tag", "pump replacement", 3, ["planned"]),
            ]),
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await runtime.ExecuteAsync(Request());

        Assert.Collection(result.Rows, row => Assert.Equal("match", row.Id));
    }

    [Fact(DisplayName = "a pack-registered expression function is callable from the authored filter")]
    public async Task PackRegisteredExpressionFunctionIsCallableFromAuthoredFilter()
    {
        var calls = new List<string>();
        var functions = new ViewExpressionFunctionRegistry([
            new ViewExpressionFunction(
                "pack.isUrgent",
                Arity: 1,
                Evaluate: arguments => StringComparer.Ordinal.Equals(arguments[0], "urgent")),
        ]);
        var runtime = new ViewQueryRuntime(
            new RecordingDefinitions(calls, Definition(filter: ViewFilter.Call(
                "pack.isUrgent",
                ViewFilter.FieldValue("state")))),
            new RecordingOpenGate(calls, new ViewAuthority(CanOpen: true, Actions: [])),
            new RecordingKinds(calls, IsRegistered: true),
            new RecordingRecordTypes(calls),
            new RecordingAccessFilter(calls),
            new InMemoryViewRowSource([
                Row("urgent", "A", "party:operator-1", "urgent"),
                Row("routine", "B", "party:operator-1", "routine"),
            ], functions),
            new RecordingMeasures(calls),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await runtime.ExecuteAsync(Request());

        Assert.Collection(result.Rows, row => Assert.Equal("urgent", row.Id));
    }

    private static ViewDefinition Definition(
        ViewMeasureBinding? measure = null,
        ViewFilter? filter = null) => new(
        Envelope: new(
            Identity: "work.queue",
            Version: "1.0.0",
            Tenant: "tenant-a",
            CascadeLayer: ViewCascadeLayer.Base,
            Provenance: System.Text.Json.JsonSerializer.SerializeToElement(new { source = "test" }),
            Requires: []),
        SchemaVersion: 1,
        Title: "Work queue",
        RecordType: "work-item",
        Ownership: ViewOwnershipTier.System,
        OpenPermission: "work:read",
        Parameters: new ViewQueryParameters(
            Columns: [new("title", 240)],
            Sort: [new("title", ViewSortDirection.Ascending)],
            Filter: filter ?? ViewFilter.Equal("state", "open"),
            GroupBy: null,
            Measure: measure));

    private static ViewQueryRequest Request() => new(
        Tenant: "tenant-a",
        DefinitionKey: "work.queue",
        Principal: "party:operator-1",
        Page: new(Offset: 0, Limit: 25),
        Binding: new(
            Kind: "layout.table",
            ShapeRoles: new Dictionary<ViewShapeRole, string>
            {
                [ViewShapeRole.Title] = "title",
            }));

    private static ViewRow Row(string id, string title, string assignee, string state) => new(
        id,
        new Dictionary<string, object?>
        {
            ["title"] = title,
            ["assignee"] = assignee,
            ["state"] = state,
        });

    private static ViewRow RichRow(
        string id,
        string title,
        int priority,
        IReadOnlyList<string> tags) => new(
        id,
        new Dictionary<string, object?>
        {
            ["title"] = title,
            ["assignee"] = "party:operator-1",
            ["state"] = "open",
            ["priority"] = priority,
            ["tags"] = tags,
        });

    private sealed class RecordingDefinitions(List<string> calls, ViewDefinition definition) : IViewDefinitionSource
    {
        public ValueTask<ViewDefinition?> ResolvePublishedHeadAsync(string tenant, string key, CancellationToken cancellationToken = default)
        {
            calls.Add("resolve");
            return ValueTask.FromResult<ViewDefinition?>(definition);
        }
    }

    private sealed class RecordingOpenGate(List<string> calls, ViewAuthority authority) : IViewOpenGate
    {
        public ValueTask<ViewAuthority> AuthorizeAsync(
            ViewDefinition definition,
            string principal,
            CancellationToken cancellationToken = default)
        {
            calls.Add("open");
            return ValueTask.FromResult(authority);
        }
    }

    private sealed class RecordingKinds(
        List<string> calls,
        bool IsRegistered,
        IReadOnlyList<ViewShapeRole>? RequiredRoles = null) : IViewKindRegistry
    {
        public ValueTask<ViewKindDescriptor?> ResolveAsync(string kind, CancellationToken cancellationToken = default)
        {
            calls.Add("kind");
            return ValueTask.FromResult<ViewKindDescriptor?>(IsRegistered
                ? new(kind, Renderer: "hlp.ui.data-grid", RequiredRoles: RequiredRoles ?? [ViewShapeRole.Title])
                : null);
        }

        public ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ViewKindDescriptor>>([]);
    }

    private sealed class RecordingRecordTypes(List<string> calls) : IViewRecordTypeRegistry
    {
        public ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(
            string recordType,
            CancellationToken cancellationToken = default)
        {
            calls.Add("record-type");
            return ValueTask.FromResult<ViewRecordTypeDescriptor?>(new(
                recordType,
                new Dictionary<string, ViewRecordFieldKind>
                {
                    ["title"] = ViewRecordFieldKind.Text,
                    ["state"] = ViewRecordFieldKind.Text,
                    ["assignee"] = ViewRecordFieldKind.Text,
                    ["priority"] = ViewRecordFieldKind.Ordered,
                    ["tags"] = ViewRecordFieldKind.Collection,
                }));
        }
    }

    private sealed class RecordingAccessFilter(List<string> calls) : IViewAccessFilter
    {
        public ValueTask<ViewFilter> BuildAsync(
            string tenant,
            string principal,
            string recordType,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
        {
            calls.Add("access");
            return ValueTask.FromResult(ViewFilter.Equal("assignee", principal));
        }
    }

    private sealed class RecordingRows(List<string> calls) : IViewRowSource
    {
        public ViewQueryPlan? Plan { get; private set; }

        public ValueTask<ViewRowPage> QueryAsync(ViewQueryPlan plan, CancellationToken cancellationToken = default)
        {
            calls.Add("rows");
            Plan = plan;
            return ValueTask.FromResult(new ViewRowPage(Rows: [], Total: 0, Groups: [], CurrentRows: []));
        }
    }

    private sealed class RecordingMeasures(List<string> calls) : IViewMeasureCatalog
    {
        public IReadOnlyList<string> RowIds { get; private set; } = [];

        public ValueTask<ViewMeasureDescriptor?> ResolveAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewMeasureDescriptor?>(new(name, ["format"]));

        public ValueTask<ViewMeasureResult> EvaluateAsync(
            ViewMeasureBinding binding,
            IReadOnlyList<ViewRow> rows,
            DateTimeOffset evaluatedAt,
            CancellationToken cancellationToken = default)
        {
            calls.Add("measure");
            RowIds = rows.Select(row => row.Id).ToArray();
            return ValueTask.FromResult(new ViewMeasureResult(binding.Name, rows.Count));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
