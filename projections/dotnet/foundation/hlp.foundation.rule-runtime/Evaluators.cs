using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;


namespace Harborline.Foundation.RuleEngine;

/// <summary>Resolves <c>field.</c> references against a flat workflow context bag (no tables).</summary>
internal sealed class ContextBagResolver : IValueResolver
{
    private readonly IReadOnlyDictionary<string, JsonNode?> _bag;

    public ContextBagResolver(IReadOnlyDictionary<string, JsonNode?> bag) => _bag = bag;

    public RefValue ResolveVar(string path)
    {
        // row. references are not addressable in a flat guard context.
        if (path.StartsWith("row.", StringComparison.Ordinal))
        {
            return RefValue.OfError(RuleError.Of(RuleEngineCodes.BadReference, "path", path));
        }
        // "field.x" and a bare "x" both address the context bag.
        string name = path.StartsWith("field.", StringComparison.Ordinal) ? path["field.".Length..] : path;
        if (_bag.TryGetValue(name, out var v))
        {
            return PendingSentinel.Is(v) ? RefValue.Pending : RefValue.Resolved(v);
        }
        return RefValue.Resolved(null);
    }

    // A flat context bag carries no tables: every aggregate is unavailable data (ticket 162's
    // one shared refusal shape — the corpus pins its params across tiers).
    public RefValue ResolveAgg(string fn, string section, string col)
        => RefValue.UnavailableAggregate(fn, section, col);
}

/// <summary>The atom (SPINE-1 design §5.2): evaluate one compiled rule against a resolver.</summary>
internal sealed class RuleEvaluator : IRuleEvaluator
{
    private readonly TimeProvider _clock;

    public RuleEvaluator(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public RuleOutcome Evaluate(CompiledRule rule, CellAddress target, string ruleKey,
        IValueResolver resolver, DateTimeOffset now, RuleEngineLimits limits, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(limits.WallClockCeiling);
        var ctx = new EvalContext(resolver, now, new EvalBudget(limits, cts.Token));
        try
        {
            return OutcomeBuilder.Build(rule, target, resolver, ctx).Outcome;
        }
        catch (RuleBudgetException) { return FailClosed(rule, target, RuleEngineCodes.BudgetExceeded); }
        // RuleEngineTimeoutException propagates (non-authoritative liveness fault; D1 ratification).
    }

    internal static RuleOutcome FailClosed(CompiledRule rule, CellAddress target, string code) => rule.OutputType switch
    {
        OutputType.Value => RuleOutcome.OfValue(rule.Source.Id, target, ComputedValue.OfError(RuleError.Of(code))),
        OutputType.Validity => RuleOutcome.OfValidity(rule.Source.Id, target, Validity.Invalid(RuleError.Of(code))),
        OutputType.Visibility => RuleOutcome.OfVisibility(rule.Source.Id, target, new VisibilityState(Visible: false)),
        // Fail-closed options is the empty list — a choice field falls back to no dynamic options.
        OutputType.Options => RuleOutcome.OfOptions(rule.Source.Id, target, OptionsOutcome.OfError(RuleError.Of(code))),
        _ => RuleOutcome.OfPresentation(rule.Source.Id, target, new PresentationOutcome(Severity.Error)),
    };
}

/// <summary>
/// The workflow orchestrator (SPINE-1 design §5.4): evaluate a transition guard /
/// action condition (a one-node graph) over a flat context bag. Server-side +
/// fail-closed — a pending/errored guard does not let a transition fire.
/// </summary>
public sealed class GuardEvaluator : IGuardEvaluator
{
    private readonly RuleEngineLimits _limits;
    private readonly TimeProvider _clock;

