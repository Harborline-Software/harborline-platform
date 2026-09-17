using System.Text.Json;

namespace Harborline.Blocks.EntityViews;

/// <summary>Stable refusal codes emitted by the Views query runtime.</summary>
public static class ViewQueryCodes
{
    public const string OpenForbidden = "view.open_forbidden";
}

/// <summary>Stable structural refusal codes shared by authoring and execution.</summary>
public static class ViewDefinitionCodes
{
    public const string KindUnknown = "view_definition.kind_unknown";
    public const string ShapeRoleFieldMissing = "view_definition.shape_role_field_missing";
    public const string ShapeRoleFieldIncompatible = "view_definition.shape_role_field_incompatible";
    public const string AggregationExpressionForbidden = "view_definition.aggregation_expression_forbidden";
    public const string RowVisibilityRuleForbidden = "view_definition.row_visibility_rule_forbidden";
    public const string ViewerRelativeScopeForbidden = "view_definition.viewer_relative_scope_forbidden";
    public const string MeasureUnknown = "view_definition.measure_unknown";
    public const string MeasureParameterMissing = "view_definition.measure_parameter_missing";
    public const string MeasureParameterUnknown = "view_definition.measure_parameter_unknown";
    public const string FilterFunctionUnknown = "view_definition.filter_function_unknown";
    public const string WidgetUnknown = "view_definition.widget_unknown";
    public const string WidgetParameterMissing = "view_definition.widget_parameter_missing";
    public const string WidgetParameterUnknown = "view_definition.widget_parameter_unknown";
    public const string RowActionUnknown = "view_definition.row_action_unknown";
    public const string WorkflowTransitionUnknown = "view_definition.workflow_transition_unknown";
    public const string FieldUnknown = "view_definition.field_unknown";
}

/// <summary>A fail-closed refusal from the Views query runtime.</summary>
public sealed class ViewQueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The cascade tier that owns a view definition.</summary>
public enum ViewOwnershipTier
{
    System,
    Public,
    Personal,
}

/// <summary>The definition's place in the base-to-instance configuration cascade.</summary>
public enum ViewCascadeLayer
{
    Base,
    Pack,
    Tenant,
    Instance,
}

/// <summary>A capability required before a definition can be admitted.</summary>
public sealed record ViewDefinitionRequirement(string Capability, string? MinimumPlatformVersion = null);

/// <summary>The control metadata transported with every view-definition revision.</summary>
public sealed record ViewDefinitionEnvelope(
    string Identity,
    string Version,
    string Tenant,
    ViewCascadeLayer CascadeLayer,
    JsonElement Provenance,
    IReadOnlyList<ViewDefinitionRequirement> Requires);

/// <summary>The direction of one authored sort key.</summary>
public enum ViewSortDirection
{
    Ascending,
    Descending,
}

/// <summary>One selected column and its authored width and presentation treatment.</summary>
public sealed record ViewColumn(string Field, int Width, string Presentation = "text");

/// <summary>One ordered sort key.</summary>
public sealed record ViewSort(string Field, ViewSortDirection Direction);

