using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Evaluation;
using RuleError = Harborline.Foundation.RuleEngine.Model.RuleError;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.References;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>
/// The binding kind named by a refusal (DES-0052 layout-run-4). The spelling is the
/// definition's own <c>binding_kind</c> discriminator, so a refusal names the authored kind
/// rather than a runtime alias.
/// </summary>
public static class LayoutBindingKinds
{
    /// <summary>layout-ck-21: a field path resolved by the field runtime.</summary>
    public const string RecordField = "record_field";

    /// <summary>layout-ck-22: a named <c>ViewDefinition</c>.</summary>
    public const string Query = "query";

    /// <summary>layout-ck-23: a catalogue measure named by stable path.</summary>
    public const string Measure = "measure";

    /// <summary>layout-ck-24: a named <c>TemplateDefinition</c>.</summary>
    public const string Template = "template";

    /// <summary>layout-ck-25: content authored on the block itself.</summary>
    public const string Static = "static";

    /// <summary>layout-ck-43: text composed of literal and record-field runs.</summary>
    public const string Text = "text";

    /// <summary>Names the authored kind of one binding.</summary>
    public static string Of(LayoutBinding binding) => binding switch
    {
        LayoutRecordFieldBinding => RecordField,
        LayoutQueryBinding => Query,
        LayoutMeasureBinding => Measure,
        LayoutTemplateBinding => Template,
        LayoutStaticBinding => Static,
        LayoutTextBinding => Text,
        _ => "unknown",
    };

    /// <summary>Names the authored identity a binding resolves by.</summary>
    public static string NameOf(LayoutBinding binding) => binding switch
    {
        LayoutRecordFieldBinding value => value.FieldPath,
        LayoutQueryBinding value => value.ViewDefinitionId,
        LayoutMeasureBinding value => value.MeasurePath,
        LayoutTemplateBinding value => value.TemplateDefinitionId,
        LayoutStaticBinding => Static,
        LayoutTextBinding => Text,
        _ => string.Empty,
    };
}

/// <summary>
/// The named results Layout places. Layout owns geometry only: it consumes each source's
/// already-produced result and never captures a field itself (DES-0052 layout-eng-28, Forms
/// retains field-runtime-cc-2), never computes a measure (layout-eng-29, Reports retains
/// measure-catalogue-cc-6), and never runs a query (Views keeps the query).
/// </summary>
/// <remarks>
/// Every <c>TryResolve</c> member returns <see langword="false"/> for a name the source cannot
/// resolve, and the resolver turns that into one <see cref="LayoutBindingRefusal"/>; a source
/// never throws to signal an unresolvable name. <see cref="ResolveRelated"/> reports its outcome.
/// </remarks>
public interface ILayoutBindingSources
{
    /// <summary>
    /// Reads one record field for the record in <paramref name="scope"/>, under the acting principal.
    /// A declared field with no value is <see cref="LayoutFieldResult.Resolved"/> with a null value; a
    /// field the principal may not read is <see cref="LayoutFieldResult.Denied"/>, which the resolver
    /// renders exactly as the missing value and reports only to the protected trace (layout-auth-25,
    /// layout-eng-31). The same observational-indistinguishability duty as <see cref="ResolveRelated"/>
    /// applies; an unknown name is <see cref="LayoutFieldResult.Undeclared"/>.
    /// </summary>
    LayoutFieldResult ResolveField(LayoutBindingScope scope, string fieldPath);

    /// <summary>Resolves one named query's result.</summary>
    bool TryResolveQuery(LayoutBindingScope scope, string viewDefinitionId, out JsonNode? value);

    /// <summary>Resolves one catalogue measure's already-computed result by stable path.</summary>
    bool TryResolveMeasure(LayoutBindingScope scope, string measurePath, out JsonNode? value);

    /// <summary>Resolves one named template definition.</summary>
    bool TryResolveTemplate(LayoutBindingScope scope, string templateDefinitionId, out JsonNode? value);

