using System.Security.Cryptography;
using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.EntityViews;

/// <summary>Stable refusal codes emitted by the Views query runtime.</summary>
public static class ViewQueryCodes
{
    /// <summary>The principal may not open the view.</summary>
    public const string OpenForbidden = "view.open_forbidden";
}

/// <summary>Stable structural refusal codes shared by authoring and execution.</summary>
public static class ViewDefinitionCodes
{
    /// <summary>The requested view kind is not registered with the host.</summary>
    public const string KindUnknown = "view_definition.kind_unknown";
    /// <summary>A shape role the kind requires is unbound, blank, or names a field the record type lacks.</summary>
    public const string ShapeRoleFieldMissing = "view_definition.shape_role_field_missing";
    /// <summary>A shape role is bound to a field whose kind does not suit that role.</summary>
    public const string ShapeRoleFieldIncompatible = "view_definition.shape_role_field_incompatible";
    /// <summary>The authored definition carries an aggregation expression, which Views does not accept.</summary>
    public const string AggregationExpressionForbidden = "view_definition.aggregation_expression_forbidden";
    /// <summary>The authored definition carries a row-visibility rule; visibility belongs to Access.</summary>
    public const string RowVisibilityRuleForbidden = "view_definition.row_visibility_rule_forbidden";
    /// <summary>The authored definition carries a viewer-relative scope token.</summary>
    public const string ViewerRelativeScopeForbidden = "view_definition.viewer_relative_scope_forbidden";
    /// <summary>The referenced measure is not in the catalogue.</summary>
    public const string MeasureUnknown = "view_definition.measure_unknown";
    /// <summary>A parameter the measure requires was not supplied.</summary>
    public const string MeasureParameterMissing = "view_definition.measure_parameter_missing";
    /// <summary>A supplied measure parameter is not one the measure declares.</summary>
    public const string MeasureParameterUnknown = "view_definition.measure_parameter_unknown";
    /// <summary>A filter calls a function that is not registered with that arity.</summary>
    public const string FilterFunctionUnknown = "view_definition.filter_function_unknown";
    /// <summary>The referenced widget is not registered.</summary>
    public const string WidgetUnknown = "view_definition.widget_unknown";
    /// <summary>A parameter the widget requires was not supplied.</summary>
    public const string WidgetParameterMissing = "view_definition.widget_parameter_missing";
    /// <summary>A supplied widget parameter is not one the widget declares.</summary>
    public const string WidgetParameterUnknown = "view_definition.widget_parameter_unknown";
    /// <summary>The row open action is not a registered action.</summary>
    public const string RowActionUnknown = "view_definition.row_action_unknown";
    /// <summary>The board-move transition is not a registered workflow transition.</summary>
    public const string WorkflowTransitionUnknown = "view_definition.workflow_transition_unknown";
    /// <summary>A column, sort, group or filter names a field the record type lacks.</summary>
    public const string FieldUnknown = "view_definition.field_unknown";
}

/// <summary>A fail-closed refusal from the Views query runtime.</summary>
public sealed class ViewQueryException(string code, string message) : Exception(message)
{
    /// <summary>The stable refusal code, one of <see cref="ViewQueryCodes"/> or <see cref="ViewDefinitionCodes"/> (or a store code such as view_definition.not_found).</summary>
    public string Code { get; } = code;
}

/// <summary>The cascade tier that owns a view definition.</summary>
public enum ViewOwnershipTier
{
    /// <summary>Shipped with the platform; not editable by tenants.</summary>
    System,
    /// <summary>Shared with other principals of the tenant.</summary>
    Public,
    /// <summary>Owned by one principal and excluded from signed-pack export.</summary>
    Personal,
}

/// <summary>The definition's place in the base-to-instance configuration cascade.</summary>
public enum ViewCascadeLayer
{
    /// <summary>The platform base definition.</summary>
    Base,
    /// <summary>Contributed by a pack.</summary>
    Pack,
    /// <summary>Overridden for one tenant.</summary>
    Tenant,
    /// <summary>Overridden for a single instance.</summary>
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
    IReadOnlyList<ViewDefinitionRequirement> Requires,
    DefinitionContractVersion? Contract);

/// <summary>The direction of one authored sort key.</summary>
public enum ViewSortDirection
{
    /// <summary>Smallest value first.</summary>
    Ascending,
    /// <summary>Largest value first.</summary>
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
    /// <summary>The definition's identity from its envelope.</summary>
    public string Key => Envelope.Identity;

    /// <summary>The definition's semantic version from its envelope.</summary>
    public string Version => Envelope.Version;

