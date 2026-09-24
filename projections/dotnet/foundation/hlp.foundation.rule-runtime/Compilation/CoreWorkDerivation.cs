using System.Numerics;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.RuleEngine.Compilation;

/// <summary>
/// Compiler-owned finite transfer proof for the closed evaluator.  It deliberately
/// shares <see cref="CoreTypeDerivation"/>'s executable/literal boundary; it is not
/// an evaluator, AST-count alias, or runtime fuel counter.  The graph combines these
/// transfers with its checked static DAG and bounded dynamic-demand scheduler.
/// </summary>
internal static class CoreWorkDerivation
{
    private static readonly BigInteger InputBytes = RuntimeInputEnvelope.MaxUtf8Bytes;
    private static readonly BigInteger InputNodes = RuntimeInputEnvelope.MaxNodes;
    private static readonly BigInteger MoneyDigits = 4_096;
    private static readonly BigInteger MoneyScale = 4_096;
    private static readonly BigInteger NumberBytes = 32;
    // Two 4096-digit parsed operands may be aligned by another 4096 zeroes, and
    // addition/subtraction may carry once.  Sign, point and JSON quotes complete
    // the serialized result transfer.
    private static readonly BigInteger MoneyAddResultBytes = MoneyDigits + MoneyScale + 1 + 4;
    private static readonly BigInteger MoneyMulResultBytes = MoneyDigits + 4;

    internal sealed record NodeProof(BigInteger Result, BigInteger Work, BigInteger Reads, BigInteger AggregateReads);

    internal static NodeProof Derive(JsonNode? node, string ruleId, BigInteger? dependencyResult = null,
        Func<string, BigInteger?>? staticReference = null, BigInteger? aggregateResult = null)
    {
        // Do not grow a second semantic walker: the established shared type walker
        // defines precisely which descendants execute and which are inert JSON data.
        _ = CoreTypeDerivation.Derive(node, ruleId);
        return Visit(node, dependencyResult ?? InputBytes, aggregateResult ?? InputBytes, staticReference);
    }

    internal static WorkProof DeriveGraph(IReadOnlyList<CompiledRule> rules, RuleEngineLimits limits)
    {
        var aggregateResult = Math.Max(0, limits.MaxTableRowsPerAggregate) * MoneyAddResultBytes;
        BigInteger dynamicResult = InputBytes;
        NodeProof[] proofs = [];
        var staticResults = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        // Static dependencies are already a checked DAG. Reach a fixed point of this
        // admitted program instead of iterating a separately configurable host depth.
        for (int pass = 0; pass <= rules.Count; pass++)
        {
            proofs = rules.Select(rule => Derive(rule.Ast, rule.Source.Id, InputBytes,
                path => staticResults.TryGetValue(path, out var result) ? result : null, aggregateResult)).ToArray();
            var next = new Dictionary<string, BigInteger>(staticResults, StringComparer.Ordinal);
            for (int index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                if (rule.Source.Action != RuleActionKind.Compute) continue;
                if (rule.Source.Scope == RuleScope.Field) next["field." + rule.Source.ScopeTarget] = proofs[index].Result;
                if (rule.Source.Scope == RuleScope.Row)
                {
                    int slash = rule.Source.ScopeTarget.IndexOf('/');
                    if (slash >= 0)
                    {
                        var key = "row." + rule.Source.ScopeTarget[(slash + 1)..];
                        next[key] = next.TryGetValue(key, out var old) ? BigInteger.Max(old, proofs[index].Result) : proofs[index].Result;
                    }
                }
            }
            dynamicResult = Max(BigInteger.Max(InputBytes, aggregateResult), proofs.Select(proof => proof.Result));
            bool unchanged = next.Count == staticResults.Count && next.All(pair => staticResults.TryGetValue(pair.Key, out var old) && old == pair.Value);
            staticResults = next;
            if (unchanged) break;
        }

        // Active-cycle refusal and per-generation completion mean a successful dynamic
        // demand chain has no repeated cell.  Its semantic height is consequently the
        // validated cell inventory, not the active-stack limit.  Each closed transfer
        // either selects a value, makes a fixed scalar, or serializes AST/input-node
        // children; compose that affine transfer by exponentiation, not by re-walking
        // a dependency-free expression once for every possible cell.
        if (rules.Any(rule => rule.References.OfType<DynamicReadRef>().Any()))
        {
            var childrenPerCell = new BigInteger(Math.Max(limits.MaxAstNodes, RuntimeInputEnvelope.MaxNodes));
            dynamicResult = RepeatedCellEnvelope(dynamicResult, 6 * childrenPerCell, aggregateResult,
                Math.Max(0, limits.MaxGraphNodes));
        }

        BigInteger localWork = 0, resolverReads = 0, aggregateReads = 0, maxCellWork = 1;
        for (int index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var proof = proofs[index];
            var rowExpansion = rule.Source.Scope == RuleScope.Row
                ? new BigInteger(Math.Max(0, limits.MaxTableRowsPerAggregate)) : BigInteger.One;
            // All rules evaluate into an outcome; a Compute also evaluates its value cell.
            var executions = rowExpansion * (rule.Source.Action == RuleActionKind.Compute ? 2 : 1);
            // Outcome/value projection owns copies after evaluator execution.
            localWork += executions * (proof.Work + proof.Result);
            resolverReads += executions * proof.Reads;
            aggregateReads += executions * proof.AggregateReads;
            maxCellWork = BigInteger.Max(maxCellWork, proof.Work);
        }

        if (rules.Any(rule => rule.References.OfType<DynamicReadRef>().Any()))
        {
            // A dynamically selected value can be converted, serialized and copied by
            // every executable operand position; use the same finite child factor as
            // result composition for one demanded cell's work transfer.
            var childFactor = 6 * new BigInteger(Math.Max(limits.MaxAstNodes, RuntimeInputEnvelope.MaxNodes));
            maxCellWork = BigInteger.Max(maxCellWork, childFactor * (BigInteger.Max(BigInteger.Max(dynamicResult, aggregateResult), InputBytes)));
        }

        // Dynamic reads are actual scheduling work: a lookup and (at most) the accepted
        // active-chain demand.  Completed cells remain memoized only for this generation.
        var dynamicDemand = resolverReads * (BigInteger.One + Math.Max(0, limits.MaxGraphNodes) * maxCellWork);
        // Each aggregate fold visits only the validated row envelope / configured row cap;
        // conversion and copying of a captured value are bounded by that input envelope.
        var foldWork = aggregateReads * Math.Max(0, limits.MaxTableRowsPerAggregate)
            * (BigInteger.Max(BigInteger.Max(dynamicResult, aggregateResult), InputBytes) + 1);
        return new WorkProof(dynamicResult, localWork + dynamicDemand + foldWork);
    }