    /// <summary>Resolves the rows a repeating block iterates, in authored order.</summary>
    bool TryResolveCollection(LayoutBindingScope scope, string name, out IReadOnlyList<JsonNode?> rows);

    /// <summary>
    /// Traverses one declared Records relationship to the second record's scope, under the acting
    /// principal. Missing and denied are distinct here so the denial can reach the protected trace;
    /// the resolver makes them identical to the viewer (layout-eng-31). The implementation must keep
    /// them observationally indistinguishable to an unauthorized caller, including response shape,
    /// status or error classification where applicable, and externally observable timing; report a
    /// denial only through <see cref="LayoutRelatedResult.Denied"/>.
    /// </summary>
    LayoutRelatedResult ResolveRelated(LayoutBindingScope scope, string relationship);
}

/// <summary>How one related-block traversal ended.</summary>
public enum LayoutRelatedOutcome
{
    /// <summary>The related record resolved.</summary>
    Resolved,
    /// <summary>The relationship is declared and has no target for this record.</summary>
    Absent,
    /// <summary>A target exists and the acting principal may not observe it.</summary>
    Denied,
    /// <summary>The relationship is not declared: an authoring fault, refused by name.</summary>
    Undeclared,
}

/// <summary>The result of one related-block traversal.</summary>
/// <param name="Outcome">How the traversal ended.</param>
/// <param name="Scope">The related record's scope when <see cref="LayoutRelatedOutcome.Resolved"/>.</param>
/// <param name="DenialCode">The access decision's stable code when <see cref="LayoutRelatedOutcome.Denied"/>.</param>
/// <param name="DenialPointer">The access decision's pointer when <see cref="LayoutRelatedOutcome.Denied"/>.</param>
/// <param name="DeniedTarget">The record the principal was denied, when <see cref="LayoutRelatedOutcome.Denied"/>.</param>
public readonly record struct LayoutRelatedResult(
    LayoutRelatedOutcome Outcome,
    LayoutBindingScope Scope = default,
    string? DenialCode = null,
    string? DenialPointer = null,
    LayoutRecordReference? DeniedTarget = null)
{
    /// <summary>A declared relationship with no target.</summary>
    public static LayoutRelatedResult Absent => new(LayoutRelatedOutcome.Absent);

    /// <summary>An undeclared relationship.</summary>
    public static LayoutRelatedResult Undeclared => new(LayoutRelatedOutcome.Undeclared);

    /// <summary>A resolved related record.</summary>
    public static LayoutRelatedResult Resolved(LayoutBindingScope scope) => new(LayoutRelatedOutcome.Resolved, scope);

    /// <summary>A target the acting principal may not observe.</summary>
    public static LayoutRelatedResult Denied(string code, string pointer, LayoutRecordReference target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(pointer);
        ArgumentNullException.ThrowIfNull(target);
        return new(LayoutRelatedOutcome.Denied, default, code, pointer, target);
    }
}

/// <summary>
/// DES-0052 layout-run-5 — one related-binding denial, keyed by authored block and relationship
/// plus the request. It names the acting principal and the denied record so the host can
/// authorize a reader against both the trace and that record. Never part of the viewer's resolution.
/// </summary>
public sealed record LayoutRelatedDenial(
    string RequestId,
    string PrincipalId,
    string BlockId,
    string BindingKind,
    string RelationshipKey,
    LayoutRecordReference Target,
    string Code,
    string Pointer);

/// <summary>How one record-field read ended.</summary>
public enum LayoutFieldOutcome
{
    /// <summary>The field was read; its value may be null (missing).</summary>
    Resolved,
    /// <summary>The acting principal may not read the field.</summary>
    Denied,
    /// <summary>The name is not a field of this record: an authoring fault, refused by name.</summary>
    Undeclared,
}