    /// <summary>The owning tenant from its envelope.</summary>
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
    /// <summary>The row title.</summary>
    Title,
    /// <summary>The field that places a row on the layout, a date-time or ordered field.</summary>
    PlacedBy,
    /// <summary>The field rows are grouped by; any scalar field.</summary>
    GroupedBy,
}

/// <summary>The authored list-density treatment held by a Layout binding.</summary>
public enum ViewDensity
{
    /// <summary>Tightest row spacing.</summary>
    Compact,
    /// <summary>Default row spacing.</summary>
    Standard,
    /// <summary>Loosest row spacing.</summary>
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

/// <summary>The authored Layout binding of a view: its kind, the record field behind each shape role, and optional row behavior, density, widget and board-move transition.</summary>
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
    /// <summary>Free text.</summary>
    Text,
    /// <summary>A point in time.</summary>
    DateTime,
    /// <summary>A value with a natural order.</summary>
    Ordered,
    /// <summary>Any other single value, such as a flag or identifier.</summary>
    Scalar,
    /// <summary>A list of values; cannot be grouped by.</summary>
    Collection,
    /// <summary>A nested object; cannot be grouped by.</summary>
    Complex,
}

/// <summary>A host-owned description of a record type available to Views.</summary>
public sealed record ViewRecordTypeDescriptor(
    string RecordType,
    IReadOnlyDictionary<string, ViewRecordFieldKind> Fields,
    string? Tenant = null,
    string? SchemaRef = null,
    IReadOnlyDictionary<string, FieldBindingDefinition>? FieldBindings = null);

/// <summary>The ambient request values used to execute a published view.</summary>
public sealed record ViewQueryRequest(
    string Tenant,
    string DefinitionKey,
    string Principal,
    ViewPage Page,
    ViewBinding Binding,
    RoleVocabulary? RoleVocabulary = null,
    HeldRoleSet? HeldRoles = null,
    string? IfNoneMatch = null);

/// <summary>Identifies where a predicate entered the plan.</summary>
public enum ViewPredicateSource
{
    /// <summary>Added by the Access filter so the caller sees only rows it may read; always first.</summary>
    Access,
    /// <summary>The view author's own filter, applied after access.</summary>
    Authored,
}

/// <summary>One predicate in the security-significant plan order.</summary>
public sealed record ViewQueryPredicate(ViewPredicateSource Source, ViewFilter Filter);

/// <summary>A fully composed query that a host adapter executes without reordering.</summary>
public sealed record ViewQueryPlan(
    string Tenant,
    string RecordType,
    IReadOnlyDictionary<string, ViewRecordFieldKind> FieldKinds,
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
    DateTimeOffset EvaluatedAt)
{
    /// <summary>Hash of the definition, binding, page, authority and result; a request whose <c>IfNoneMatch</c> equals it is answered as not modified.</summary>
    public required string ETag { get; init; }

    /// <summary>True when the ETag matched, in which case the rows, groups and measure are omitted.</summary>
    public bool NotModified { get; init; }

    /// <summary>Allowed values per column, present only for columns whose field domain resolved to a value list.</summary>
    public IReadOnlyDictionary<string, ResolvedFieldConstraints> ColumnDomains { get; init; }
        = new Dictionary<string, ResolvedFieldConstraints>();
}

