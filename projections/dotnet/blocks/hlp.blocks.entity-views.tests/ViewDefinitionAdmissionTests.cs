using System.Text.Json;

using Harborline.Blocks.EntityViews;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewDefinitionAdmissionTests
{
    [Fact(DisplayName = "aggregation, row visibility, and viewer-relative scope are refused together")]
    public async Task ForbiddenQueryAuthorityIsRefusedTogether()
    {
        var admission = Admission();
        var draft = new ViewDefinitionDraft(
            Definition(),
            new ViewBinding(
                "layout.table",
                new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }),
            AggregationExpression: "sum(cost)",
            RowVisibilityRule: "owner = current_user()",
            ScopeToken: "mine");

        var error = await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () =>
            await admission.ValidateAsync(draft));

        Assert.Equal("definition.validate", error.Stage);
        Assert.Equal(
            [
                $"{ViewDefinitionCodes.AggregationExpressionForbidden}:/aggregationExpression",
                $"{ViewDefinitionCodes.RowVisibilityRuleForbidden}:/rowVisibilityRule",
                $"{ViewDefinitionCodes.ViewerRelativeScopeForbidden}:/scopeToken",
            ],
            error.Refusals.Select(refusal => $"{refusal.Code}:{refusal.Pointer}"));
    }

    [Fact(DisplayName = "structural admission runs before the definition-store write")]
    public async Task StructuralAdmissionRunsBeforeTheDefinitionStoreWrite()
    {
        var store = new InMemoryViewDefinitionStore();
        var authoring = new ViewDefinitionAuthoring(
            Admission(),
            store);
        var invalid = new ViewDefinitionDraft(
            Definition(),
            new ViewBinding(
                "layout.table",
                new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }),
            RowVisibilityRule: "owner = current_user()");

        await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () =>
            await authoring.CreateDraftAsync(invalid));

        Assert.Empty(await store.ListHistoryAsync("tenant-a", "work.queue"));
    }

    [Fact(DisplayName = "measure bindings are validated against catalogue parameters")]
    public async Task MeasureBindingsAreValidatedAgainstCatalogueParameters()
    {
        var admission = Admission();
        var draft = new ViewDefinitionDraft(
            Definition(new ViewMeasureBinding(
                "work.open-count",
                new Dictionary<string, string> { ["bogus"] = "value" })),
            new ViewBinding(
                "layout.table",
                new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }));

        var error = await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () =>
            await admission.ValidateAsync(draft));

        Assert.Equal(
            [
                ViewDefinitionCodes.MeasureParameterMissing,
                ViewDefinitionCodes.MeasureParameterUnknown,
            ],
            error.Refusals.Select(refusal => refusal.Code));
    }

    [Fact(DisplayName = "an unregistered filter function is refused before persistence")]
    public async Task UnregisteredFilterFunctionIsRefusedBeforePersistence()
    {
        var draft = new ViewDefinitionDraft(
            Definition(filter: ViewFilter.Call(
                "pack.missing",
                ViewFilter.FieldValue("title"))),
            new ViewBinding(
                "layout.table",
                new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }));

        var error = await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () =>
            await Admission().ValidateAsync(draft));

        Assert.Contains(
            error.Refusals,
            refusal => refusal.Code == ViewDefinitionCodes.FilterFunctionUnknown
                && refusal.Pointer == "/definition/parameters/filter/function");
    }

    [Fact(DisplayName = "the offered kind set uses the admission compatibility predicate")]
    public async Task OfferedKindSetUsesAdmissionCompatibilityPredicate()
    {
        var admission = new ViewDefinitionAdmission(
            new OfferingKinds(),
            new RecordTypes(),
            new Measures(),
            new ViewExpressionFunctionRegistry(),
            new Interactions());

        var offered = await admission.ListOfferedKindsAsync("work-item");

        Assert.Collection(offered, kind => Assert.Equal("layout.table", kind.Kind));
    }

    [Fact(DisplayName = "unknown widget, row action, and Board transition bindings refuse together")]
    public async Task UnknownInteractionBindingsRefuseTogether()
    {
        var draft = new ViewDefinitionDraft(
            Definition(),
            new ViewBinding(
                Kind: "layout.table",
                ShapeRoles: new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" },
                RowBehavior: new(OpenAction: "work.open", InlineEdit: false),
                Density: ViewDensity.Compact,
                Widget: new(
                    "helm.counter",
                    new Dictionary<string, string> { ["measure"] = "work.open-count" }),
                BoardMoveTransition: "work.advance"));

        var error = await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () =>
            await Admission().ValidateAsync(draft));

        Assert.Equal(
            [
                ViewDefinitionCodes.WidgetUnknown,
                ViewDefinitionCodes.RowActionUnknown,
                ViewDefinitionCodes.WorkflowTransitionUnknown,
            ],
            error.Refusals.Select(refusal => refusal.Code));
    }

    [Fact(DisplayName = "widget parameters are validated against the registered descriptor")]
    public async Task WidgetParametersAreValidatedAgainstRegisteredDescriptor()
    {
        var admission = new ViewDefinitionAdmission(
            new Kinds(),
            new RecordTypes(),
            new Measures(),
            new ViewExpressionFunctionRegistry(),
            new KnownInteractions());
        var draft = new ViewDefinitionDraft(
            Definition(),
            new ViewBinding(
                "layout.table",
                new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" },
                Widget: new(
                    "helm.counter",
                    new Dictionary<string, string> { ["bogus"] = "value" })));

        var error = await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () =>
            await admission.ValidateAsync(draft));

        Assert.Equal(
            [ViewDefinitionCodes.WidgetParameterMissing, ViewDefinitionCodes.WidgetParameterUnknown],
            error.Refusals.Select(refusal => refusal.Code));
    }

    [Fact(DisplayName = "columns, sort, grouping, and filter fields resolve against the record type")]
    public async Task AuthoredFieldsResolveAgainstTheRecordType()
    {
        var definition = Definition() with
        {
            Parameters = new(
                Columns: [new("missing_column", 100)],
                Sort: [new("missing_sort", ViewSortDirection.Ascending)],
                Filter: ViewFilter.Equal("missing_filter", "x"),
                GroupBy: "missing_group",
                Measure: null),
        };
        var draft = new ViewDefinitionDraft(
            definition,
            new ViewBinding(
                "layout.table",
                new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }));

        var error = await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () =>
            await Admission().ValidateAsync(draft));

        Assert.Equal(
            [
                "/definition/parameters/columns/0/field",
                "/definition/parameters/sort/0/field",
                "/definition/parameters/groupBy",
                "/definition/parameters/filter/field",
            ],
            error.Refusals
                .Where(refusal => refusal.Code == ViewDefinitionCodes.FieldUnknown)
                .Select(refusal => refusal.Pointer));
    }

    private static ViewDefinitionAdmission Admission() => new(
        new Kinds(),
        new RecordTypes(),
        new Measures(),
        new ViewExpressionFunctionRegistry(),
        new Interactions());

    private static ViewDefinition Definition(
        ViewMeasureBinding? measure = null,
        ViewFilter? filter = null) => new(
        new(
            "work.queue",
            "1.0.0",
            "tenant-a",
            ViewCascadeLayer.Tenant,
            JsonSerializer.SerializeToElement(new { source = "authoring" }),
            []),
        SchemaVersion: 1,
        Title: "Work queue",
        RecordType: "work-item",
        Ownership: ViewOwnershipTier.Public,
        OpenPermission: "work:read",
        Parameters: new(
            Columns: [new("title", 240)],
            Sort: [],
            Filter: filter,
            GroupBy: null,
            Measure: measure));

    private sealed class Kinds : IViewKindRegistry
    {
        public ValueTask<ViewKindDescriptor?> ResolveAsync(string kind, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewKindDescriptor?>(new(kind, "hlp.ui.data-grid", [ViewShapeRole.Title]));

        public ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ViewKindDescriptor>>([
                new("layout.table", "hlp.ui.data-grid", [ViewShapeRole.Title]),
            ]);
    }

    private sealed class OfferingKinds : IViewKindRegistry
    {
        private static readonly ViewKindDescriptor[] All =
        [
            new("layout.table", "hlp.ui.data-grid", [ViewShapeRole.Title]),
            new("layout.timeline", "hlp.ui.timeline", [ViewShapeRole.Title, ViewShapeRole.PlacedBy]),
        ];

        public ValueTask<ViewKindDescriptor?> ResolveAsync(string kind, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewKindDescriptor?>(All.SingleOrDefault(candidate => candidate.Kind == kind));

        public ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ViewKindDescriptor>>(All);
    }

    private sealed class RecordTypes : IViewRecordTypeRegistry
    {
        public ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(string recordType, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewRecordTypeDescriptor?>(new(
                recordType,
                new Dictionary<string, ViewRecordFieldKind> { ["title"] = ViewRecordFieldKind.Text }));
    }

    private sealed class Measures : IViewMeasureCatalog
    {
        public ValueTask<ViewMeasureDescriptor?> ResolveAsync(string name, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewMeasureDescriptor?>(new(name, ["format"]));

        public ValueTask<ViewMeasureResult> EvaluateAsync(
            ViewMeasureBinding binding,
            IReadOnlyList<ViewRow> rows,
            DateTimeOffset evaluatedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Interactions : IViewInteractionRegistry
    {
        public ValueTask<ViewWidgetDescriptor?> ResolveWidgetAsync(
            string widget,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewWidgetDescriptor?>(null);

        public ValueTask<bool> HasRowActionAsync(
            string action,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> HasWorkflowTransitionAsync(
            string transition,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class KnownInteractions : IViewInteractionRegistry
    {
        public ValueTask<ViewWidgetDescriptor?> ResolveWidgetAsync(
            string widget,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewWidgetDescriptor?>(new(widget, ["measure"]));

        public ValueTask<bool> HasRowActionAsync(string action, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> HasWorkflowTransitionAsync(string transition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