/// <summary>The result of one record-field read.</summary>
/// <param name="Outcome">How the read ended.</param>
/// <param name="Value">The field's value when <see cref="LayoutFieldOutcome.Resolved"/>; null when it is missing.</param>
/// <param name="DenialCode">The access decision's stable code when <see cref="LayoutFieldOutcome.Denied"/>.</param>
/// <param name="DenialPointer">The access decision's pointer when <see cref="LayoutFieldOutcome.Denied"/>.</param>
/// <param name="DeniedRecord">The record whose field was denied, when <see cref="LayoutFieldOutcome.Denied"/>.</param>
public readonly record struct LayoutFieldResult(
    LayoutFieldOutcome Outcome,
    JsonNode? Value = null,
    string? DenialCode = null,
    string? DenialPointer = null,
    LayoutRecordReference? DeniedRecord = null)
{
    /// <summary>A name that is not a field of this record.</summary>
    public static LayoutFieldResult Undeclared => new(LayoutFieldOutcome.Undeclared);

    /// <summary>A field read, whose value may be null.</summary>
    public static LayoutFieldResult Resolved(JsonNode? value) => new(LayoutFieldOutcome.Resolved, value);

    /// <summary>A field the acting principal may not read.</summary>
    public static LayoutFieldResult Denied(string code, string pointer, LayoutRecordReference record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(pointer);
        ArgumentNullException.ThrowIfNull(record);
        return new(LayoutFieldOutcome.Denied, null, code, pointer, record);
    }
}

/// <summary>
/// DES-0052 layout-auth-25, layout-run-5 — one record-field read denial, keyed by authored block and
/// field plus the request. Never part of the viewer's resolution.
/// </summary>
public sealed record LayoutFieldDenial(
    string RequestId,
    string PrincipalId,
    string BlockId,
    string FieldPath,
    LayoutRecordReference Record,
    string Code,
    string Pointer);

/// <summary>A typed reference to one record: its Records type and its identity.</summary>
public sealed record LayoutRecordReference(string RecordTypeId, string RecordId);

/// <summary>The request a resolution runs for: its identity and the acting principal.</summary>
public sealed record LayoutResolutionRequest(string RequestId, string PrincipalId);

/// <summary>
/// The host's protected decision trace. Layout only writes to it: the host owns the store and every
/// reader (owner ruling 2026-09-24, R-0115).
/// </summary>
public interface ILayoutDecisionTrace
{
    /// <summary>Records one related-binding denial.</summary>
    void RecordDenial(LayoutRelatedDenial denial);

    /// <summary>Records one record-field read denial (layout-auth-25).</summary>
    void RecordFieldDenial(LayoutFieldDenial denial);
}

/// <summary>
/// One resolution scope: the surface root, or one row of a repeating collection. A repeating
/// block's children resolve in a fresh scope per row (DES-0052 layout-eng-17, layout-run-2), so
/// a row never reads a sibling row's values.
/// </summary>
/// <param name="Section">The repeating collection this scope belongs to, or <see langword="null"/> at the root.</param>
/// <param name="RowId">The row identity within <paramref name="Section"/>, or <see langword="null"/> at the root.</param>
/// <param name="Values">The values addressable in this scope.</param>
public readonly record struct LayoutBindingScope(
    string? Section,
    string? RowId,
    IReadOnlyDictionary<string, JsonNode?> Values)
{
    /// <summary>The surface-root scope over <paramref name="values"/>.</summary>
    public static LayoutBindingScope Root(IReadOnlyDictionary<string, JsonNode?> values) => new(null, null, values);

    /// <summary>Whether this scope is one row of a repeating collection.</summary>
    public bool IsRow => Section is not null;
}

/// <summary>Stable codes a <see cref="LayoutBindingRefusal"/> carries.</summary>
public static class LayoutBindingRefusalCodes
{
    /// <summary>layout-run-4: the source cannot resolve the authored name.</summary>
    public const string Unresolvable = "layout.binding.unresolvable";