/// <summary>Resolves the current published definition revision.</summary>
public interface IViewDefinitionSource
{
    /// <summary>Returns the highest-version published definition for the tenant and key, or null when none is published.</summary>
    ValueTask<ViewDefinition?> ResolvePublishedHeadAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>Decides whether a principal may open a view and which actions it may receive.</summary>
public interface IViewOpenGate
{
    /// <summary>Returns whether the principal may open the view and which registered row actions it may use.</summary>
    ValueTask<ViewAuthority> AuthorizeAsync(
        ViewDefinition definition,
        string principal,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves only the view kinds that the current host has registered.</summary>
public interface IViewKindRegistry
{
    /// <summary>Returns the descriptor for a registered kind, or null when the host has not registered it.</summary>
    ValueTask<ViewKindDescriptor?> ResolveAsync(
        string kind,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every view kind the host has registered.</summary>
    ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves the field contract of a host-registered record type.</summary>
public interface IViewRecordTypeRegistry
{
    /// <summary>Resolves an admitted tenant model; bound descriptors must attest the same tenant.</summary>
    ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(string tenant, string recordType,
        CancellationToken cancellationToken = default) => ResolveAsync(recordType, cancellationToken);

    /// <summary>Returns the record-type descriptor, or null when it is not registered.</summary>
    ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(
        string recordType,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves the developer-owned interaction capabilities a Layout binding may name.</summary>
public interface IViewInteractionRegistry
{
    /// <summary>Returns the descriptor for a registered widget, or null when it is not registered.</summary>
    ValueTask<ViewWidgetDescriptor?> ResolveWidgetAsync(
        string widget,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether the action is a registered row action.</summary>
    ValueTask<bool> HasRowActionAsync(
        string action,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether the transition is a registered workflow transition.</summary>
    ValueTask<bool> HasWorkflowTransitionAsync(
        string transition,
        CancellationToken cancellationToken = default);
}

/// <summary>The single compatibility predicate used by kind offering and admission.</summary>
public static class ViewBindingCompatibility
{
    /// <summary>True when every role the kind requires has at least one compatible field in the record type.</summary>
    public static bool CanOffer(ViewKindDescriptor kind, ViewRecordTypeDescriptor recordType)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(recordType);
        return kind.RequiredRoles.All(role => recordType.Fields.Values.Any(field => IsCompatible(role, field)));
    }

    /// <summary>Returns null when the binding is valid, <see cref="ViewDefinitionCodes.ShapeRoleFieldMissing"/> when a required or bound role has no usable field, or <see cref="ViewDefinitionCodes.ShapeRoleFieldIncompatible"/> when a bound field's kind does not suit its role.</summary>
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
    /// <summary>Builds the access filter for the tenant, principal and record type as of the given instant.</summary>
    ValueTask<ViewFilter> BuildAsync(
        string tenant,
        string principal,
        string recordType,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes an already authorized and fully composed plan.</summary>
public interface IViewRowSource
{
    /// <summary>Runs the plan exactly as ordered and returns the matching rows, the total, the groups and the current page.</summary>
    ValueTask<ViewRowPage> QueryAsync(ViewQueryPlan plan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves and evaluates named measures; Views owns no aggregation math. The tenant and principal
/// are part of the seam because the measure substrate binds the Access set filter at an explicit
/// identity and instant before it computes anything (T-624).
/// </summary>
public interface IViewMeasureCatalog
{
    /// <summary>Returns the catalogue descriptor for the named measure, or null when it does not exist.</summary>
    ValueTask<ViewMeasureDescriptor?> ResolveAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Computes the bound measure over the rows for the tenant and principal at the given instant.</summary>
    ValueTask<ViewMeasureResult> EvaluateAsync(
        ViewMeasureBinding binding,
        IReadOnlyList<ViewRow> rows,
        DateTimeOffset evaluatedAt,
        string tenant,
        string principal,
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
    private readonly IFieldDomainRuntime? _fieldDomains;

    /// <summary>Wires the pipeline's ports; throws <see cref="ArgumentNullException"/> for any null except the optional field-domain runtime.</summary>
    public ViewQueryRuntime(
        IViewDefinitionSource definitions,
        IViewOpenGate openGate,
        IViewKindRegistry kinds,
        IViewRecordTypeRegistry recordTypes,
        IViewAccessFilter accessFilter,
        IViewRowSource rows,
        IViewMeasureCatalog measures,
        TimeProvider clock,
        IFieldDomainRuntime? fieldDomains = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _openGate = openGate ?? throw new ArgumentNullException(nameof(openGate));
        _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _accessFilter = accessFilter ?? throw new ArgumentNullException(nameof(accessFilter));
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _measures = measures ?? throw new ArgumentNullException(nameof(measures));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _fieldDomains = fieldDomains;
    }

    /// <summary>Resolves the published definition, checks the open gate, binding and field domains, then queries with the Access predicate first and the authored filter second. Throws <see cref="InvalidOperationException"/> when no definition is published, <see cref="ViewQueryException"/> for a forbidden open, unknown kind or incompatible binding, and a field-admission error for an unresolved column binding. Returns not-modified (no rows) when the ETag matches.</summary>
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
            .ResolveAsync(request.Tenant, definition.RecordType, cancellationToken)
            .ConfigureAwait(false);
        if (ViewBindingCompatibility.GetRefusalCode(kind, request.Binding, recordType) is { } bindingRefusal)
        {
            throw new ViewQueryException(bindingRefusal, "The view binding is incompatible with its record type.");
        }
        var evaluatedAt = _clock.GetUtcNow();
        var access = await _accessFilter
            .BuildAsync(request.Tenant, request.Principal, definition.RecordType, evaluatedAt, cancellationToken)
            .ConfigureAwait(false);

        var domains = new Dictionary<string, ResolvedFieldConstraints>(StringComparer.Ordinal);
        if (_fieldDomains is not null || recordType?.FieldBindings is not null)
        {
            if (_fieldDomains is null || recordType?.FieldBindings is null || recordType.Tenant != request.Tenant
                || definition.Tenant != request.Tenant || recordType.RecordType != definition.RecordType
                || string.IsNullOrWhiteSpace(recordType.SchemaRef))
                throw BindingRefusal("");
            foreach (var column in definition.Parameters.Columns)
            {
                var pointer = "/" + column.Field.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                if (!recordType.FieldBindings.TryGetValue(column.Field, out var field)) throw BindingRefusal(pointer);
                var roles = field.Constraints.ReadRoleIds.Select(RoleReference.Domain).ToArray();
                if (roles.Length > 0 && (request.RoleVocabulary is null || request.HeldRoles is null
                    || !RoleGateResolver.Allows(new RoleGate(roles), request.RoleVocabulary, request.HeldRoles))) continue;
                var resolved = await _fieldDomains.NarrowAsync(field.Constraints, field.Constraints,
                    new(new(request.Tenant), request.Principal), pointer, cancellationToken);
                if (resolved.Values is not null) domains[column.Field] = resolved;
            }
        }

        var predicates = new List<ViewQueryPredicate>
        {
            new(ViewPredicateSource.Access, access),
        };
        if (definition.Parameters.Filter is { } authored)
        {
            predicates.Add(new(ViewPredicateSource.Authored, authored));
        }

        var plan = new ViewQueryPlan(
            request.Tenant,
            definition.RecordType,
            recordType!.Fields,
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
                .EvaluateAsync(binding, page.CurrentRows, evaluatedAt, request.Tenant, request.Principal, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var eTag = ComputeETag(definition, request, authority, page, measure);
        // ponytail: the row page and measure are still computed before the comparison — this trims the response body, not the query cost. Upgrade path: a per-view/per-tenant write-stamp checked before running the row source, if query cost ever matters more than bandwidth.
        var notModified = !string.IsNullOrEmpty(eTag)
            && string.Equals(eTag, request.IfNoneMatch, StringComparison.Ordinal);
        return new(
            notModified ? [] : page.Rows,
            page.Total,
            notModified ? [] : page.Groups,
            notModified ? null : measure,
            authority,
            evaluatedAt)
        {
            ETag = eTag,
            NotModified = notModified,
            ColumnDomains = domains,
        };
    }

    private static string ComputeETag(
        ViewDefinition definition,
        ViewQueryRequest request,
        ViewAuthority authority,
        ViewRowPage page,
        ViewMeasureResult? measure)
    {
        var canonical = new
        {
            Definition = new
            {
                definition.Envelope.Identity,
                definition.Version,
            },
            Query = new
            {
                Page = new { request.Page.Offset, request.Page.Limit },
                Binding = new
                {
                    request.Binding.Kind,
                    ShapeRoles = request.Binding.ShapeRoles
                        .OrderBy(role => role.Key)
                        .Select(role => new { Role = role.Key, role.Value }),
                    RowBehavior = request.Binding.RowBehavior is { } rowBehavior
                        ? new { rowBehavior.OpenAction, rowBehavior.InlineEdit }
                        : null,
                    request.Binding.Density,
                    Widget = request.Binding.Widget is { } widget
                        ? new
                        {
                            widget.Widget,
                            Parameters = widget.Parameters
                                .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
                                .Select(parameter => new { parameter.Key, parameter.Value }),
                        }
                        : null,
                    request.Binding.BoardMoveTransition,
                },
            },
            Authority = new
            {
                authority.CanOpen,
                Actions = authority.Actions
                    .OrderBy(action => action.Action, StringComparer.Ordinal)
                    .ThenBy(action => action.Allowed)
                    .Select(action => new { action.Action, action.Allowed }),
            },
            Result = new
            {
                Rows = page.Rows.Select(row => new
                {
                    row.Id,
                    Values = row.Values
                        .OrderBy(value => value.Key, StringComparer.Ordinal)
                        .Select(value => new { value.Key, value.Value }),
                }),
                page.Total,
                Groups = page.Groups.Select(group => new { group.Key, group.Count }),
                Measure = measure is { } result ? new { result.Name, result.Value } : null,
            },
        };
        var serialized = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexString(SHA256.HashData(serialized));
    }

    private static FieldAdmissionException BindingRefusal(string pointer)
        => new([new("field.binding_unresolved", pointer, "The selected column has no admitted field binding.")]);
}
