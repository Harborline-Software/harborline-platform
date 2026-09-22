using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Evaluation;
using RuleError = Harborline.Foundation.RuleEngine.Model.RuleError;

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

    /// <summary>Names the authored kind of one binding.</summary>
    public static string Of(LayoutBinding binding) => binding switch
    {
        LayoutRecordFieldBinding => RecordField,
        LayoutQueryBinding => Query,
        LayoutMeasureBinding => Measure,
        LayoutTemplateBinding => Template,
        LayoutStaticBinding => Static,
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
/// Every member returns <see langword="false"/> for a name the source cannot resolve. The
/// resolver turns that into one <see cref="LayoutBindingRefusal"/>; a source never throws to
/// signal an unresolvable name.
/// </remarks>
public interface ILayoutBindingSources
{
    /// <summary>Resolves one record-field result for the record in <paramref name="scope"/>.</summary>
    bool TryResolveField(LayoutBindingScope scope, string fieldPath, out JsonNode? value);

    /// <summary>Resolves one named query's result.</summary>
    bool TryResolveQuery(LayoutBindingScope scope, string viewDefinitionId, out JsonNode? value);

    /// <summary>Resolves one catalogue measure's already-computed result by stable path.</summary>
    bool TryResolveMeasure(LayoutBindingScope scope, string measurePath, out JsonNode? value);

    /// <summary>Resolves one named template definition.</summary>
    bool TryResolveTemplate(LayoutBindingScope scope, string templateDefinitionId, out JsonNode? value);

    /// <summary>Resolves the rows a repeating block iterates, in authored order.</summary>
    bool TryResolveCollection(LayoutBindingScope scope, string name, out IReadOnlyList<JsonNode?> rows);

    /// <summary>Traverses one declared Records relationship to the second record's scope.</summary>
    bool TryResolveRelated(LayoutBindingScope scope, string relationship, out LayoutBindingScope related);
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

/// <summary>
/// DES-0052 layout-run-4 — one unresolvable binding, reported by block and binding kind when
/// the surface is resolved. The surface still resolves: every other block keeps its result, so
/// one bad name does not blank a screen.
/// </summary>
/// <param name="BlockId">The definition-local block whose binding did not resolve.</param>
/// <param name="BindingKind">The authored binding kind, spelled as <see cref="LayoutBindingKinds"/>.</param>
/// <param name="Name">The authored name that did not resolve.</param>
/// <param name="RowId">The repeating row the refusal occurred in, or <see langword="null"/> at the root.</param>
public sealed record LayoutBindingRefusal(string BlockId, string BindingKind, string Name, string? RowId = null)
{
    /// <summary>The refusal text, naming the block and the binding kind.</summary>
    public string Message => RowId is null
        ? $"Block '{BlockId}' has an unresolvable {BindingKind} binding '{Name}'."
        : $"Block '{BlockId}' (row '{RowId}') has an unresolvable {BindingKind} binding '{Name}'.";
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

    /// <summary>Creates a resolver over the shared rule engine's guard evaluator.</summary>
    /// <param name="guards">The shared evaluator; the default instance when omitted.</param>
    public LayoutBindingResolver(GuardEvaluator? guards = null) => _guards = guards ?? new GuardEvaluator();

    /// <summary>Resolves one admitted definition against one root scope.</summary>
    public LayoutBindingResolution Resolve(
        LayoutDefinition definition,
        ILayoutBindingSources sources,
        LayoutBindingScope root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(sources);

        var blocks = new List<LayoutResolvedBlock>();
        var refusals = new List<LayoutBindingRefusal>();
        var hidden = new List<string>();
        foreach (var block in definition.Blocks ?? [])
            Walk(block, sources, root, root, blocks, refusals, hidden, cancellationToken);
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
            if (!sources.TryResolveRelated(scope, relationship, out var related))
            {
                refusals.Add(new(block.Id, LayoutBindingKinds.Of(block.Binding), relationship, scope.RowId));
                return;
            }
            childScope = related;
        }

        if (block.Repeating)
        {
            RepeatChildren(block, sources, root, childScope, blocks, refusals, hidden, cancellationToken);
            return;
        }

        Place(block, sources, childScope, blocks, refusals);
        foreach (var child in block.Children ?? [])
            Walk(child, sources, root, childScope, blocks, refusals, hidden, cancellationToken);
    }

    private void RepeatChildren(
        LayoutBlock block,
        ILayoutBindingSources sources,
        LayoutBindingScope root,
        LayoutBindingScope scope,
        ICollection<LayoutResolvedBlock> blocks,
        ICollection<LayoutBindingRefusal> refusals,
        ICollection<string> hidden,
        CancellationToken cancellationToken)
    {
        var kind = LayoutBindingKinds.Of(block.Binding);
        var name = LayoutBindingKinds.NameOf(block.Binding);
        if (!sources.TryResolveCollection(scope, name, out var rows))
        {
            refusals.Add(new(block.Id, kind, name, scope.RowId));
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
                Walk(child, sources, root, rowScope, blocks, refusals, hidden, cancellationToken);
            index++;
        }
    }

    private static void Place(
        LayoutBlock block,
        ILayoutBindingSources sources,
        LayoutBindingScope scope,
        ICollection<LayoutResolvedBlock> blocks,
        ICollection<LayoutBindingRefusal> refusals)
    {
        var kind = LayoutBindingKinds.Of(block.Binding);
        var name = LayoutBindingKinds.NameOf(block.Binding);
        JsonNode? value;
        var resolved = block.Binding switch
        {
            // Static content is authored on the block; nothing is looked up (layout-ck-25).
            LayoutStaticBinding content => Static(content, out value),
            LayoutRecordFieldBinding field => sources.TryResolveField(scope, field.FieldPath, out value),
            LayoutQueryBinding query => sources.TryResolveQuery(scope, query.ViewDefinitionId, out value),
            LayoutMeasureBinding measure => sources.TryResolveMeasure(scope, measure.MeasurePath, out value),
            LayoutTemplateBinding template => sources.TryResolveTemplate(scope, template.TemplateDefinitionId, out value),
            _ => Unresolved(out value),
        };

        if (resolved) blocks.Add(new(block.Id, block.Kind, kind, name, value, scope.RowId));
        else refusals.Add(new(block.Id, kind, name, scope.RowId));
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
        if (block.ShowWhen is not { Length: > 0 } expression) return true;

        // The guard is Rules' grammar, evaluated by the shared engine and never by a
        // layout-local conditional (layout-eng-16, layout-auth-20). A block inside a repeating
        // container is a Row-scoped rule over that container's section, which is what makes a
        // `row.` reference legal there and illegal anywhere else.
        var rule = new RuleDefinition
        {
            Id = $"layout.show_when.{block.Id}",
            Tier = RuleTier.JsonLogic,
            Scope = scope.IsRow ? RuleScope.Row : RuleScope.Schema,
            // A Row-scoped rule's target is 'section/field': the repeating collection and the
            // block the guard attaches to within one row.
            ScopeTarget = scope.IsRow ? $"{scope.Section}/{block.Id}" : block.Id,
            Expression = expression,
            Action = RuleActionKind.Validate,
        };
        var adapter = new LayoutSurfaceContextAdapter(root, scope);
        var evalScope = scope.IsRow ? new RuleEvalScope(scope.Section, scope.RowId) : RuleEvalScope.Root;
        try
        {
            // Evaluation is already fail-closed: a pending, errored or budget-aborted guard is
            // Invalid. Compilation is NOT — the compiler throws on a malformed expression or a
            // reference illegal at this scope — so an uncompilable guard withholds the block
            // here rather than escaping as a fault that would blank the whole surface.
            return _guards.EvaluateGuard(rule, adapter, evalScope, cancellationToken).Ok;
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

    private static string RowIdOf(JsonNode? row, int index)
        => row is JsonObject obj && obj.TryGetPropertyValue("id", out var id) && id is not null
            ? id.ToString()
            : index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, JsonNode?> ValuesOf(JsonNode? row)
        => row is JsonObject obj
            ? obj.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
}