    /// <summary>layout-ck-40: the collection's row count lies outside the block's bounds.</summary>
    public const string CollectionOutOfBounds = "layout.collection.out_of_bounds";
}

/// <summary>
/// DES-0052 layout-run-4 — one unresolvable binding, reported by block and binding kind when
/// the surface is resolved. The surface still resolves: every other block keeps its result, so
/// one bad name does not blank a screen.
/// </summary>
/// <param name="BlockId">The definition-local block whose binding did not resolve.</param>
/// <param name="BindingKind">The authored binding kind, spelled as <see cref="LayoutBindingKinds"/>.</param>
/// <param name="Name">The authored name that did not resolve.</param>
/// <param name="RowId">The repeating row the refusal occurred in, or <see langword="null"/> at the root.</param>
/// <param name="Code">The stable refusal code, one of <see cref="LayoutBindingRefusalCodes"/>.</param>
public sealed record LayoutBindingRefusal(
    string BlockId,
    string BindingKind,
    string Name,
    string? RowId = null,
    string Code = LayoutBindingRefusalCodes.Unresolvable)
{
    /// <summary>The refusal text, naming the block and the binding kind.</summary>
    public string Message => (Code, RowId) switch
    {
        (LayoutBindingRefusalCodes.CollectionOutOfBounds, _) => $"Block '{BlockId}' collection '{Name}' has a row count outside its bounds; no rows were placed.",
        (_, null) => $"Block '{BlockId}' has an unresolvable {BindingKind} binding '{Name}'.",
        _ => $"Block '{BlockId}' (row '{RowId}') has an unresolvable {BindingKind} binding '{Name}'.",
    };
}

/// <summary>
/// DES-0052 layout-run-2 — one block resolved in one scope. A repeating block's child appears
/// once per row, each carrying that row's identity.
/// </summary>
/// <param name="BlockId">The definition-local block identifier.</param>
/// <param name="Kind">The released component kind.</param>
/// <param name="BindingKind">The authored binding kind, spelled as <see cref="LayoutBindingKinds"/>.</param>
/// <param name="Name">The authored name this block resolved by.</param>
/// <param name="Value">The source's result. Layout places it and derives nothing from it.</param>
/// <param name="RowId">The repeating row this instance belongs to, or <see langword="null"/> at the root.</param>
public sealed record LayoutResolvedBlock(
    string BlockId,
    string Kind,
    string BindingKind,
    string Name,
    JsonNode? Value,
    string? RowId = null);

/// <summary>The resolved surface: what placed, what was hidden by a guard, and what refused.</summary>
/// <param name="Blocks">The blocks that resolved, in authored order.</param>
/// <param name="Refusals">One entry per unresolvable binding.</param>
/// <param name="Hidden">The block identifiers a <c>show_when</c> guard withheld.</param>
public sealed record LayoutBindingResolution(
    IReadOnlyList<LayoutResolvedBlock> Blocks,
    IReadOnlyList<LayoutBindingRefusal> Refusals,
    IReadOnlyList<string> Hidden);

/// <summary>
/// Maps one Layout scope into the rule engine's scope grammar (ADR 0146 D3): the third
/// concrete <see cref="IContextAdapter"/>, beside the forms and workflow-guard contexts. A
/// <c>row.</c> reference resolves only within the row scope it was produced for, so a guard
/// cannot read across rows.
/// </summary>
public sealed class LayoutSurfaceContextAdapter : IContextAdapter
{
    private readonly LayoutBindingScope _root;
    private readonly LayoutBindingScope _current;

    /// <summary>Creates the adapter for one root and the scope currently being resolved.</summary>
    public LayoutSurfaceContextAdapter(LayoutBindingScope root, LayoutBindingScope current)
    {
        _root = root;
        _current = current;
    }

    /// <inheritdoc />
    public IValueResolver CreateResolver(RuleEvalScope scope) => new LayoutValueResolver(_root, _current, scope);
}

