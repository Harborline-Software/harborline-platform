namespace Harborline.Blocks.EntityViews;

/// <summary>The authoring payload validated before a definition can be persisted or published.</summary>
public sealed record ViewDefinitionDraft(
    ViewDefinition Definition,
    ViewBinding Binding,
    string? AggregationExpression = null,
    string? RowVisibilityRule = null,
    string? ScopeToken = null);

/// <summary>One structural admission refusal and its RFC 6901 target.</summary>
public sealed record ViewDefinitionRefusal(string Code, string Pointer);

/// <summary>All structural refusals found during one terminal validation stage.</summary>
public sealed class ViewDefinitionAdmissionException(
    string stage,
    IReadOnlyList<ViewDefinitionRefusal> refusals)
    : Exception("The view definition was refused.")
{
    public string Stage { get; } = stage;

    public IReadOnlyList<ViewDefinitionRefusal> Refusals { get; } = refusals;
}

/// <summary>Runs the same structural gates for editor validation and persistence admission.</summary>
public sealed class ViewDefinitionAdmission(
    IViewKindRegistry kinds,
    IViewRecordTypeRegistry recordTypes,
    IViewMeasureCatalog measures,
    IViewExpressionFunctionRegistry functions,
    IViewInteractionRegistry interactions)
{
    private const string Stage = "definition.validate";
    private readonly IViewKindRegistry _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
    private readonly IViewRecordTypeRegistry _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
    private readonly IViewMeasureCatalog _measures = measures ?? throw new ArgumentNullException(nameof(measures));
    private readonly IViewExpressionFunctionRegistry _functions = functions ?? throw new ArgumentNullException(nameof(functions));
    private readonly IViewInteractionRegistry _interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));

    public async ValueTask ValidateAsync(
        ViewDefinitionDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var refusals = new List<ViewDefinitionRefusal>();
        if (draft.AggregationExpression is not null)
        {
            refusals.Add(new(
                ViewDefinitionCodes.AggregationExpressionForbidden,
                "/aggregationExpression"));
        }
        if (draft.RowVisibilityRule is not null)
        {
            refusals.Add(new(
                ViewDefinitionCodes.RowVisibilityRuleForbidden,
                "/rowVisibilityRule"));
        }
        if (draft.ScopeToken is not null)
        {
            refusals.Add(new(
                ViewDefinitionCodes.ViewerRelativeScopeForbidden,
                "/scopeToken"));
        }

        ViewRecordTypeDescriptor? recordType = null;
        var kind = await _kinds.ResolveAsync(draft.Binding.Kind, cancellationToken).ConfigureAwait(false);
        if (kind is null)
        {
            refusals.Add(new(ViewDefinitionCodes.KindUnknown, "/binding/kind"));
        }
        else
        {
            recordType = await _recordTypes
                .ResolveAsync(draft.Definition.RecordType, cancellationToken)
                .ConfigureAwait(false);
            if (ViewBindingCompatibility.GetRefusalCode(kind, draft.Binding, recordType) is { } bindingCode)
            {
                refusals.Add(new(bindingCode, "/binding/shapeRoles"));
            }
        }

        if (recordType is not null)
        {
            for (var index = 0; index < draft.Definition.Parameters.Columns.Count; index++)
            {
                AddUnknownField(
                    draft.Definition.Parameters.Columns[index].Field,
                    $"/definition/parameters/columns/{index}/field",
                    recordType,
                    refusals);
            }
            for (var index = 0; index < draft.Definition.Parameters.Sort.Count; index++)
            {
                AddUnknownField(
                    draft.Definition.Parameters.Sort[index].Field,
                    $"/definition/parameters/sort/{index}/field",
                    recordType,
                    refusals);
            }
            if (draft.Definition.Parameters.GroupBy is { } groupBy)
            {
                AddUnknownField(
                    groupBy,
                    "/definition/parameters/groupBy",
                    recordType,
                    refusals);
            }
            if (draft.Definition.Parameters.Filter is { } authoredFilter)
            {
                foreach (var (field, pointer) in Fields(authoredFilter, "/definition/parameters/filter"))
                {
                    if (field != "$")
                    {
                        AddUnknownField(field, pointer, recordType, refusals);
                    }
                }
            }
        }

        if (draft.Definition.Parameters.Measure is { } measure)
        {
            var descriptor = await _measures.ResolveAsync(measure.Name, cancellationToken).ConfigureAwait(false);
            if (descriptor is null)
            {
                refusals.Add(new(
                    ViewDefinitionCodes.MeasureUnknown,
                    "/definition/parameters/measure/name"));
            }
            else
            {
                foreach (var required in descriptor.ParameterNames)
                {
                    if (!measure.Parameters.ContainsKey(required))
                    {
                        refusals.Add(new(
                            ViewDefinitionCodes.MeasureParameterMissing,
                            $"/definition/parameters/measure/parameters/{Escape(required)}"));
                    }
                }
                foreach (var supplied in measure.Parameters.Keys.Order(StringComparer.Ordinal))
                {
                    if (!descriptor.ParameterNames.Contains(supplied, StringComparer.Ordinal))
                    {
                        refusals.Add(new(
                            ViewDefinitionCodes.MeasureParameterUnknown,
                            $"/definition/parameters/measure/parameters/{Escape(supplied)}"));
                    }
                }
            }
        }

        if (draft.Definition.Parameters.Filter is { } filter)
        {
            foreach (var function in Functions(filter))
            {
                if (!_functions.IsRegistered(function.Function, function.Arguments.Count))
                {
                    refusals.Add(new(
                        ViewDefinitionCodes.FilterFunctionUnknown,
                        "/definition/parameters/filter/function"));
                }
            }
        }

        if (draft.Binding.Widget is { } widget)
        {
            var descriptor = await _interactions
                .ResolveWidgetAsync(widget.Widget, cancellationToken)
                .ConfigureAwait(false);
            if (descriptor is null)
            {
                refusals.Add(new(ViewDefinitionCodes.WidgetUnknown, "/binding/widget/widget"));
            }
            else
            {
                foreach (var required in descriptor.ParameterNames)
                {
                    if (!widget.Parameters.ContainsKey(required))
                    {
                        refusals.Add(new(
                            ViewDefinitionCodes.WidgetParameterMissing,
                            $"/binding/widget/parameters/{Escape(required)}"));
                    }
                }
                foreach (var supplied in widget.Parameters.Keys.Order(StringComparer.Ordinal))
                {
                    if (!descriptor.ParameterNames.Contains(supplied, StringComparer.Ordinal))
                    {
                        refusals.Add(new(
                            ViewDefinitionCodes.WidgetParameterUnknown,
                            $"/binding/widget/parameters/{Escape(supplied)}"));
                    }
                }
            }
        }
        if (draft.Binding.RowBehavior?.OpenAction is { } action
            && !await _interactions.HasRowActionAsync(action, cancellationToken).ConfigureAwait(false))
        {
            refusals.Add(new(ViewDefinitionCodes.RowActionUnknown, "/binding/rowBehavior/openAction"));
        }
        if (draft.Binding.BoardMoveTransition is { } transition
            && !await _interactions.HasWorkflowTransitionAsync(transition, cancellationToken).ConfigureAwait(false))
        {
            refusals.Add(new(ViewDefinitionCodes.WorkflowTransitionUnknown, "/binding/boardMoveTransition"));
        }

        if (refusals.Count > 0)
        {
            throw new ViewDefinitionAdmissionException(Stage, refusals);
        }
    }

    public async ValueTask<IReadOnlyList<ViewKindDescriptor>> ListOfferedKindsAsync(
        string recordType,
        CancellationToken cancellationToken = default)
    {
        var descriptor = await _recordTypes.ResolveAsync(recordType, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
        {
            return [];
        }
        var kinds = await _kinds.ListAsync(cancellationToken).ConfigureAwait(false);
        return kinds
            .Where(kind => ViewBindingCompatibility.CanOffer(kind, descriptor))
            .OrderBy(kind => kind.Kind, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Escape(string token) => token.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static IEnumerable<ViewFunctionFilter> Functions(ViewFilter filter)
    {
        switch (filter)
        {
            case ViewFunctionFilter function:
                yield return function;
                break;
            case ViewAllFilter all:
                foreach (var nested in all.Filters.SelectMany(Functions)) yield return nested;
                break;
            case ViewAnyOfFilter any:
                foreach (var nested in any.Filters.SelectMany(Functions)) yield return nested;
                break;
            case ViewNotFilter not:
                foreach (var nested in Functions(not.Filter)) yield return nested;
                break;
            case ViewCollectionFilter collection:
                foreach (var nested in Functions(collection.Predicate)) yield return nested;
                break;
        }
    }

    private static void AddUnknownField(
        string field,
        string pointer,
        ViewRecordTypeDescriptor recordType,
        ICollection<ViewDefinitionRefusal> refusals)
    {
        if (string.IsNullOrWhiteSpace(field) || !recordType.Fields.ContainsKey(field))
        {
            refusals.Add(new(ViewDefinitionCodes.FieldUnknown, pointer));
        }
    }

    private static IEnumerable<(string Field, string Pointer)> Fields(ViewFilter filter, string pointer)
    {
        switch (filter)
        {
            case ViewComparisonFilter comparison:
                yield return (comparison.Field, $"{pointer}/field");
                break;
            case ViewFunctionFilter function:
                for (var index = 0; index < function.Arguments.Count; index++)
                {
                    if (function.Arguments[index] is ViewFieldOperand field)
                    {
                        yield return (field.Field, $"{pointer}/arguments/{index}/field");
                    }
                }
                break;
            case ViewAllFilter all:
                for (var index = 0; index < all.Filters.Count; index++)
                    foreach (var nested in Fields(all.Filters[index], $"{pointer}/filters/{index}")) yield return nested;
                break;
            case ViewAnyOfFilter any:
                for (var index = 0; index < any.Filters.Count; index++)
                    foreach (var nested in Fields(any.Filters[index], $"{pointer}/filters/{index}")) yield return nested;
                break;
            case ViewNotFilter not:
                foreach (var nested in Fields(not.Filter, $"{pointer}/filter")) yield return nested;
                break;
            case ViewCollectionFilter collection:
                yield return (collection.Field, $"{pointer}/field");
                foreach (var nested in Fields(collection.Predicate, $"{pointer}/predicate")) yield return nested;
                break;
        }
    }
}

/// <summary>The authoring command surface; admission always precedes persistence.</summary>
public sealed class ViewDefinitionAuthoring(
    ViewDefinitionAdmission admission,
    IViewDefinitionStore store)
{
    private readonly ViewDefinitionAdmission _admission = admission ?? throw new ArgumentNullException(nameof(admission));
    private readonly IViewDefinitionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask<ViewDefinitionRevision> CreateDraftAsync(
        ViewDefinitionDraft draft,
        CancellationToken cancellationToken = default)
    {
        await _admission.ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
        return await _store.CreateDraftAsync(draft.Definition, cancellationToken).ConfigureAwait(false);
    }
}