    private static NodeProof Visit(JsonNode? node, BigInteger dependencyResult, BigInteger aggregateResult,
        Func<string, BigInteger?>? staticReference)
    {
        if (node is not JsonObject expression || expression.Count != 1)
        {
            var bytes = LiteralBytes(node);
            return new(bytes, 1 + bytes, 0, 0);
        }

        var (op, raw) = expression.First();
        var args = raw is JsonArray array ? array.ToList() : new List<JsonNode?> { raw };
        var children = args.Select(arg => Visit(arg, dependencyResult, aggregateResult, staticReference)).ToArray();
        var childWork = children.Aggregate(BigInteger.Zero, (total, child) => total + child.Work);
        var childReads = children.Aggregate(BigInteger.Zero, (total, child) => total + child.Reads);
        var aggregateReads = children.Aggregate(BigInteger.Zero, (total, child) => total + child.AggregateReads);
        var childBytes = children.Aggregate(BigInteger.Zero, (total, child) => total + child.Result);
        var childResult = Max(BigInteger.Zero, children.Select(child => child.Result));
        NodeProof Base(BigInteger result, BigInteger transfer, BigInteger? reads = null, BigInteger? aggs = null)
            => new(result, 1 + childWork + transfer, reads ?? childReads, aggs ?? aggregateReads);

        return op switch
        {
            // Both interpreters eagerly evaluate var's fallback; every resolver call has work.
            "var" => Var(raw, childResult, childWork, childBytes, childReads, aggregateReads, dependencyResult, staticReference),
            "missing" => Missing(args, children, childWork, childReads, aggregateReads, childResult),
            "missing_some" => MissingSome(children, childWork, childReads, aggregateReads, childBytes),
            // Object/array equality serializes complete operands in both tiers.
            "==" or "!=" or "===" or "!==" => Base(5, childBytes),
            "!" or "!!" or ">" or ">=" or "<" or "<=" => Base(5, childBytes),
            // Short circuit selects a subset at runtime; all executable children give the upper transfer.
            "and" or "or" or "if" => Base(BigInteger.Max(5, childResult), 0),
            "+" or "-" or "*" or "/" or "%" or "min" or "max" => Base(NumberBytes, childBytes),
            "in" => Base(5, InputNodes * (childBytes + 1)),
            "cat" => Cat(args.Count, children, childWork, childReads, aggregateReads),
            "agg" => Base(aggregateResult, childBytes + 1, childReads + 1, aggregateReads + 1),
            // Parsing scans complete input before refusal; accepted decimal digit/scale and
            // alignment/multiplication retain the existing 4096 bound.
            "money.add" or "money.sub" => Base(MoneyDigits + MoneyScale + args.Count + 4,
                childBytes + args.Count * MoneyDigits * MoneyDigits + args.Count * (MoneyDigits + MoneyScale + args.Count + 4)),
            "money.mul" => Base(MoneyMulResultBytes,
                childBytes + args.Count * MoneyDigits * MoneyDigits + args.Count * MoneyMulResultBytes),
            "date.add" => Base(12, childBytes),
            "date.diff" => Base(NumberBytes, childBytes),
            "date.today" => Base(12, 0),
            "coding.is" => Base(5, InputNodes * (childBytes + 1)),
            // ValidateOperators closes this set first.  A new evaluator arm cannot silently
            // receive a generic bound: its transfer is a required admission decision.
            _ => throw new InvalidOperationException($"work proof has no transfer for '{op}'"),
        };
    }

