using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Model;


namespace Harborline.Foundation.RuleEngine.Compilation;

/// <summary>The immutable compiled form of a definition's Tier-2 rules (SPINE-1 design §2.2 step 1).</summary>
public sealed class CompiledGraph
{
    internal CompiledGraph(IReadOnlyList<CompiledRule> rules, WorkProof workProof)
    {
        Rules = rules;
        WorkProof = workProof;
    }

    internal IReadOnlyList<CompiledRule> Rules { get; }

    /// <summary>Compiler-owned finite transfer proof; never serialized into an authored definition.</summary>
    public WorkProof WorkProof { get; }

    /// <summary>The number of Tier-2 rules compiled (Tier-1 rules are handled by the kernel JSON-Schema validator).</summary>
    public int RuleCount => Rules.Count;

    /// <summary>
    /// Each compiled rule's lowered AST in compile order, with references rewritten as the grammar
    /// defines them (e.g. <c>parent.z</c> to <c>field.z</c>). Returned as copies, so a caller cannot
    /// alter a compiled rule. The TypeScript engine exposes the same through <c>rules[].ast</c>.
    /// </summary>
    public IReadOnlyList<JsonNode?> LoweredAsts => [.. Rules.Select(rule => rule.Ast?.DeepClone())];
}

/// <summary>
/// Lowers + validates a definition's rules at publish-time (SPINE-1 design §2.2,
/// §2.3, §4): grammar-lowering, reference extraction, the static resource bounds,
/// and fail-closed cycle detection with a path diagnostic. A rejected definition
/// throws <see cref="RuleCompilationException"/> and never reaches an instance.
/// </summary>
public static class RuleCompiler
{
    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        "var", "missing", "missing_some",
        "==", "!=", "===", "!==", "!", "!!", "and", "or", "if",
        ">", ">=", "<", "<=", "+", "-", "*", "/", "%", "min", "max", "in", "cat",
        "agg", "money.add", "money.sub", "money.mul", "date.add", "date.diff", "date.today", "coding.is",
    };

    /// <summary>Compiles a definition's rules; throws <see cref="RuleCompilationException"/> on rejection.</summary>
    public static CompiledGraph Compile(IReadOnlyList<RuleDefinition> rules, RuleEngineLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var lim = limits ?? RuleEngineLimits.Default;
        var compiled = new List<CompiledRule>();

        foreach (var rule in rules)
        {
            // This engine owns Tier-2 (JsonLogic). Tier-1 (JsonSchema) is the kernel validator's.
            if (rule.Tier == RuleTier.JsonSchema) continue;
            if (rule.Tier != RuleTier.JsonLogic)
            {
                throw new RuleCompilationException(
                    RuleEngineCodes.CompileUnsupportedTier,
                    $"rule '{rule.Id}': tier '{rule.Tier}' is unsupported by the v1 evaluator.",
                    rule.Id);
            }

            if (!Enum.IsDefined(rule.Action))
                throw new RuleCompilationException(RuleEngineCodes.CompileUnknownAction,
                    $"rule '{rule.Id}': unknown action '{rule.Action}'.", rule.Id);

            var (ctx, staticTarget, rowSection, rowField) = ResolveScope(rule);
            var ast = ScopeGrammar.Lower(rule.Expression, ctx, rule.Id);

            int nodes = ScopeGrammar.Measure(ast, lim, rule.Id);
            if (nodes > lim.MaxAstNodes)
            {
                throw new RuleCompilationException(
                    RuleEngineCodes.CompileAstTooLarge,
                    $"rule '{rule.Id}': AST node count {nodes} exceeds the bound {lim.MaxAstNodes}",
                    rule.Id);
            }

            ValidateOperators(ast, rule.Id);
            var refs = ScopeGrammar.ExtractRefs(ast, rule.Id);
            if (refs.Count > lim.MaxReferencesPerRule)
            {
                throw new RuleCompilationException(
                    RuleEngineCodes.CompileTooManyRefs,
                    $"rule '{rule.Id}': reference count {refs.Count} exceeds the bound {lim.MaxReferencesPerRule}",
                    rule.Id);
            }

            compiled.Add(new CompiledRule
            {
                Source = rule,
                Ast = ast,
                OutputType = ScopeGrammar.OutputTypeFor(rule.Action),
                References = refs,
                StaticTarget = staticTarget,
                RowSection = rowSection,
                RowField = rowField,
            });
        }

        // A value rule can feed another rule.  First derive producer result sets, then
        // iterate the finite six-tag domain into consumers instead of treating every var
        // as AnyJson.  Cycles are rejected below; the bounded loop is defensive.
        ValidateCoreTypes(compiled);
        DetectCyclesAndDepth(compiled, lim);
        // Admission proof after closed-operator and static-DAG validation.  It is
        // deliberately distinct from the runtime step/wall-clock backstops.
        return new CompiledGraph(compiled, CoreWorkDerivation.DeriveGraph(compiled, lim));
    }

    private static void ValidateCoreTypes(IReadOnlyList<CompiledRule> rules)
    {
        var fields = new Dictionary<string, CoreJsonType>(StringComparer.Ordinal);
        foreach (var rule in rules.Where(rule => rule.Source.Action == RuleActionKind.Compute
            && rule.Source.Scope == RuleScope.Field))
            fields[rule.Source.ScopeTarget] = CoreTypeDerivation.Derive(rule.Ast, rule.Source.Id).Types;

        for (var pass = 0; pass <= rules.Count; pass++)
        {
            var next = new Dictionary<string, CoreJsonType>(fields, StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                var result = CoreTypeDerivation.Derive(rule.Ast, rule.Source.Id,
                    path => path.StartsWith("field.", StringComparison.Ordinal)
                        && fields.TryGetValue(path["field.".Length..], out var known)
                        ? known : CoreJsonType.AnyJson);
                if (rule.Source.Action == RuleActionKind.Compute && rule.Source.Scope == RuleScope.Field)
                    next[rule.Source.ScopeTarget] = result.Types;
            }
            if (next.Count == fields.Count && next.All(pair => fields.TryGetValue(pair.Key, out var old) && old == pair.Value)) return;
            fields = next;
        }
    }

    private static void ValidateOperators(JsonNode? node, string ruleId)
    {
        // The evaluator executes only single-property objects. Arrays and multi-property
        // objects are literal data; their contents do not become executable declarations.
        if (node is not JsonObject expression || expression.Count != 1) return;
        var operation = expression.First();
        if (!Operators.Contains(operation.Key))
            throw new RuleCompilationException(RuleEngineCodes.CompileInvalidExpression,
                $"rule '{ruleId}': unsupported operator '{operation.Key}'.", ruleId);

        var arguments = operation.Value is JsonArray array
            ? array.ToList()
            : new List<JsonNode?> { operation.Value };
        ValidateArity(operation.Key, arguments.Count, ruleId);

        foreach (var argument in arguments)
            ValidateOperators(argument, ruleId);
    }

    private static void ValidateArity(string operation, int count, string ruleId)
    {
        bool valid = operation switch
        {
            "var" => count is 1 or 2,
            "missing" => count >= 0,
            "missing_some" => count == 2,
            "==" or "!=" or "===" or "!==" or ">" or ">=" or "<" or "<=" or "in" => count == 2,
            "!" or "!!" => count == 1,
            "and" or "or" or "cat" => true,
            // A one-argument `if` is the decision-table skin's canonical otherwise-only
            // shape; the closed interpreter returns that argument unchanged.
            "if" => true,
            "+" or "-" or "*" or "/" or "%" or "min" or "max" or "money.add" or "money.sub" or "money.mul" => count >= 1,
            "agg" or "date.add" or "coding.is" => count == 3,
            "date.diff" => count == 2,
            "date.today" => count == 0,
            _ => false,
        };

        if (!valid)
            throw new RuleCompilationException(operation == "agg" ? RuleEngineCodes.CompileBadGrammar : RuleEngineCodes.CompileInvalidExpression,
                $"rule '{ruleId}': operator '{operation}' does not accept {count} argument(s).", ruleId);
    }

    private static (LowerContext Ctx, CellAddress? Target, string? RowSection, string? RowField) ResolveScope(RuleDefinition rule)
    {
        switch (rule.Scope)
        {
            case RuleScope.Field:
                return (new LowerContext(rule.Scope, rule.ScopeTarget, null), CellAddress.Field(rule.ScopeTarget), null, null);
            case RuleScope.Section:
                return (new LowerContext(rule.Scope, rule.ScopeTarget, null), CellAddress.Section(rule.ScopeTarget), null, null);
            case RuleScope.Schema:
                return (new LowerContext(rule.Scope, "", null), CellAddress.Schema(), null, null);
            case RuleScope.Row:
            {
                var parts = rule.ScopeTarget.Split('/');
                if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                    throw new RuleCompilationException(RuleEngineCodes.CompileBadGrammar,
                        $"rule '{rule.Id}': Row ScopeTarget '{rule.ScopeTarget}' must be 'section/field'", rule.Id);
                return (new LowerContext(rule.Scope, rule.ScopeTarget, parts[0]), null, parts[0], parts[1]);
            }
            case RuleScope.Table:
            {
                var parts = rule.ScopeTarget.Split('/');
                if (parts.Length != 3 || parts.Any(p => p.Length == 0))
                    throw new RuleCompilationException(RuleEngineCodes.CompileBadGrammar,
                        $"rule '{rule.Id}': Table ScopeTarget '{rule.ScopeTarget}' must be 'section/fn/col'", rule.Id);
                return (new LowerContext(rule.Scope, rule.ScopeTarget, parts[0]),
                    CellAddress.TableAggregate(parts[0], parts[1], parts[2]), null, null);
            }
            default:
                throw new RuleCompilationException(RuleEngineCodes.CompileBadGrammar,
                    $"rule '{rule.Id}': unknown scope", rule.Id);
        }
    }

    // ── abstract dependency graph (cycle + depth) ─────────────────────────────

    private static void DetectCyclesAndDepth(IReadOnlyList<CompiledRule> rules, RuleEngineLimits lim)
    {
        // dependsOn[node] = set of nodes the node depends on. Abstract node keys:
        //   F:<field>  R:<section>/<field>  A:<section>/<fn>/<col>  S:<id>  SCHEMA
        var dependsOn = new Dictionary<string, HashSet<string>>();
        var ruleOfTarget = new Dictionary<string, string>();

        void Edge(string node, string dep)
        {
            if (!dependsOn.TryGetValue(node, out var set)) dependsOn[node] = set = new HashSet<string>();
            set.Add(dep);
            dependsOn.TryAdd(dep, new HashSet<string>());
        }

        // Only VALUE-producing nodes form the dependency graph: Compute-rule targets +
        // aggregate cells. Sink rules (Validate / Visibility / Presentation) read values but
        // do not define their target's value, so they create no edges (a Validate reading its
        // own field is not a cycle). This mirrors the runtime value graph exactly.
        foreach (var r in rules)
        {
            bool isValueRule = r.Source.Action == RuleActionKind.Compute;
            string? target = isValueRule ? AbstractTarget(r) : null;
            if (target is not null)
            {
                ruleOfTarget[target] = r.Source.Id;
                dependsOn.TryAdd(target, new HashSet<string>());
            }

            foreach (var rf in r.References)
            {
                switch (rf)
                {
                    case FieldRef f when target is not null:
                        Edge(target, "F:" + f.Name);
                        break;
                    case RowFieldRef rfld when target is not null && r.RowSection is not null:
                        Edge(target, "R:" + r.RowSection + "/" + rfld.Field);
                        break;
                    case AggRef a:
                        // Aggregate cells are value nodes regardless of the referencing rule's kind.
                        string aggNode = "A:" + a.Section + "/" + a.Fn + "/" + a.Col;
                        dependsOn.TryAdd(aggNode, new HashSet<string>());
                        Edge(aggNode, "R:" + a.Section + "/" + a.Col);
                        if (target is not null) Edge(target, aggNode);
                        break;
                }
            }
        }

        // Cycle detection (DFS coloring) with path reconstruction.
        var color = new Dictionary<string, int>(); // 0 white, 1 gray, 2 black
        var stack = new List<string>();

        foreach (var node in dependsOn.Keys)
        {
            if (color.GetValueOrDefault(node) == 0)
            {
                var cycle = Dfs(node, dependsOn, color, stack, 0, lim.MaxDependencyDepth);
                if (cycle is not null)
                {
                    var pathKeys = cycle.Select(k => ruleOfTarget.TryGetValue(k, out var rid) ? $"{k} (rule {rid})" : k).ToList();
                    throw new RuleCompilationException(
                        RuleEngineCodes.CompileCycle,
                        "cyclic rule definition: " + string.Join(" -> ", cycle),
                        cyclePath: pathKeys);
                }
            }
        }

        // Longest-path depth (acyclic now): depth(node) = 1 + max depth(dep).
        var depthMemo = new Dictionary<string, int>();
        int Depth(string node, int descent)
        {
            if (descent > lim.MaxDependencyDepth)
                throw new RuleCompilationException(RuleEngineCodes.CompileDepthExceeded,
                    $"dependency depth exceeds the bound {lim.MaxDependencyDepth}");
            if (depthMemo.TryGetValue(node, out var d)) return d;
            depthMemo[node] = 0; // guard (graph is acyclic here)
            int best = 0;
            foreach (var dep in dependsOn[node]) best = Math.Max(best, 1 + Depth(dep, descent + 1));
            return depthMemo[node] = best;
        }

        foreach (var node in dependsOn.Keys)
        {
            if (Depth(node, 0) > lim.MaxDependencyDepth)
            {
                throw new RuleCompilationException(
                    RuleEngineCodes.CompileDepthExceeded,
                    $"dependency depth at '{node}' exceeds the bound {lim.MaxDependencyDepth}");
            }
        }
    }

    private static List<string>? Dfs(string node, Dictionary<string, HashSet<string>> dependsOn,
        Dictionary<string, int> color, List<string> stack, int descent, int maxDepth)
    {
        // Bound recursion depth (finding F9): a chain deeper than the depth cap is itself a depth
        // violation (the longest-path check also rejects it), so throw before the recursion can run
        // to full graph depth — defensive ahead of packet-carried (untrusted) definitions.
        if (descent > maxDepth)
            throw new RuleCompilationException(RuleEngineCodes.CompileDepthExceeded,
                $"dependency depth exceeds the bound {maxDepth}");
        color[node] = 1;
        stack.Add(node);
        foreach (var dep in dependsOn[node])
        {
            int c = color.GetValueOrDefault(dep);
            if (c == 1)
            {
                int idx = stack.IndexOf(dep);
                var cycle = stack.GetRange(idx, stack.Count - idx);
                cycle.Add(dep);
                return cycle;
            }
            if (c == 0)
            {
                var found = Dfs(dep, dependsOn, color, stack, descent + 1, maxDepth);
                if (found is not null) return found;
            }
        }
        color[node] = 2;
        stack.RemoveAt(stack.Count - 1);
        return null;
    }

    private static string AbstractTarget(CompiledRule r) => r.Source.Scope switch
    {
        RuleScope.Field => "F:" + r.Source.ScopeTarget,
        RuleScope.Section => "S:" + r.Source.ScopeTarget,
        RuleScope.Schema => "SCHEMA",
        RuleScope.Row => "R:" + r.RowSection + "/" + r.RowField,
        RuleScope.Table => "A:" + r.StaticTarget!.Value.SectionId + "/" + r.StaticTarget!.Value.Name,
        _ => "?",
    };
}