/// <summary>
/// Resolves a guard's <c>var</c> references against one Layout scope. <c>row.</c> addresses the
/// row scope the resolver was produced for and nothing else; <c>parent.</c> and <c>field.</c>
/// address the surface root. A name the scope does not carry is a bad reference, which the
/// shared evaluator turns into a fail-closed verdict.
/// </summary>
internal sealed class LayoutValueResolver : IValueResolver
{
    private readonly LayoutBindingScope _root;
    private readonly LayoutBindingScope _current;
    private readonly RuleEvalScope _scope;

    public LayoutValueResolver(LayoutBindingScope root, LayoutBindingScope current, RuleEvalScope scope)
    {
        _root = root;
        _current = current;
        _scope = scope;
    }

    public RefValue ResolveVar(string path)
    {
        if (string.IsNullOrEmpty(path)) return BadReference(path);

        if (path.StartsWith("row.", StringComparison.Ordinal))
        {
            // A row reference is addressable only inside the row scope this resolver was made
            // for, and only for that row's own section. Cross-row lookup is a bad reference.
            if (!_current.IsRow) return BadReference(path);
            if (_scope.RowSection is { } section && !string.Equals(section, _current.Section, StringComparison.Ordinal))
                return BadReference(path);
            if (_scope.RowId is { } rowId && !string.Equals(rowId, _current.RowId, StringComparison.Ordinal))
                return BadReference(path);
            return Read(_current.Values, path["row.".Length..], path);
        }

        // The compiler already lowers parent.<f> and section.<id>.<f> to field.<f>, so by the
        // time a path reaches here "field." means the surface root, exactly as it means the
        // top-level instance for forms.
        if (path.StartsWith("field.", StringComparison.Ordinal))
            return Read(_root.Values, path["field.".Length..], path);

        // An unqualified name reads the current scope first, then the surface root.
        return _current.Values.ContainsKey(path) ? Read(_current.Values, path, path) : Read(_root.Values, path, path);
    }

    // Layout places an already-produced result and never aggregates one itself: a measure is
    // Reports' (layout-eng-29) and a query is Views'. Every aggregate is unavailable data.
    public RefValue ResolveAgg(string fn, string section, string col) => RefValue.UnavailableAggregate(fn, section, col);

    private static RefValue Read(IReadOnlyDictionary<string, JsonNode?> values, string name, string path)
        => values.TryGetValue(name, out var value) ? RefValue.Resolved(value) : BadReference(path);

    private static RefValue BadReference(string path) => RefValue.OfError(RuleError.Of(RuleEngineCodes.BadReference, "path", path));
}

/// <summary>
/// Resolves every binding on an admitted Layout surface by name, refuses an unresolvable one
/// naming the block and the binding kind (DES-0052 layout-eng-14, layout-run-4), narrows scope
/// per repeating row (layout-eng-17, layout-run-2), and evaluates every <c>show_when</c> guard
/// fail-closed through the shared rule engine (layout-eng-16).
/// </summary>
/// <remarks>
/// Layout derives no policy here. It does not capture a field, compute a measure, run a query,
/// mint an issued artefact or widen an authorized source: each result arrives already produced
/// through <see cref="ILayoutBindingSources"/>, and Layout only places it.
/// </remarks>
public sealed class LayoutBindingResolver
{
    private readonly GuardEvaluator _guards;
    private readonly PinnedClosure? _predicates;

    /// <summary>Creates a resolver over the caller-supplied shared rule engine evaluator.</summary>
    /// <param name="guards">The evaluator bound to the caller's business clock.</param>
    /// <param name="predicates">The pinned closure the definition was published with; a <c>show_when</c> predicate resolves only here (layout-ck-29). Absent, every predicate guard withholds its block.</param>
    public LayoutBindingResolver(GuardEvaluator guards, PinnedClosure? predicates = null)
    {
        _guards = guards ?? throw new ArgumentNullException(nameof(guards));
        _predicates = predicates;
    }

