using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Model;


namespace Harborline.Foundation.RuleEngine.Graph;

/// <summary>
/// The form orchestrator (SPINE-1 design §2, §5.2). Builds the instance dependency
/// graph (addressable-cell DAG), topo-sorts it (Kahn), evaluates value cells with
/// Error/Pending propagation, exposes per-rule outcomes + form-facing views, and
/// reactively re-evaluates the transitive-dependent set on a single-cell change or a
/// child-table row add/remove.
/// </summary>
public sealed class FormRuleGraph : IFormRuleGraph
{
    private readonly RuleEngineLimits _limits;
    private readonly TimeProvider _clock;

    private RuleInstance _instance = new();

    // Build artifacts (rebuilt on structural change).
    private readonly List<ComputedCell> _order = new();              // topo-ordered computed cells
    private readonly Dictionary<string, ComputedCell> _cells = new();
    private readonly Dictionary<string, HashSet<string>> _deps = new();      // cell -> its dependency cells
    private readonly Dictionary<string, HashSet<string>> _dependents = new();// cell -> computed cells depending on it
    // Broad reverse index: ANY referenced cell key (raw field OR computed) -> computed cells that read it.
    // Used by the reactive path so a change to a RAW field still dirties its dependents.
    private readonly Dictionary<string, HashSet<string>> _readers = new();
    // Actual (expression-valued missing key) reads from the current generation.
    // Kept separate from static references so a key-list change can replace them.
    private readonly Dictionary<string, HashSet<string>> _dynamicReaders = new();
    private readonly Dictionary<string, HashSet<string>> _actualCellReads = new();
    private readonly Dictionary<string, HashSet<string>> _actualPlanReads = new();
    private readonly List<OutcomePlan> _plans = new();

    // Last evaluation state (mutated incrementally).
    private readonly Dictionary<string, ComputedValue> _values = new();
    private readonly Dictionary<string, RuleOutcome> _outcomes = new();
    private DateTimeOffset _evaluationInstant;

    public FormRuleGraph(CompiledGraph compiled, TimeProvider clock, RuleEngineLimits? limits = null)
    {
        Compiled = compiled ?? throw new ArgumentNullException(nameof(compiled));
        _limits = limits ?? RuleEngineLimits.Default;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public CompiledGraph Compiled { get; }

    /// <inheritdoc />
    public RuleEvaluationResult EvaluateInstance(RuleInstance instance, CancellationToken ct = default)
    {
        try
        {
            _instance = (instance ?? throw new ArgumentNullException(nameof(instance))).CaptureOwned();
        }
        catch (InvalidOperationException exception) when (exception.Message == RuleEngineCodes.ContextSnapshotRequired)
        {
            return FailClosed(RuleEngineCodes.ContextSnapshotRequired);
        }
        catch (ArgumentException)
        {
            return FailClosed(RuleEngineCodes.InputTooLarge);
        }
        return BuildAndEvaluate(ct);
    }

    /// <inheritdoc />
    public RuleEvaluationResult Reevaluate(string fieldName, JsonNode? unownedValue, CancellationToken ct = default)
    {
        try { RuntimeInputEnvelope.ValidateMemberName(fieldName, nameof(fieldName)); }
        catch (ArgumentException) { return FailClosed(RuleEngineCodes.InputTooLarge); }
        return FailClosed(RuleEngineCodes.ContextSnapshotRequired);
    }

    /// <inheritdoc />
    public RuleEvaluationResult Reevaluate(string fieldName, RuleInputValue newValue, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(newValue);
        try { RuntimeInputEnvelope.ValidateMemberName(fieldName, nameof(fieldName)); }
        catch (ArgumentException) { return FailClosed(RuleEngineCodes.InputTooLarge); }
        bool hadPrevious = _instance.Fields.TryGetValue(fieldName, out var previous);
        _instance.Fields[fieldName] = newValue.CloneOwned();
        try { _instance.EnsureEnvelope(); }
        catch (ArgumentException)
        {
            if (hadPrevious) _instance.Fields[fieldName] = previous;
            else _instance.Fields.Remove(fieldName);
            return FailClosed(RuleEngineCodes.InputTooLarge);
        }
        string changedKey = CellAddress.Field(fieldName).Key;

        // Transitive dependents of the changed (possibly raw) cell, via the broad reader index.
        var dirty = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(changedKey);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var readers in ReadersOf(node))
            {
                foreach (var d in readers)
                {
                    if (dirty.Add(d)) queue.Enqueue(d);
                }
            }
        }