/// <summary>A reference to catalogue-owned measure math and its authored parameters.</summary>
public sealed record ViewMeasureBinding(
    string Name,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>A catalogue measure available for binding in the editor.</summary>
public sealed record ViewMeasureDescriptor(
    string Name,
    IReadOnlyList<string> ParameterNames);

/// <summary>The catalogue-owned value computed for this refresh.</summary>
public sealed record ViewMeasureResult(string Name, object? Value);

/// <summary>The typed record-set grammar owned by a view definition.</summary>
public sealed record ViewQueryParameters(
    IReadOnlyList<ViewColumn> Columns,
    IReadOnlyList<ViewSort> Sort,
    ViewFilter? Filter,
    string? GroupBy,
    ViewMeasureBinding? Measure);

/// <summary>A published, executable view-definition revision.</summary>
public sealed record ViewDefinition(
    ViewDefinitionEnvelope Envelope,
    int SchemaVersion,
    string Title,
    string RecordType,
    ViewOwnershipTier Ownership,
    string OpenPermission,
    ViewQueryParameters Parameters)
{
    public string Key => Envelope.Identity;

    public string Version => Envelope.Version;

    public string Tenant => Envelope.Tenant;
}

/// <summary>The caller-facing authority for this refresh.</summary>
public sealed record ViewAuthority(bool CanOpen, IReadOnlyList<ViewRowActionAuthority> Actions);

/// <summary>Whether one registered row action is currently available.</summary>
public sealed record ViewRowActionAuthority(string Action, bool Allowed);

/// <summary>An offset page requested from the authored view.</summary>
public sealed record ViewPage(int Offset, int Limit);

/// <summary>A presentation-role channel held by a Layout block binding.</summary>
public enum ViewShapeRole
{
    Title,
    PlacedBy,
    GroupedBy,
}

/// <summary>The authored list-density treatment held by a Layout binding.</summary>
public enum ViewDensity
{
    Compact,
    Standard,
    Spacious,
}

/// <summary>The row interaction selected from registered actions.</summary>
public sealed record ViewRowBehavior(string? OpenAction, bool InlineEdit);

/// <summary>A registered Helm widget and its authored parameter values.</summary>
public sealed record ViewWidgetBinding(
    string Widget,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>A widget capability supplied by the host.</summary>
public sealed record ViewWidgetDescriptor(
    string Widget,
    IReadOnlyList<string> ParameterNames);

public sealed record ViewBinding(
    string Kind,
    IReadOnlyDictionary<ViewShapeRole, string> ShapeRoles,
    ViewRowBehavior? RowBehavior = null,
    ViewDensity Density = ViewDensity.Standard,
    ViewWidgetBinding? Widget = null,
    string? BoardMoveTransition = null);

/// <summary>A host-registered Layout view kind available to Views callers.</summary>
public sealed record ViewKindDescriptor(
    string Kind,
    string Renderer,
    IReadOnlyList<ViewShapeRole> RequiredRoles);

/// <summary>The scalar category of one field in a registered record type.</summary>
public enum ViewRecordFieldKind
{
    Text,
    DateTime,
    Ordered,
    Scalar,
    Collection,
    Complex,
}

/// <summary>A host-owned description of a record type available to Views.</summary>
public sealed record ViewRecordTypeDescriptor(
    string RecordType,
    IReadOnlyDictionary<string, ViewRecordFieldKind> Fields);

/// <summary>The ambient request values used to execute a published view.</summary>
public sealed record ViewQueryRequest(
    string Tenant,
    string DefinitionKey,
    string Principal,
    ViewPage Page,
    ViewBinding Binding);

/// <summary>Identifies where a predicate entered the plan.</summary>
public enum ViewPredicateSource
{
    Access,
    Authored,
}

/// <summary>One predicate in the security-significant plan order.</summary>
public sealed record ViewQueryPredicate(ViewPredicateSource Source, ViewFilter Filter);

/// <summary>A fully composed query that a host adapter executes without reordering.</summary>
public sealed record ViewQueryPlan(
    string Tenant,
    string RecordType,
    IReadOnlyList<ViewColumn> Columns,
    IReadOnlyList<ViewQueryPredicate> Predicates,
    IReadOnlyList<ViewSort> Sort,
    string? GroupBy,
    ViewMeasureBinding? Measure,
    ViewPage Page,
    DateTimeOffset EvaluatedAt);

/// <summary>One result row. Values retain their declared record-field names.</summary>
public sealed record ViewRow(string Id, IReadOnlyDictionary<string, object?> Values);

/// <summary>One group returned by the row source.</summary>
public sealed record ViewGroup(string Key, int Count);

/// <summary>The materialized row-source response after all plan predicates.</summary>
public sealed record ViewRowPage(
    IReadOnlyList<ViewRow> Rows,
    int Total,
    IReadOnlyList<ViewGroup> Groups,
    IReadOnlyList<ViewRow> CurrentRows);

/// <summary>A transient view result; it is not content and carries no edition or basis.</summary>
public sealed record ViewQueryResult(
    IReadOnlyList<ViewRow> Rows,
    int Total,
    IReadOnlyList<ViewGroup> Groups,
    ViewMeasureResult? Measure,
    ViewAuthority Authority,
    DateTimeOffset EvaluatedAt);

/// <summary>Resolves the current published definition revision.</summary>
public interface IViewDefinitionSource
{
    ValueTask<ViewDefinition?> ResolvePublishedHeadAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>Decides whether a principal may open a view and which actions it may receive.</summary>
public interface IViewOpenGate
{
    ValueTask<ViewAuthority> AuthorizeAsync(
        ViewDefinition definition,
        string principal,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves only the view kinds that the current host has registered.</summary>
public interface IViewKindRegistry
{
    ValueTask<ViewKindDescriptor?> ResolveAsync(
        string kind,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves the field contract of a host-registered record type.</summary>
public interface IViewRecordTypeRegistry
{
    ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(
        string recordType,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves the developer-owned interaction capabilities a Layout binding may name.</summary>
public interface IViewInteractionRegistry
{
    ValueTask<ViewWidgetDescriptor?> ResolveWidgetAsync(
        string widget,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasRowActionAsync(
        string action,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasWorkflowTransitionAsync(
        string transition,
        CancellationToken cancellationToken = default);
}

/// <summary>The single compatibility predicate used by kind offering and admission.</summary>
public static class ViewBindingCompatibility
{
    public static bool CanOffer(ViewKindDescriptor kind, ViewRecordTypeDescriptor recordType)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(recordType);
        return kind.RequiredRoles.All(role => recordType.Fields.Values.Any(field => IsCompatible(role, field)));
    }

    public static string? GetRefusalCode(
        ViewKindDescriptor kind,
        ViewBinding binding,
        ViewRecordTypeDescriptor? recordType)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(binding);

        foreach (var role in kind.RequiredRoles)
        {
            if (!binding.ShapeRoles.TryGetValue(role, out var field)
                || string.IsNullOrWhiteSpace(field)
                || recordType is null
                || !recordType.Fields.ContainsKey(field))
            {
                return ViewDefinitionCodes.ShapeRoleFieldMissing;
            }
        }

        foreach (var (role, field) in binding.ShapeRoles)
        {
            if (string.IsNullOrWhiteSpace(field)
                || recordType is null
                || !recordType.Fields.TryGetValue(field, out var fieldKind))
            {
                return ViewDefinitionCodes.ShapeRoleFieldMissing;
            }

            if (!IsCompatible(role, fieldKind))
            {
                return ViewDefinitionCodes.ShapeRoleFieldIncompatible;
            }
        }

        return null;
    }

    private static bool IsCompatible(ViewShapeRole role, ViewRecordFieldKind fieldKind) => role switch
    {
        ViewShapeRole.Title => fieldKind == ViewRecordFieldKind.Text,
        ViewShapeRole.PlacedBy => fieldKind is ViewRecordFieldKind.DateTime or ViewRecordFieldKind.Ordered,
        ViewShapeRole.GroupedBy => fieldKind is not (ViewRecordFieldKind.Collection or ViewRecordFieldKind.Complex),
        _ => false,
    };
}

/// <summary>Builds the Access-owned set predicate for a record type.</summary>
public interface IViewAccessFilter
{
    ValueTask<ViewFilter> BuildAsync(
        string tenant,
        string principal,
        string recordType,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes an already authorized and fully composed plan.</summary>
public interface IViewRowSource
{
    ValueTask<ViewRowPage> QueryAsync(ViewQueryPlan plan, CancellationToken cancellationToken = default);
}

/// <summary>Resolves and evaluates named measures; Views owns no aggregation math.</summary>
public interface IViewMeasureCatalog
{
    ValueTask<ViewMeasureDescriptor?> ResolveAsync(
        string name,
        CancellationToken cancellationToken = default);

    ValueTask<ViewMeasureResult> EvaluateAsync(
        ViewMeasureBinding binding,
        IReadOnlyList<ViewRow> rows,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs the Views query pipeline in its security-significant order.</summary>
public sealed class ViewQueryRuntime
{
    private readonly IViewDefinitionSource _definitions;
    private readonly IViewOpenGate _openGate;
    private readonly IViewKindRegistry _kinds;
    private readonly IViewRecordTypeRegistry _recordTypes;
    private readonly IViewAccessFilter _accessFilter;
    private readonly IViewRowSource _rows;
    private readonly IViewMeasureCatalog _measures;
    private readonly TimeProvider _clock;

    public ViewQueryRuntime(
        IViewDefinitionSource definitions,
        IViewOpenGate openGate,
        IViewKindRegistry kinds,
        IViewRecordTypeRegistry recordTypes,
        IViewAccessFilter accessFilter,
        IViewRowSource rows,
        IViewMeasureCatalog measures,
        TimeProvider clock)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _openGate = openGate ?? throw new ArgumentNullException(nameof(openGate));
        _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _accessFilter = accessFilter ?? throw new ArgumentNullException(nameof(accessFilter));
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _measures = measures ?? throw new ArgumentNullException(nameof(measures));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask<ViewQueryResult> ExecuteAsync(
        ViewQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await _definitions
            .ResolvePublishedHeadAsync(request.Tenant, request.DefinitionKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The published view definition does not exist.");
        var authority = await _openGate
            .AuthorizeAsync(definition, request.Principal, cancellationToken)
            .ConfigureAwait(false);
        if (!authority.CanOpen)
        {
            throw new ViewQueryException(ViewQueryCodes.OpenForbidden, "The view cannot be opened.");
        }
        var kind = await _kinds.ResolveAsync(request.Binding.Kind, cancellationToken).ConfigureAwait(false);
        if (kind is null)
        {
            throw new ViewQueryException(ViewDefinitionCodes.KindUnknown, "The view kind is not registered.");
        }
        var recordType = await _recordTypes
            .ResolveAsync(definition.RecordType, cancellationToken)
            .ConfigureAwait(false);
        if (ViewBindingCompatibility.GetRefusalCode(kind, request.Binding, recordType) is { } bindingRefusal)
        {
            throw new ViewQueryException(bindingRefusal, "The view binding is incompatible with its record type.");
        }
        var access = await _accessFilter
            .BuildAsync(request.Tenant, request.Principal, definition.RecordType, cancellationToken)
            .ConfigureAwait(false);

        var predicates = new List<ViewQueryPredicate>
        {
            new(ViewPredicateSource.Access, access),
        };
        if (definition.Parameters.Filter is { } authored)
        {
            predicates.Add(new(ViewPredicateSource.Authored, authored));
        }

        var evaluatedAt = _clock.GetUtcNow();
        var plan = new ViewQueryPlan(
            request.Tenant,
            definition.RecordType,
            definition.Parameters.Columns,
            predicates,
            definition.Parameters.Sort,
            definition.Parameters.GroupBy,
            definition.Parameters.Measure,
            request.Page,
            evaluatedAt);
        var page = await _rows.QueryAsync(plan, cancellationToken).ConfigureAwait(false);
        var measure = definition.Parameters.Measure is { } binding
            ? await _measures
                .EvaluateAsync(binding, page.CurrentRows, evaluatedAt, cancellationToken)
                .ConfigureAwait(false)
            : null;
        return new(page.Rows, page.Total, page.Groups, measure, authority, evaluatedAt);
    }
}
