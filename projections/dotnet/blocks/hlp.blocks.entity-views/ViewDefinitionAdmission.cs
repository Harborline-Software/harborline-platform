using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.RuleEngine.Environments;

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

/// <summary>The host facts the structural rules need, read before the rules run.</summary>
internal sealed record ViewAdmissionFacts(
    ViewKindDescriptor? Kind,
    bool RecordTypeChecked,
    ViewRecordTypeDescriptor? RecordType,
    ViewMeasureDescriptor? Measure,
    IViewExpressionFunctionRegistry Functions,
    ViewWidgetDescriptor? Widget,
    bool RowActionKnown,
    bool TransitionKnown);

/// <summary>All structural refusals found during one terminal validation stage.</summary>
public sealed class ViewDefinitionAdmissionException(
    string stage,
    IReadOnlyList<ViewDefinitionRefusal> refusals)
    : Exception("The view definition was refused.")
{
    /// <summary>The validation stage that produced the refusals.</summary>
    public string Stage { get; } = stage;

    /// <summary>Every refusal found in the stage, each with a code and JSON-pointer target.</summary>
    public IReadOnlyList<ViewDefinitionRefusal> Refusals { get; } = refusals;
}

/// <summary>Runs the same structural gates for editor validation and persistence admission.</summary>
public sealed class ViewDefinitionAdmission(
    IViewKindRegistry kinds,
    IViewRecordTypeRegistry recordTypes,
    IViewMeasureCatalog measures,
    IViewExpressionFunctionRegistry functions,
    IViewInteractionRegistry interactions,
    AdmittedEnvironment? environment = null)
{
    private const string Stage = "definition.validate";
    private readonly IViewKindRegistry _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
    private readonly IViewRecordTypeRegistry _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
    private readonly IViewMeasureCatalog _measures = measures ?? throw new ArgumentNullException(nameof(measures));
    private readonly IViewExpressionFunctionRegistry _functions = functions ?? throw new ArgumentNullException(nameof(functions));
    private readonly IViewInteractionRegistry _interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));

    /// <summary>Checks the draft and throws <see cref="ViewDefinitionAdmissionException"/> listing every refusal found: forbidden aggregation, row-visibility and scope-token inputs, a contract outside the platform window, unknown kind, incompatible shape roles, unknown fields, measure, widget and filter-function names, missing or unknown parameters, and unknown row actions or workflow transitions. Returns normally only when there are none.</summary>
    public async ValueTask ValidateAsync(
        ViewDefinitionDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ViewsExpressionEnvironment.Require(environment);
        // Every host fact the rules need is read first; the rules themselves are the one synchronous check the shared
        // definition store also runs (ViewDefinitionCatalogue.Admission), so author time and the store cannot disagree.
        var kind = await _kinds.ResolveAsync(draft.Binding.Kind, cancellationToken).ConfigureAwait(false);
        var recordType = kind is null
            ? null
            : await _recordTypes.ResolveAsync(draft.Definition.RecordType, cancellationToken).ConfigureAwait(false);
        var measure = draft.Definition.Parameters.Measure is { } binding
            ? await _measures.ResolveAsync(binding.Name, cancellationToken).ConfigureAwait(false)
            : null;
        var widget = draft.Binding.Widget is { } bound
            ? await _interactions.ResolveWidgetAsync(bound.Widget, cancellationToken).ConfigureAwait(false)
            : null;
        var rowActionKnown = draft.Binding.RowBehavior?.OpenAction is not { } action
            || await _interactions.HasRowActionAsync(action, cancellationToken).ConfigureAwait(false);
        var transitionKnown = draft.Binding.BoardMoveTransition is not { } transition
            || await _interactions.HasWorkflowTransitionAsync(transition, cancellationToken).ConfigureAwait(false);

        var refusals = Check(draft, new(kind, RecordTypeChecked: true, recordType, measure, _functions, widget, rowActionKnown, transitionKnown));
        if (refusals.Count > 0)
        {
            throw new ViewDefinitionAdmissionException(Stage, refusals);
        }
    }

    /// <summary>
    /// The structural rules over facts already read from the host. With <see cref="ViewAdmissionFacts.RecordTypeChecked"/>
    /// false, the record type's fields are not consulted: the shared store checks them in its dependency derivation.
    /// </summary>
    internal static List<ViewDefinitionRefusal> Check(ViewDefinitionDraft draft, ViewAdmissionFacts facts)
    {
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
        // T-572 (rulings 85-88): View maps the shared contract refusal into its own record.
        var contract = PlatformPackageSeed.ContractWindow.Check(draft.Definition.Envelope.Contract, null);
        if (contract is not null)
        {
            refusals.Add(new(contract.Code, contract.Pointer));
        }
        if (draft.ScopeToken is not null)
        {
            refusals.Add(new(
                ViewDefinitionCodes.ViewerRelativeScopeForbidden,
                "/scopeToken"));
        }

        if (facts.Kind is null)
        {
            refusals.Add(new(ViewDefinitionCodes.KindUnknown, "/binding/kind"));
        }
        else if (facts.RecordTypeChecked)
        {
            if (ViewBindingCompatibility.GetRefusalCode(facts.Kind, draft.Binding, facts.RecordType) is { } bindingCode)
            {
                refusals.Add(new(bindingCode, "/binding/shapeRoles"));
            }
        }
        else if (facts.Kind.RequiredRoles.Any(role => !draft.Binding.ShapeRoles.TryGetValue(role, out var field) || string.IsNullOrWhiteSpace(field)))
        {
            refusals.Add(new(ViewDefinitionCodes.ShapeRoleFieldMissing, "/binding/shapeRoles"));
        }

        if (facts.RecordTypeChecked && facts.RecordType is { } recordType)
        {
            foreach (var (field, pointer) in ReferencedFields(draft.Definition))
            {
                AddUnknownField(field, pointer, recordType, refusals);
            }
        }

        if (draft.Definition.Parameters.Measure is { } measure)
        {
            if (facts.Measure is not { } descriptor)
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
            foreach (var (function, pointer) in Functions(
                filter,
                "/definition/parameters/filter"))
            {
                if (!facts.Functions.IsRegistered(function.Function, function.Arguments.Count))
                {
                    refusals.Add(new(
                        ViewDefinitionCodes.FilterFunctionUnknown,
                        $"{pointer}/function"));
                }
            }
        }

        if (draft.Binding.Widget is { } widget)
        {
            if (facts.Widget is not { } descriptor)
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
        if (draft.Binding.RowBehavior?.OpenAction is not null && !facts.RowActionKnown)
        {
            refusals.Add(new(ViewDefinitionCodes.RowActionUnknown, "/binding/rowBehavior/openAction"));
        }
        if (draft.Binding.BoardMoveTransition is not null && !facts.TransitionKnown)
        {
            refusals.Add(new(ViewDefinitionCodes.WorkflowTransitionUnknown, "/binding/boardMoveTransition"));
        }
        return refusals;
    }

    /// <summary>Every record field the definition names, with the RFC 6901 pointer of the naming member.</summary>
    internal static IEnumerable<(string Field, string Pointer)> ReferencedFields(ViewDefinition definition)
    {
        for (var index = 0; index < definition.Parameters.Columns.Count; index++)
        {
            yield return (definition.Parameters.Columns[index].Field, $"/definition/parameters/columns/{index}/field");
        }
        for (var index = 0; index < definition.Parameters.Sort.Count; index++)
        {
            yield return (definition.Parameters.Sort[index].Field, $"/definition/parameters/sort/{index}/field");
        }
        if (definition.Parameters.GroupBy is { } groupBy)
        {
            yield return (groupBy, "/definition/parameters/groupBy");
        }
        if (definition.Parameters.Filter is { } authoredFilter)
        {
            foreach (var (field, pointer) in Fields(authoredFilter, "/definition/parameters/filter"))
            {
                if (field != "$")
                {
                    yield return (field, pointer);
                }
            }
        }
    }

    /// <summary>Lists the registered view kinds that can bind to the record type, ordered by kind name; empty when the record type is unknown.</summary>
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

    internal static string Escape(string token) => token.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static IEnumerable<(ViewFunctionFilter Function, string Pointer)> Functions(
        ViewFilter filter,
        string pointer)
    {
        switch (filter)
        {
            case ViewFunctionFilter function:
                yield return (function, pointer);
                break;
            case ViewAllFilter all:
                for (var index = 0; index < all.Filters.Count; index++)
                    foreach (var nested in Functions(all.Filters[index], $"{pointer}/filters/{index}")) yield return nested;
                break;
            case ViewAnyOfFilter any:
                for (var index = 0; index < any.Filters.Count; index++)
                    foreach (var nested in Functions(any.Filters[index], $"{pointer}/filters/{index}")) yield return nested;
                break;
            case ViewNotFilter not:
                foreach (var nested in Functions(not.Filter, $"{pointer}/filter")) yield return nested;
                break;
            case ViewCollectionFilter collection:
                foreach (var nested in Functions(collection.Predicate, $"{pointer}/predicate")) yield return nested;
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
    IViewDefinitionCatalogue store)
{
    private readonly ViewDefinitionAdmission _admission = admission ?? throw new ArgumentNullException(nameof(admission));
    private readonly IViewDefinitionCatalogue _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Stamps the platform's current contract version onto the draft, validates it (throwing <see cref="ViewDefinitionAdmissionException"/> on refusal) and only then persists it as a draft revision.</summary>
    public async ValueTask<ViewDefinitionRevision> CreateDraftAsync(
        ViewDefinitionDraft draft,
        CancellationToken cancellationToken = default)
    {
        // Ruling 85: the authoring surface stamps the app's contract before admission; authors never type it.
        var window = PlatformPackageSeed.ContractWindow;
        var stamped = draft with
        {
            Definition = draft.Definition with
            {
                Envelope = draft.Definition.Envelope with
                {
                    Contract = new DefinitionContractVersion(window.Major, window.Minor),
                },
            },
        };
        await _admission.ValidateAsync(stamped, cancellationToken).ConfigureAwait(false);
        return await _store.CreateDraftAsync(
            stamped.Definition,
            draft.Binding,
            cancellationToken).ConfigureAwait(false);
    }
}