    public GuardEvaluator(RuleEngineLimits? limits = null, TimeProvider? clock = null)
    {
        _limits = limits ?? RuleEngineLimits.Default;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Validity EvaluateGuard(RuleDefinition rule, IReadOnlyDictionary<string, JsonNode?> context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        // The workflow-guard context flows through the ADR 0146 D3 seam: a flat bag is a
        // ContextBagAdapter, evaluated at Root (behaviour-neutral).
        return EvaluateGuard(rule, new ContextBagAdapter(context), RuleEvalScope.Root, ct);
    }

    /// <summary>
    /// Evaluates a guard rule (a <c>Validate</c>-shaped boolean) against ANY pillar's context adapter
    /// (ADR 0146 D3), resolving <c>var</c>/<c>agg</c>/<c>row.</c> references at <paramref name="scope"/>.
    /// The bag overload is the special case <c>ContextBagAdapter + Root</c>; the documents pillar (#111)
    /// evaluates a template block guard through its <c>DocumentMergeContextAdapter</c> at ROOT_SCOPE (a
    /// header/footer condition) or a row scope (a per-line-item condition) with this overload — the
    /// seam completion the D3 remarks anticipate for the three new adapters. Fail-closed: a pending /
    /// errored / budget-aborted guard is <see cref="Validity.Invalid(RuleError)"/> (never lets a gated
    /// block render on an ambiguous verdict).
    /// </summary>
    public Validity EvaluateGuard(RuleDefinition rule, IContextAdapter adapter, RuleEvalScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(adapter);
        var compiled = RuleCompiler.Compile(new[] { rule }, _limits);
        if (compiled.RuleCount == 0) return Validity.Valid; // Tier-1 guard: nothing for this engine to check.

        var cr = compiled.Rules[0];
        return Run(cr, adapter.CreateResolver(scope), ct,
            onValue: v => HarborlineJsonLogic.IsTruthy(v) ? Validity.Valid : Validity.Invalid(RuleError.Of(rule.Id)),
            onError: e => Validity.Invalid(e),
            onPending: () => Validity.Invalid(RuleError.Of(RuleEngineCodes.PendingAtSave)),
            onAbort: code => Validity.Invalid(RuleError.Of(code)));
    }

    /// <inheritdoc />
    public ComputedValue EvaluateValue(RuleDefinition rule, IReadOnlyDictionary<string, JsonNode?> context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return EvaluateValue(rule, new ContextBagAdapter(context), RuleEvalScope.Root, ct);
    }

    /// <summary>
    /// Evaluates a value expression (a <c>Compute</c>-shaped rule) against ANY pillar's context adapter
    /// (ADR 0146 D3) at <paramref name="scope"/> — the adapter-based companion to the bag overload. The
    /// documents pillar (#111) resolves a computed merge value (a named-Rule or inline compute) through
    /// its <c>DocumentMergeContextAdapter</c> with this; plain merge fields ask the resolver directly.
    /// </summary>
    public ComputedValue EvaluateValue(RuleDefinition rule, IContextAdapter adapter, RuleEvalScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(adapter);
        var compiled = RuleCompiler.Compile(new[] { rule }, _limits);
        if (compiled.RuleCount == 0) return ComputedValue.Resolved(null);

        var cr = compiled.Rules[0];
        return Run(cr, adapter.CreateResolver(scope), ct,
            onValue: v => ComputedValue.Resolved(v),
            onError: ComputedValue.OfError,
            onPending: ComputedValue.OfPending,
            onAbort: code => ComputedValue.OfError(RuleError.Of(code)));
    }

    private T Run<T>(CompiledRule cr, IValueResolver resolver, CancellationToken ct,
        Func<JsonNode?, T> onValue, Func<RuleError, T> onError, Func<T> onPending, Func<string, T> onAbort)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_limits.WallClockCeiling);
        var ctx = new EvalContext(resolver, _clock.GetUtcNow(), new EvalBudget(_limits, cts.Token));
        try { return onValue(HarborlineJsonLogic.Evaluate(cr.Ast, ctx)); }
        catch (RuleEvalException ex) { return onError(ex.Error); }
        catch (RulePendingException) { return onPending(); }
        catch (RuleBudgetException) { return onAbort(RuleEngineCodes.BudgetExceeded); }
        // RuleEngineTimeoutException propagates (non-authoritative liveness fault; D1 ratification):
        // a guard that hits the wall-clock is an infrastructure fault, not a transition verdict.
    }
}