        // The business clock is an explicit graph input. Seed only cells that read it, then use the
        // existing reader index to reach their transitive dependents; unrelated outcomes retain identity.
        foreach (var cell in _order.Where(cell => UsesClock(cell.Rule)))
        {
            if (dirty.Add(cell.Key)) queue.Enqueue(cell.Key);
        }
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var readers in ReadersOf(node))
            {
                foreach (var reader in readers)
                {
                    if (dirty.Add(reader)) queue.Enqueue(reader);
                }
            }
        }

        _evaluationInstant = _clock.GetUtcNow();
        var budget = NewBudget(ct);
        var adapter = new FormContextAdapter(_values, _instance); // ADR 0146 D3 context seam
        var run = new DemandRun(this, budget, adapter, dirty);
        try
        {
            // Re-eval only the dirty computed cells, in topo order.
            foreach (var cell in _order.Where(c => dirty.Contains(c.Key)))
            {
                _values[cell.Key] = run.Evaluate(cell);
            }
            // Re-eval only the outcomes that read the changed field or a dirty cell (referential stability elsewhere).
            var touched = new HashSet<string>(dirty) { changedKey };
            foreach (var plan in _plans.Where(p => UsesClock(p.Rule) || p.Reads.Overlaps(touched)
                || (_actualPlanReads.TryGetValue(p.Key, out var actual) && actual.Overlaps(touched))
                || dirty.Contains(p.Target.Key)))
            {
                _outcomes[plan.Key] = BuildOutcome(plan, run);
            }
        }
        catch (RuleBudgetException) { return FailClosed(RuleEngineCodes.BudgetExceeded); }
        // RuleEngineTimeoutException is a non-authoritative liveness fault (D1 ratification): it is NOT
        // caught here — it propagates as an infrastructure exception so the wall-clock can never emit a
        // divergent evaluation outcome. The op-budget above is the sole authoritative fail-closed bound.

        return Project();
    }

    /// <inheritdoc />
    public RuleEvaluationResult AddRow(string section, RuleRow row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(row);
        try { RuntimeInputEnvelope.ValidateMemberName(section, nameof(section)); }
        catch (ArgumentException) { return FailClosed(RuleEngineCodes.InputTooLarge); }
        RuleRow captured;
        try
        {
            captured = row.CaptureOwned();
        }
        catch (InvalidOperationException exception) when (exception.Message == RuleEngineCodes.ContextSnapshotRequired)
        {
            return FailClosed(RuleEngineCodes.ContextSnapshotRequired);
        }
        catch (ArgumentException)
        {
            return FailClosed(RuleEngineCodes.InputTooLarge);
        }
        var prospective = _instance.CaptureOwned();
        if (!prospective.Tables.TryGetValue(section, out var prospectiveRows)) prospective.Tables[section] = prospectiveRows = new List<RuleRow>();
        prospectiveRows.Add(captured);
        try { prospective.EnsureEnvelope(); }
        catch (ArgumentException)
        {
            return FailClosed(RuleEngineCodes.InputTooLarge);
        }
        if (_instance.Tables.TryGetValue(section, out var rows) && rows.Count >= _limits.MaxTableRowsPerAggregate && HasAggregateForSection(section))
            return RefuseTableRow(section);
        if (!_instance.Tables.TryGetValue(section, out rows)) _instance.Tables[section] = rows = new List<RuleRow>();
        rows.Add(captured);
        return BuildAndEvaluate(ct); // structural change: rebuild graph + re-link aggregates
    }

    /// <inheritdoc />
    public RuleEvaluationResult RemoveRow(string section, string rowId, CancellationToken ct = default)
    {
        try
        {
            RuntimeInputEnvelope.ValidateMemberName(section, nameof(section));
            RuntimeInputEnvelope.ValidateMemberName(rowId, nameof(rowId));
        }
        catch (ArgumentException) { return FailClosed(RuleEngineCodes.InputTooLarge); }
        if (_instance.Tables.TryGetValue(section, out var rows)) rows.RemoveAll(r => r.Id == rowId);
        return BuildAndEvaluate(ct);
    }

    // ── build + full evaluate ────────────────────────────────────────────────

    private EvalBudget NewBudget(CancellationToken ct)
    {
        // The .NET integrity tier's authoritative wall-clock backstop: a linked CTS with the ceiling.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_limits.WallClockCeiling);
        return new EvalBudget(_limits, cts.Token);
    }

    private RuleEvaluationResult BuildAndEvaluate(CancellationToken ct)
    {
        _evaluationInstant = _clock.GetUtcNow();
        var preflight = PreflightBounds();
        if (preflight is not null) return FailClosed(preflight);

        Build();

        if (_cells.Count > _limits.MaxGraphNodes)
        {
            return FailClosed(RuleEngineCodes.GraphTooLarge);
        }

        var budget = NewBudget(ct);
        _values.Clear();
        _outcomes.Clear();
        var adapter = new FormContextAdapter(_values, _instance); // ADR 0146 D3 context seam
        var run = new DemandRun(this, budget, adapter, null);
        try
        {
            foreach (var cell in _order)
            {
                _values[cell.Key] = run.Evaluate(cell);
            }
            foreach (var plan in _plans)
            {
                _outcomes[plan.Key] = BuildOutcome(plan, run);
            }
        }
        catch (RuleBudgetException) { return FailClosed(RuleEngineCodes.BudgetExceeded); }
        // RuleEngineTimeoutException is a non-authoritative liveness fault (D1 ratification 2026-07-01):
        // it is NOT caught here — it propagates as an infrastructure exception so the wall-clock can never
        // emit a divergent evaluation outcome. The op-budget above is the sole authoritative fail-closed bound.

        return Project();
    }

    private string? PreflightBounds()
    {
        // Bound the otherwise un-budgeted graph CONSTRUCTION (finding F3): project the cell count
        // BEFORE Build() expands Row rules across every (user-controlled) instance row + Kahn-sorts.
        // Also reject a Compute rule that addresses no form cell — Section/Schema scope — as a
        // fail-closed config error rather than a silent no-op (finding F8). (Guards use the shared
        // compiler directly, not this form graph, so their Schema-scoped Compute is unaffected.)
        long projectedCells = 0;
        var aggCells = new HashSet<string>();
        foreach (var rule in Compiled.Rules)
        {
            if (rule.Source.Action == RuleActionKind.Compute)
            {
                if (rule.Source.Scope is not (RuleScope.Field or RuleScope.Row or RuleScope.Table))
                    return RuleEngineCodes.ComputeScopeInvalid;
                projectedCells += rule.Source.Scope == RuleScope.Row ? RowsOf(rule.RowSection!).Count : 1;
            }
            foreach (var rf in rule.References)
                if (rf is AggRef a) aggCells.Add(CellAddress.TableAggregate(a.Section, a.Fn, a.Col).Key);
            if (projectedCells > _limits.MaxGraphNodes) return RuleEngineCodes.GraphTooLarge;
        }
        return projectedCells + aggCells.Count > _limits.MaxGraphNodes ? RuleEngineCodes.GraphTooLarge : null;
    }

    private void Build()
    {
        _cells.Clear();
        _deps.Clear();
        _dependents.Clear();
        _readers.Clear();
        _dynamicReaders.Clear();
        _actualCellReads.Clear();
        _actualPlanReads.Clear();
        _order.Clear();
        _plans.Clear();

        // 1. Compute cells (rule-driven) + their outcome plans; 2. implicit aggregate cells.
        foreach (var rule in Compiled.Rules)
        {
            if (rule.Source.Action == RuleActionKind.Compute)
            {
                AddComputeCellsAndPlans(rule);
            }
            else
            {
                AddNonComputePlans(rule);
            }

            // Aggregate cells referenced anywhere become implicit fold cells (unless a Table-compute defines them).
            foreach (var rf in rule.References)
            {
                if (rf is AggRef a)
                {
                    var addr = CellAddress.TableAggregate(a.Section, a.Fn, a.Col);
                    if (!_cells.ContainsKey(addr.Key))
                    {
                        _cells[addr.Key] = new ComputedCell(addr.Key, addr, null, null, AggFold: (a.Fn, a.Section, a.Col));
                    }
                }
            }
        }

        // 3. Edges.
        foreach (var cell in _cells.Values)
        {
            _deps.TryAdd(cell.Key, new HashSet<string>());
            _dependents.TryAdd(cell.Key, new HashSet<string>());
        }
        foreach (var cell in _cells.Values)
        {
            if (cell.AggFold is { } fold)
            {
                // Aggregate depends on each row's column cell. Bound this un-budgeted edge walk:
                // a table over the per-aggregate row cap fails closed in FoldAggregate, so don't
                // pre-walk all its (user-controlled) rows here (finding F3).
                if (_instance.Tables.TryGetValue(fold.Section, out var rows)
                    && rows.Count <= _limits.MaxTableRowsPerAggregate)
                {
                    foreach (var row in rows)
                    {
                        var rowKey = CellAddress.Row(fold.Section, row.Id, fold.Col).Key;
                        AddReader(rowKey, cell.Key);
                        if (_cells.ContainsKey(rowKey)) Edge(cell.Key, rowKey);
                    }
                }
            }
            else if (cell.Rule is { } cr)
            {
                foreach (var rf in cr.References)
                {
                    var depKey = ResolveRefKey(rf, cr, cell.RowId);
                    if (depKey is not null)
                    {
                        AddReader(depKey, cell.Key);
                        if (_cells.ContainsKey(depKey)) Edge(cell.Key, depKey);
                    }
                }
            }
        }

        // 4. Topological order (Kahn); cells that remain are in a (defensive) cycle.
        TopoSort();
    }

    private void AddComputeCellsAndPlans(CompiledRule rule)
    {
        switch (rule.Source.Scope)
        {
            case RuleScope.Field:
            {
                var addr = CellAddress.Field(rule.Source.ScopeTarget);
                _cells[addr.Key] = new ComputedCell(addr.Key, addr, rule, null, null);
                _plans.Add(MakePlan(rule, addr, null));
                break;
            }
            case RuleScope.Row:
            {
                foreach (var row in RowsOf(rule.RowSection!))
                {
                    var addr = CellAddress.Row(rule.RowSection!, row.Id, rule.RowField!);
                    _cells[addr.Key] = new ComputedCell(addr.Key, addr, rule, row.Id, null);
                    _plans.Add(MakePlan(rule, addr, row.Id));
                }
                break;
            }
            case RuleScope.Table:
            {
                var addr = rule.StaticTarget!.Value;
                _cells[addr.Key] = new ComputedCell(addr.Key, addr, rule, null, null);
                _plans.Add(MakePlan(rule, addr, null));
                break;
            }
        }
    }

    private void AddNonComputePlans(CompiledRule rule)
    {
        switch (rule.Source.Scope)
        {
            case RuleScope.Row:
                foreach (var row in RowsOf(rule.RowSection!))
                {
                    var addr = CellAddress.Row(rule.RowSection!, row.Id, rule.RowField!);
                    _plans.Add(MakePlan(rule, addr, row.Id));
                }
                break;
            default:
                _plans.Add(MakePlan(rule, rule.StaticTarget!.Value, null));
                break;
        }
    }

    private OutcomePlan MakePlan(CompiledRule rule, CellAddress target, string? rowId)
    {
        string key = rowId is null ? rule.Source.Id : rule.Source.Id + "#" + rowId;
        var reads = new HashSet<string>();
        foreach (var rf in rule.References)
        {
            var k = ResolveRefKey(rf, rule, rowId);
            if (k is not null) reads.Add(k);
        }
        return new OutcomePlan(key, rule, target, rowId, reads, rule.Source.Action == RuleActionKind.Compute);
    }

    private string? ResolveRefKey(RuleRef rf, CompiledRule rule, string? rowId) => rf switch
    {
        FieldRef f => CellAddress.Field(f.Name).Key,
        RowFieldRef rfld when rule.RowSection is not null && rowId is not null
            => CellAddress.Row(rule.RowSection, rowId, rfld.Field).Key,
        AggRef a => CellAddress.TableAggregate(a.Section, a.Fn, a.Col).Key,
        _ => null,
    };

    private IReadOnlyList<RuleRow> RowsOf(string section)
        => _instance.Tables.TryGetValue(section, out var rows) ? rows : Array.Empty<RuleRow>();

    private void Edge(string from, string dep)
    {
        _deps[from].Add(dep);
        _dependents[dep].Add(from);
    }

    private void AddReader(string key, string reader)
    {
        if (!_readers.TryGetValue(key, out var set)) _readers[key] = set = new HashSet<string>();
        set.Add(reader);
    }

    private IEnumerable<HashSet<string>> ReadersOf(string key)
    {
        if (_readers.TryGetValue(key, out var staticReaders)) yield return staticReaders;
        if (_dynamicReaders.TryGetValue(key, out var dynamicReaders)) yield return dynamicReaders;
    }

    private void ClearActualCellReads(string owner)
    {
        if (!_actualCellReads.Remove(owner, out var prior)) return;
        foreach (var key in prior)
        {
            if (!_dynamicReaders.TryGetValue(key, out var readers)) continue;
            readers.Remove(owner);
            if (readers.Count == 0) _dynamicReaders.Remove(key);
        }
    }

    private void RecordActualCellRead(string owner, string key)
    {
        if (!_actualCellReads.TryGetValue(owner, out var reads)) _actualCellReads[owner] = reads = new HashSet<string>();
        if (!reads.Add(key)) return;
        if (!_dynamicReaders.TryGetValue(key, out var readers)) _dynamicReaders[key] = readers = new HashSet<string>();
        readers.Add(owner);
    }

    private void BeginPlanReads(string key) => _actualPlanReads[key] = new HashSet<string>();

    private void RecordActualPlanRead(string owner, string key)
    {
        if (!_actualPlanReads.TryGetValue(owner, out var reads)) _actualPlanReads[owner] = reads = new HashSet<string>();
        reads.Add(key);
    }

    private void TopoSort()
    {
        var inDeg = _cells.Keys.ToDictionary(k => k, k => _deps[k].Count);
        var ready = new Queue<string>(inDeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var ordered = new HashSet<string>();
        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            _order.Add(_cells[node]);
            ordered.Add(node);
            foreach (var dependent in _dependents[node])
            {
                if (--inDeg[dependent] == 0) ready.Enqueue(dependent);
            }
        }
        // Any cell not ordered is in a defensive per-row cycle — appended; eval marks them Error(cycle).
        foreach (var cell in _cells.Values)
        {
            if (!ordered.Contains(cell.Key)) _order.Add(cell with { Cyclic = true });
        }
    }

    // ── cell + outcome evaluation ────────────────────────────────────────────

    private ComputedValue EvalComputedCell(ComputedCell cell, DemandRun run)
    {
        if (cell.Cyclic) return ComputedValue.OfError(RuleError.Of(RuleEngineCodes.Cycle, "cell", cell.Key));

        if (cell.AggFold is { } fold) return FoldAggregate(fold.Fn, fold.Section, fold.Col, run);

        var scope = new RuleEvalScope(cell.Rule!.RowSection, cell.RowId);
        var resolver = new DemandResolver(run.Adapter.CreateResolver(scope), run, cell.Key, scope);
        var ctx = new EvalContext(resolver, _evaluationInstant, run.Budget);
        try
        {
            return ComputedValue.Resolved(HarborlineJsonLogic.Evaluate(cell.Rule.Ast, ctx));
        }
        catch (RulePendingException) { return ComputedValue.OfPending(); }
        catch (RuleEvalException ex) { return ComputedValue.OfError(ex.Error); }
    }

    private static bool UsesClock(CompiledRule? rule) => rule is not null && ContainsDateToday(rule.Ast);

    private static bool ContainsDateToday(JsonNode? node)
    {
        // The evaluator only executes a single-key object; arrays and multi-key objects
        // are literal data.  Recurse only through an executable operator's argument list.
        if (node is not JsonObject obj || obj.Count != 1) return false;
        var (op, raw) = obj.First();
        if (op == "date.today") return true;
        return raw is JsonArray args
            ? args.Any(ContainsDateToday)
            : ContainsDateToday(raw);
    }

    private RuleOutcome BuildOutcome(OutcomePlan plan, DemandRun run)
    {
        if (plan.IsComputeValue)
        {
            var cv = _values.TryGetValue(plan.Target.Key, out var v) ? v : ComputedValue.Resolved(null);
            return RuleOutcome.OfValue(plan.Rule.Source.Id, plan.Target, cv);
        }
        BeginPlanReads(plan.Key);
        var scope = new RuleEvalScope(plan.Rule.RowSection, plan.RowId);
        var resolver = new DemandResolver(run.Adapter.CreateResolver(scope), run, plan.Key, scope, plan: true);
        var ctx = new EvalContext(resolver, _evaluationInstant, run.Budget);
        return OutcomeBuilder.Build(plan.Rule, plan.Target, resolver, ctx).Outcome;
    }

    private ComputedValue FoldAggregate(string fn, string section, string col, DemandRun run)
    {
        if (!_instance.Tables.TryGetValue(section, out var rows)) rows = new List<RuleRow>();
        if (rows.Count > _limits.MaxTableRowsPerAggregate)
        {
            return ComputedValue.OfError(RuleError.Of(RuleEngineCodes.TableTooLarge, "section", section));
        }

        var values = new List<JsonNode?>();
        foreach (var row in rows)
        {
            run.Budget.Charge();
            var rowKey = CellAddress.Row(section, row.Id, col).Key;
            ComputedValue cv;
            // A demanded aggregate can run before the ordinary topo loop reaches its
            // row producer. Demand the current-generation cell when one exists;
            // only an absent producer is permitted to use the raw row value.
            if (_cells.TryGetValue(rowKey, out var rowCell))
            {
                cv = run.Evaluate(rowCell);
            }
            else
            {
                var raw = row.Fields.TryGetValue(col, out var rv) ? rv : null;
                cv = PendingSentinel.Is(raw) ? ComputedValue.OfPending() : ComputedValue.Resolved(raw);
            }
            if (cv.State == ValueState.Pending) return ComputedValue.OfPending();
            if (cv.State == ValueState.Error) return ComputedValue.OfError(RuleError.Of(RuleEngineCodes.UpstreamError, "cell", rowKey));
            values.Add(cv.Value);
        }

        // A column whose every value is a decimal string is money — sum/min/max/avg must be exact
        // decimal, never IEEE double (finding F7). Number columns keep the numeric (double) fold.
        bool allDecimalStrings = values.Count > 0 && values.All(v => v is JsonValue jv && jv.TryGetValue<string>(out _));
        try
        {
            if (allDecimalStrings && fn is "sum" or "min" or "max" or "avg")
            {
                return MoneyAggregate(fn, values, section);
            }
            return fn switch
            {
                "count" => ComputedValue.Resolved(JsonValue.Create(values.Count)),
                "sum" => ComputedValue.Resolved(NumNode(values.Sum(v => HarborlineJsonLogic.ToNumber(v)))),
                "avg" => ComputedValue.Resolved(values.Count == 0
                    ? JsonValue.Create(0)
                    : NumNode(values.Sum(v => HarborlineJsonLogic.ToNumber(v)) / values.Count)),
                "min" => ComputedValue.Resolved(values.Count == 0 ? null : NumNode(values.Min(v => HarborlineJsonLogic.ToNumber(v)))),
                "max" => ComputedValue.Resolved(values.Count == 0 ? null : NumNode(values.Max(v => HarborlineJsonLogic.ToNumber(v)))),
                "any" => ComputedValue.Resolved(JsonValue.Create(values.Any(HarborlineJsonLogic.IsTruthy))),
                "all" => ComputedValue.Resolved(JsonValue.Create(values.All(HarborlineJsonLogic.IsTruthy))),
                _ => ComputedValue.OfError(RuleError.Of(RuleEngineCodes.UnknownOperator, "agg", fn)),
            };
        }
        catch (RuleEvalException ex)
        {
            return ComputedValue.OfError(ex.Error);
        }
    }

    private ComputedValue MoneyAggregate(string fn, List<JsonNode?> values, string section)
    {
        // Exact decimal sum/min/max over a money (decimal-string) column (finding F7). avg needs
        // exact decimal division (undefined in v1) → fail closed rather than silently use double.
        if (fn == "avg")
            return ComputedValue.OfError(RuleError.Of(RuleEngineCodes.MoneyAggUnsupported, "section", section));
        try
        {
            var acc = MoneyDecimal.Parse(((JsonValue)values[0]!).GetValue<string>());
            for (int i = 1; i < values.Count; i++)
            {
                var m = MoneyDecimal.Parse(((JsonValue)values[i]!).GetValue<string>());
                acc = fn switch
                {
                    "sum" => acc + m,
                    "min" => m.Compare(acc) < 0 ? m : acc,
                    "max" => m.Compare(acc) > 0 ? m : acc,
                    _ => acc,
                };
            }
            return ComputedValue.Resolved(JsonValue.Create(acc.ToCanonicalString()));
        }
        catch (FormatException)
        {
            return ComputedValue.OfError(RuleError.Of(RuleEngineCodes.TypeError, "op", "agg"));
        }
    }

    private static JsonNode NumNode(double d)
        => d == Math.Floor(d) && Math.Abs(d) < 9.007e15 ? JsonValue.Create((long)d) : JsonValue.Create(d);

    // ── projection ───────────────────────────────────────────────────────────

    private RuleEvaluationResult Project()
    {
        var projectedOutcomes = _outcomes.ToDictionary(pair => pair.Key, pair => ProjectOutcome(pair.Value));
        var projectedValues = _values.ToDictionary(pair => pair.Key, pair => ProjectValue(pair.Value));
        var visibility = new Dictionary<string, VisibilityState>();
        var validations = new List<RuleOutcome>();
        var options = new Dictionary<string, OptionsOutcome>();
        bool hasPending = projectedValues.Values.Any(v => v.State == ValueState.Pending);

        foreach (var outcome in projectedOutcomes.Values)
        {
            switch (outcome.OutputType)
            {
                case OutputType.Visibility:
                    visibility[outcome.Target.Key] = Merge(
                        visibility.TryGetValue(outcome.Target.Key, out var prev) ? prev : new VisibilityState(),
                        outcome.Visibility!);
                    break;
                case OutputType.Validity when outcome.Validity is { Ok: false }:
                    validations.Add(outcome);
                    break;
                case OutputType.Value when outcome.Value is { State: ValueState.Pending }:
                    hasPending = true;
                    break;
                case OutputType.Options:
                    // Last-writer-wins per cell (multiple set-options rules on one field is an
                    // authoring smell, not an engine concern — the graph is deterministically ordered).
                    options[outcome.Target.Key] = outcome.Options!;
                    break;
            }
        }

        return new RuleEvaluationResult(
            projectedOutcomes,
            projectedValues,
            visibility,
            validations,
            hasPending,
            options);
    }

    private static ComputedValue ProjectValue(ComputedValue value)
        => value.State == ValueState.Resolved && value.Value is JsonObject or JsonArray
            ? value with { Value = value.Value.DeepClone() }
            : value;

    private static RuleOutcome ProjectOutcome(RuleOutcome outcome)
        => outcome.OutputType switch
        {
            OutputType.Value when !ReferenceEquals(ProjectValue(outcome.Value!), outcome.Value)
                => RuleOutcome.OfValue(outcome.RuleId, outcome.Target, ProjectValue(outcome.Value!)),
            OutputType.Value => outcome,
            OutputType.Options => RuleOutcome.OfOptions(outcome.RuleId, outcome.Target,
                outcome.Options! with { Options = outcome.Options.Options?.Select(option => option?.DeepClone()).ToList() }),
            _ => outcome,
        };

    // Merge rule: any visibility-rule false hides; required/readOnly are OR-merged.
    private static VisibilityState Merge(VisibilityState a, VisibilityState b)
        => new(a.Visible && b.Visible, a.Required || b.Required, a.ReadOnly || b.ReadOnly);

    private RuleEvaluationResult FailClosed(string code)
    {
        var synthetic = RuleOutcome.OfValidity("rule.engine", CellAddress.Schema(),
            Validity.Invalid(RuleError.Of(code)));
        return new RuleEvaluationResult(
            new Dictionary<string, RuleOutcome> { ["rule.engine"] = synthetic },
            new Dictionary<string, ComputedValue>(),
            new Dictionary<string, VisibilityState>(),
            new List<RuleOutcome> { synthetic },
            hasPending: false);
    }

    private RuleEvaluationResult RefuseTableRow(string section)
    {
        var accepted = Project();
        var values = accepted.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var key in values.Keys.Where(key => key.StartsWith("agg:" + section + "/", StringComparison.Ordinal)).ToArray())
            values[key] = ComputedValue.OfError(RuleError.Of(RuleEngineCodes.TableTooLarge, "section", section));
        return new RuleEvaluationResult(accepted.ByRule, values, accepted.Visibility, accepted.Validations, accepted.HasPending, accepted.Options);
    }

    private bool HasAggregateForSection(string section)
        => Compiled.Rules.Any(rule => rule.References.Any(reference => reference is AggRef aggregate && aggregate.Section == section));

    // ── internal node + plan records ─────────────────────────────────────────

    private sealed class DemandRun
    {
        private readonly FormRuleGraph _graph;
        private readonly HashSet<string> _active = new();
        private readonly HashSet<string> _completed = new();
        private readonly HashSet<string>? _dirty;

        public DemandRun(FormRuleGraph graph, EvalBudget budget, IContextAdapter adapter, HashSet<string>? dirty)
        {
            _graph = graph;
            Budget = budget;
            Adapter = adapter;
            _dirty = dirty;
        }

        public EvalBudget Budget { get; }
        public IContextAdapter Adapter { get; }

        public ComputedValue Evaluate(ComputedCell cell)
        {
            if (_dirty is not null && !_dirty.Contains(cell.Key) && _graph._values.TryGetValue(cell.Key, out var stable)) return stable;
            if (_completed.Contains(cell.Key) && _graph._values.TryGetValue(cell.Key, out var complete)) return complete;
            if (_active.Contains(cell.Key)) return ComputedValue.OfError(RuleError.Of(RuleEngineCodes.Cycle, "cell", cell.Key));
            // Static admission bounds a declared chain; this closes the additional runtime
            // dynamic chain before host recursion can grow without a validated limit.
            // _active already contains the parent chain, so before adding this cell its
            // count is the number of demand edges that will lead to it. Match compiler
            // admission: depth zero permits a leaf, and only depth greater than the cap
            // is refused.
            if (_active.Count > Budget.Limits.MaxDependencyDepth)
                return ComputedValue.OfError(RuleError.Of(RuleEngineCodes.BudgetExceeded));

            Budget.Charge();
            _active.Add(cell.Key);
            _graph.ClearActualCellReads(cell.Key);
            try
            {
                var value = _graph.EvalComputedCell(cell, this);
                _graph._values[cell.Key] = value;
                _completed.Add(cell.Key);
                return value;
            }
            finally
            {
                _active.Remove(cell.Key);
            }
        }

        public RefValue Resolve(string owner, bool plan, string key, Func<RefValue> fallback)
        {
            if (plan) _graph.RecordActualPlanRead(owner, key);
            else _graph.RecordActualCellRead(owner, key);
            if (!_graph._cells.TryGetValue(key, out var cell)) return fallback();
            return FromComputed(Evaluate(cell), key);
        }

        private static RefValue FromComputed(ComputedValue value, string key) => value.State switch
        {
            ValueState.Resolved => RefValue.Resolved(value.Value),
            ValueState.Pending => RefValue.Pending,
            _ => RefValue.OfError(value.Error?.Code == RuleEngineCodes.Cycle
                ? value.Error
                : RuleError.Of(RuleEngineCodes.UpstreamError, "cell", key)),
        };
    }

    private sealed class DemandResolver : IValueResolver
    {
        private readonly IValueResolver _fallback;
        private readonly DemandRun _run;
        private readonly string _owner;
        private readonly bool _plan;
        private readonly RuleEvalScope _scope;

        public DemandResolver(IValueResolver fallback, DemandRun run, string owner, RuleEvalScope scope, bool plan = false)
        {
            _fallback = fallback;
            _run = run;
            _owner = owner;
            _scope = scope;
            _plan = plan;
        }

        public RefValue ResolveVar(string path)
        {
            if (path.StartsWith("row.", StringComparison.Ordinal))
            {
                if (_scope.RowSection is null || _scope.RowId is null) return _fallback.ResolveVar(path);
                var rowKey = CellAddress.Row(_scope.RowSection, _scope.RowId, path["row.".Length..]).Key;
                return _run.Resolve(_owner, _plan, rowKey, () => _fallback.ResolveVar(path));
            }
            var name = path.StartsWith("field.", StringComparison.Ordinal) ? path["field.".Length..] : path;
            var key = CellAddress.Field(name).Key;
            return _run.Resolve(_owner, _plan, key, () => _fallback.ResolveVar(path));
        }

        public RefValue ResolveAgg(string fn, string section, string col)
        {
            var key = CellAddress.TableAggregate(section, fn, col).Key;
            return _run.Resolve(_owner, _plan, key, () => _fallback.ResolveAgg(fn, section, col));
        }
    }

    private sealed record ComputedCell(
        string Key,
        CellAddress Address,
        CompiledRule? Rule,
        string? RowId,
        (string Fn, string Section, string Col)? AggFold)
    {
        public bool Cyclic { get; init; }
    }

    private sealed record OutcomePlan(
        string Key,
        CompiledRule Rule,
        CellAddress Target,
        string? RowId,
        HashSet<string> Reads,
        bool IsComputeValue);
}