    /// <summary>
    /// Resolves one admitted definition against one root scope for <paramref name="request"/>,
    /// writing each related-binding denial to <paramref name="trace"/> (layout-run-5).
    /// </summary>
    public LayoutBindingResolution Resolve(
        LayoutDefinition definition,
        ILayoutBindingSources sources,
        LayoutBindingScope root,
        ILayoutDecisionTrace trace,
        LayoutResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(sources);
        // Denial evidence is mandatory (layout-run-5): no trace or request, no resolution.
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PrincipalId);
        var denials = new DenialSink(trace, request);

        var blocks = new List<LayoutResolvedBlock>();
        var refusals = new List<LayoutBindingRefusal>();
        var hidden = new List<string>();
        foreach (var block in definition.Blocks ?? [])
            Walk(block, sources, root, root, blocks, refusals, hidden, denials, cancellationToken);
        return new(blocks.AsReadOnly(), refusals.AsReadOnly(), hidden.AsReadOnly());
    }

    private void Walk(
        LayoutBlock block,
        ILayoutBindingSources sources,
        LayoutBindingScope root,
        LayoutBindingScope scope,
        ICollection<LayoutResolvedBlock> blocks,
        ICollection<LayoutBindingRefusal> refusals,
        ICollection<string> hidden,
        DenialSink denials,
        CancellationToken cancellationToken)
    {
        // A guard is evaluated before the binding: a withheld block resolves nothing, so a
        // hidden block's unresolvable name is not reported as a refusal the author cannot see.
        if (!IsVisible(block, root, scope, cancellationToken))
        {
            hidden.Add(block.Id);
            return;
        }

        // A related block traverses a declared Records relationship and observes the second
        // record: its subtree resolves in the related scope (layout-auth-19).
        var childScope = scope;
        if (block.RelatedRelationship is { Length: > 0 } relationship)
        {
            var related = sources.ResolveRelated(scope, relationship);
            switch (related.Outcome)
            {
                case LayoutRelatedOutcome.Resolved:
                    childScope = related.Scope;
                    break;
                case LayoutRelatedOutcome.Denied:
                    // layout-eng-31: the viewer sees exactly what an absent target shows — nothing,
                    // with no refusal, marker or correlation id. Only the protected trace learns why.
                    denials.Record(block, relationship, related);
                    return;
                case LayoutRelatedOutcome.Absent:
                    return;
                default:
                    refusals.Add(new(block.Id, LayoutBindingKinds.Of(block.Binding), relationship, scope.RowId));
                    return;
            }
        }

        if (block.Repeating)
        {
            RepeatChildren(block, sources, root, childScope, blocks, refusals, hidden, denials, cancellationToken);
            return;
        }

        Place(block, sources, childScope, blocks, refusals, denials);
        foreach (var child in block.Children ?? [])
            Walk(child, sources, root, childScope, blocks, refusals, hidden, denials, cancellationToken);
    }

    private void RepeatChildren(
        LayoutBlock block,
        ILayoutBindingSources sources,
        LayoutBindingScope root,
        LayoutBindingScope scope,
        ICollection<LayoutResolvedBlock> blocks,
        ICollection<LayoutBindingRefusal> refusals,
        ICollection<string> hidden,
        DenialSink denials,
        CancellationToken cancellationToken)
    {
        var kind = LayoutBindingKinds.Of(block.Binding);
        var name = LayoutBindingKinds.NameOf(block.Binding);
        if (!sources.TryResolveCollection(scope, name, out var rows))
        {
            refusals.Add(new(block.Id, kind, name, scope.RowId));
            return;
        }

        // layout-ck-40: a count outside the bounds refuses the whole block. Placing the rows that
        // fit would be a truncated, unmarked partial, which the ruling forbids.
        if (block.CollectionBounds is { } bounds && !bounds.Contains(rows.Count))
        {
            refusals.Add(new(block.Id, kind, name, scope.RowId, LayoutBindingRefusalCodes.CollectionOutOfBounds));
            return;
        }

        blocks.Add(new(block.Id, block.Kind, kind, name, null, scope.RowId));
        var index = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One fresh scope per row: the row's own values only, so a child resolved for row 1
            // can never read row 2 (layout-eng-17).
            var rowId = RowIdOf(row, index);
            var rowScope = new LayoutBindingScope(name, rowId, ValuesOf(row));
            foreach (var child in block.Children ?? [])
                Walk(child, sources, root, rowScope, blocks, refusals, hidden, denials, cancellationToken);
            index++;
        }
    }

    private static void Place(
        LayoutBlock block,
        ILayoutBindingSources sources,
        LayoutBindingScope scope,
        ICollection<LayoutResolvedBlock> blocks,
        ICollection<LayoutBindingRefusal> refusals,
        DenialSink denials)
    {
        var kind = LayoutBindingKinds.Of(block.Binding);
        var name = LayoutBindingKinds.NameOf(block.Binding);
        JsonNode? value;
        var resolved = block.Binding switch
        {
            // Static content is authored on the block; nothing is looked up (layout-ck-25).
            LayoutStaticBinding content => Static(content, out value),
            LayoutRecordFieldBinding field => ReadField(block, sources, scope, field.FieldPath, denials, out value),
            LayoutTextBinding text => Compose(block, text, sources, scope, denials, out value, ref name),
            LayoutQueryBinding query => sources.TryResolveQuery(scope, query.ViewDefinitionId, out value),
            LayoutMeasureBinding measure => sources.TryResolveMeasure(scope, measure.MeasurePath, out value),
            LayoutTemplateBinding template => sources.TryResolveTemplate(scope, template.TemplateDefinitionId, out value),
            _ => Unresolved(out value),
        };

        if (resolved) blocks.Add(new(block.Id, block.Kind, kind, name, value, scope.RowId));
        else refusals.Add(new(block.Id, kind, name, scope.RowId));
    }

    // layout-auth-25, layout-eng-31: a field the principal may not read renders exactly as a missing
    // one, a resolved null, and only the protected trace learns why.
    private static bool ReadField(LayoutBlock block, ILayoutBindingSources sources, LayoutBindingScope scope, string fieldPath, DenialSink denials, out JsonNode? value)
    {
        var read = sources.ResolveField(scope, fieldPath);
        value = read.Outcome == LayoutFieldOutcome.Resolved ? read.Value : null;
        if (read.Outcome == LayoutFieldOutcome.Denied) denials.RecordField(block, fieldPath, read);
        return read.Outcome != LayoutFieldOutcome.Undeclared;
    }

    // layout-ck-43: the runs compose in order. layout-ck-44: a field run whose value is absent (null
    // or missing, or denied, which looks missing) shows its fallback; an empty string is a value.
    // An undeclared field refuses the block by that field's name.
    private static bool Compose(LayoutBlock block, LayoutTextBinding text, ILayoutBindingSources sources, LayoutBindingScope scope, DenialSink denials, out JsonNode? value, ref string name)
    {
        value = null;
        var composed = new System.Text.StringBuilder();
        foreach (var run in text.Runs)
        {
            if (run.FieldPath is not { } path)
            {
                composed.Append(run.Text);
                continue;
            }
            if (!ReadField(block, sources, scope, path, denials, out var field))
            {
                name = path;
                return false;
            }
            composed.Append(field is null || field.GetValueKind() == System.Text.Json.JsonValueKind.Null ? run.Fallback : field.ToString());
        }
        value = JsonValue.Create(composed.ToString());
        return true;
    }

    private static bool Static(LayoutStaticBinding binding, out JsonNode? value)
    {
        value = JsonNode.Parse(binding.Content.GetRawText());
        return true;
    }

    private static bool Unresolved(out JsonNode? value)
    {
        value = null;
        return false;
    }

    private bool IsVisible(LayoutBlock block, LayoutBindingScope root, LayoutBindingScope scope, CancellationToken cancellationToken)
    {
        // layout-ck-29: an absent guard is no guard; a declared one must hold and fails closed.
        if (block.ShowWhen is not { } guard) return true;

        // The guard is Rules' grammar, evaluated by the shared engine and never by a
        // layout-local conditional (layout-eng-16, layout-auth-20). A block inside a repeating
        // container is a Row-scoped rule over that container's section, which is what makes a
        // `row.` reference legal there and illegal anywhere else.
        // The same rule publication compiled (T-724 ruling 39). A guard holding neither form or
        // both, or a predicate pin the closure cannot resolve, withholds the block.
        RuleDefinition rule;
        try
        {
            rule = LayoutGuardRule.For(block.Id, guard, scope.IsRow ? scope.Section : null, _predicates);
        }
        catch (NamedReferenceException)
        {
            return false;
        }
        var evalScope = scope.IsRow ? new RuleEvalScope(scope.Section, scope.RowId) : RuleEvalScope.Root;
        // Capture the layout producer's values before entering Rules. The evaluator never calls
        // a layout resolver (which could be arbitrary host code) during pure evaluation.
        var snapshot = RuleContextSnapshot.Capture(root.Values, scope.IsRow ? scope.Values : null);
        try
        {
            // Evaluation is already fail-closed: a pending, errored or budget-aborted guard is
            // Invalid. Compilation is NOT — the compiler throws on a malformed expression or a
            // reference illegal at this scope — so an uncompilable guard withholds the block
            // here rather than escaping as a fault that would blank the whole surface.
            return _guards.EvaluateGuard(rule, snapshot, evalScope, LayoutExpressionEnvironment.Admitted.For(EvaluationPhase.Render), cancellationToken).Ok;
        }
        catch (RuleCompilationException)
        {
            return false;
        }
        catch (RuleEngineTimeoutException)
        {
            return false;
        }
    }

    private readonly record struct DenialSink(ILayoutDecisionTrace Trace, LayoutResolutionRequest Request)
    {
        public void Record(LayoutBlock block, string relationship, LayoutRelatedResult related)
        {
            // A source that reports a denial without its evidence is a host defect; losing the
            // evidence silently is what layout-run-5 forbids, so it faults instead.
            if (related.DeniedTarget is not { } target
                || string.IsNullOrWhiteSpace(related.DenialCode)
                || string.IsNullOrWhiteSpace(related.DenialPointer))
                throw new InvalidOperationException(
                    $"The binding source denied '{relationship}' on block '{block.Id}' without a target, code and pointer; use LayoutRelatedResult.Denied.");
            Trace.RecordDenial(new(Request.RequestId, Request.PrincipalId, block.Id, LayoutBindingKinds.Of(block.Binding),
                relationship, target, related.DenialCode, related.DenialPointer));
        }

        public void RecordField(LayoutBlock block, string fieldPath, LayoutFieldResult read)
        {
            if (read.DeniedRecord is not { } record
                || string.IsNullOrWhiteSpace(read.DenialCode)
                || string.IsNullOrWhiteSpace(read.DenialPointer))
                throw new InvalidOperationException(
                    $"The binding source denied field '{fieldPath}' on block '{block.Id}' without a record, code and pointer; use LayoutFieldResult.Denied.");
            Trace.RecordFieldDenial(new(Request.RequestId, Request.PrincipalId, block.Id, fieldPath, record, read.DenialCode, read.DenialPointer));
        }
    }

    private static string RowIdOf(JsonNode? row, int index)
        => row is JsonObject obj && obj.TryGetPropertyValue("id", out var id) && id is not null
            ? id.ToString()
            : index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, JsonNode?> ValuesOf(JsonNode? row)
        => row is JsonObject obj
            ? obj.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
}