    private static NodeProof Var(JsonNode? raw, BigInteger childResult, BigInteger childWork, BigInteger childBytes, BigInteger childReads,
        BigInteger aggregateReads, BigInteger dependencyResult, Func<string, BigInteger?>? staticReference)
    {
        string? path = raw switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonArray array when array.Count > 0 && array[0] is JsonValue value && value.TryGetValue<string>(out var text) => text,
            _ => null,
        };
        var resolved = path is null ? dependencyResult : staticReference?.Invoke(path) ?? InputBytes;
        return new(BigInteger.Max(resolved, childResult), 1 + childWork + childBytes + 1, childReads + 1, aggregateReads);
    }

    private static NodeProof Missing(IReadOnlyList<JsonNode?> args, IReadOnlyList<NodeProof> children,
        BigInteger childWork, BigInteger childReads, BigInteger aggregateReads, BigInteger childResult)
    {
        var keyCount = args.Count == 1 ? InputNodes : new BigInteger(args.Count);
        var keyBytes = BigInteger.Max(childResult, BigInteger.One);
        var firstTwice = args.Count == 1 && children.Count > 0 ? children[0].Work : BigInteger.Zero;
        var repeatedAggregateReads = args.Count == 1 && children.Count > 0 ? children[0].AggregateReads : BigInteger.Zero;
        return new(2 + keyCount * (keyBytes + 1), 1 + childWork + firstTwice + keyCount * (keyBytes + 1),
            childReads + (args.Count == 1 && children.Count > 0 ? children[0].Reads : BigInteger.Zero) + keyCount,
            aggregateReads + repeatedAggregateReads);
    }

    private static NodeProof MissingSome(IReadOnlyList<NodeProof> children, BigInteger childWork,
        BigInteger childReads, BigInteger aggregateReads, BigInteger childBytes)
    {
        var keyBytes = BigInteger.Max(children.Count > 1 ? children[1].Result : BigInteger.Zero, BigInteger.One);
        return new(2 + InputNodes * (keyBytes + 1), 1 + childWork + childBytes + InputNodes * (keyBytes + 1),
            childReads + InputNodes, aggregateReads);
    }

    private static NodeProof Cat(int count, IReadOnlyList<NodeProof> children, BigInteger childWork,
        BigInteger childReads, BigInteger aggregateReads)
    {
        var output = children.Aggregate(BigInteger.Zero, (total, child) => total + 2 + 6 * child.Result);
        // Count × final length conservatively covers copying of all growing prefixes.
        return new(output, 1 + childWork + count * output, childReads, aggregateReads);
    }

    private static BigInteger LiteralBytes(JsonNode? node) => node switch
    {
        null => 4,
        JsonArray array => 2 + array.Aggregate(BigInteger.Zero, (total, child) => total + LiteralBytes(child)) + Math.Max(0, array.Count - 1),
        JsonObject obj when obj.Count != 1 => 2 + obj.Aggregate(BigInteger.Zero,
            (total, pair) => total + 2 + 6 * pair.Key.Length + 1 + LiteralBytes(pair.Value)) + Math.Max(0, obj.Count - 1),
        JsonObject obj => 2 + obj.Aggregate(BigInteger.Zero,
            (total, pair) => total + 2 + 6 * pair.Key.Length + 1 + LiteralBytes(pair.Value)),
        JsonValue value when value.TryGetValue<string>(out var text) => 2 + 6 * text.Length,
        JsonValue value when value.TryGetValue<bool>(out var boolean) => boolean ? 4 : 5,
        _ => NumberBytes,
    };

    private static BigInteger Max(BigInteger initial, IEnumerable<BigInteger> values)
        => values.Aggregate(initial, BigInteger.Max);

    private static BigInteger RepeatedCellEnvelope(BigInteger seed, BigInteger factor, BigInteger addend, int cells)
    {
        if (cells <= 0) return seed;
        if (factor.IsZero) return addend;
        if (factor.IsOne) return seed + cells * addend;
        var power = BigInteger.Pow(factor, cells);
        return power * seed + addend * ((power - BigInteger.One) / (factor - BigInteger.One));
    }
}

/// <summary>Non-persisted compiler proof; it never appears in canonical authored definitions.</summary>
public sealed record WorkProof(BigInteger MaximumResultBytes, BigInteger MaximumEvaluationWork);
