using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Evaluation;

using L = Harborline.Foundation.RuleEngine.Evaluation.HarborlineJsonLogic;

namespace Harborline.Foundation.RuleEngine.Functions;

/// <summary>One executable built-in: its reference, its arity and its implementation.</summary>
public sealed class BuiltInFunctionDefinition
{
    internal BuiltInFunctionDefinition(string key, string category, int minArity, int maxArity, bool authorable,
        Func<List<JsonNode?>, EvalContext, JsonNode?> invoke)
    {
        Reference = new BuiltInFunction(key);
        Category = category;
        MinArity = minArity;
        MaxArity = maxArity;
        Authorable = authorable;
        Invoke = invoke;
    }

    /// <summary>The discriminated reference the register owns.</summary>
    public BuiltInFunction Reference { get; }

    /// <summary>The operator key.</summary>
    public string Key => Reference.Key;

    /// <summary>The palette group.</summary>
    public string Category { get; }

    /// <summary>Fewest arguments admitted.</summary>
    public int MinArity { get; }

    /// <summary>Most arguments admitted; -1 is unbounded.</summary>
    public int MaxArity { get; }

    /// <summary>False only for <c>var</c>, which authors reach through a declared reference.</summary>
    public bool Authorable { get; }

    internal Func<List<JsonNode?>, EvalContext, JsonNode?> Invoke { get; }

    internal bool Admits(int count) => count >= MinArity && (MaxArity < 0 || count <= MaxArity);
}

/// <summary>
/// The R1 function register (DES-0018 <c>rules-eng-27</c>): the single source of truth for the
/// executable built-ins. The evaluator dispatches through it, the compiler admits keys and arity from
/// it, and both editor lanes generate their function palette from it. Only the built-in arm of
/// <see cref="FunctionReference"/> is inhabited; every entry carries its implementation, so nothing is
/// registered that cannot execute. The package-provider arm is T-685.
/// </summary>
public static class BuiltInFunctionRegister
{
    /// <summary>The folds <c>agg</c> accepts over a bounded child collection (<c>rules-bound-4</c>).</summary>
    public static IReadOnlyList<string> AggregateFolds { get; } = ["sum", "count", "avg", "min", "max", "any", "all"];

    /// <summary>Every executable built-in, in palette order.</summary>
    public static IReadOnlyList<BuiltInFunctionDefinition> Functions { get; } =
    [
        Def("var", "data", 1, 2, L.EvalVar, authorable: false),
        Def("missing", "data", 0, -1, L.EvalMissing),
        Def("missing_some", "data", 2, 2, L.EvalMissingSome),

        Def("==", "logic", 2, 2, (a, c) => L.Bool(L.LooseEquals(L.Eval(a, 0, c), L.Eval(a, 1, c)))),
        Def("!=", "logic", 2, 2, (a, c) => L.Bool(!L.LooseEquals(L.Eval(a, 0, c), L.Eval(a, 1, c)))),
        Def("===", "logic", 2, 2, (a, c) => L.Bool(L.StrictEquals(L.Eval(a, 0, c), L.Eval(a, 1, c)))),
        Def("!==", "logic", 2, 2, (a, c) => L.Bool(!L.StrictEquals(L.Eval(a, 0, c), L.Eval(a, 1, c)))),
        Def("!", "logic", 1, 1, (a, c) => L.Bool(!L.IsTruthy(L.Eval(a, 0, c)))),
        Def("!!", "logic", 1, 1, (a, c) => L.Bool(L.IsTruthy(L.Eval(a, 0, c)))),
        Def("and", "logic", 0, -1, L.EvalAnd),
        Def("or", "logic", 0, -1, L.EvalOr),
        // A one-argument `if` is the decision-table skin's otherwise-only shape.
        Def("if", "logic", 0, -1, L.EvalIf),

        Def(">", "compare", 2, 2, (a, c) => L.Bool(L.Compare(a, c) > 0)),
        Def(">=", "compare", 2, 2, (a, c) => L.Bool(L.Compare(a, c) >= 0)),
        Def("<", "compare", 2, 2, (a, c) => L.Bool(L.Compare(a, c) < 0)),
        Def("<=", "compare", 2, 2, (a, c) => L.Bool(L.Compare(a, c) <= 0)),

        Def("+", "arithmetic", 1, -1, (a, c) => L.Arith(a, c, '+')),
        Def("-", "arithmetic", 1, -1, (a, c) => L.Arith(a, c, '-')),
        Def("*", "arithmetic", 1, -1, (a, c) => L.Arith(a, c, '*')),
        Def("/", "arithmetic", 1, -1, (a, c) => L.Arith(a, c, '/')),
        Def("%", "arithmetic", 1, -1, (a, c) => L.Arith(a, c, '%')),
        Def("min", "arithmetic", 1, -1, (a, c) => L.MinMax(a, c, min: true)),
        Def("max", "arithmetic", 1, -1, (a, c) => L.MinMax(a, c, min: false)),

        Def("in", "membership", 2, 2, L.EvalIn),
        Def("cat", "text", 0, -1, L.EvalCat),

        Def("agg", "fold", 3, 3, L.EvalAgg),
        Def("money.add", "money", 1, -1, (a, c) => L.Money(a, c, '+')),
        Def("money.sub", "money", 1, -1, (a, c) => L.Money(a, c, '-')),
        Def("money.mul", "money", 1, -1, (a, c) => L.Money(a, c, '*')),
        Def("date.add", "date", 3, 3, L.DateAdd),
        Def("date.diff", "date", 2, 2, L.DateDiff),
        Def("date.today", "date", 0, 0, (_, c) => JsonValue.Create(DateMath.Today(c.Now))),
        Def("coding.is", "coding", 3, 3, L.EvalCodingIs),
    ];

    private static readonly Dictionary<string, BuiltInFunctionDefinition> ByKey =
        Functions.ToDictionary(function => function.Key, StringComparer.Ordinal);

    /// <summary>Resolves a key to its single registered definition.</summary>
    public static bool TryResolve(string key, out BuiltInFunctionDefinition function)
        => ByKey.TryGetValue(key, out function!);

    /// <summary>Resolves a key to the register's one reference; throws when unregistered.</summary>
    public static BuiltInFunction Resolve(string key) => TryResolve(key, out var function)
        ? function.Reference
        : throw new FunctionReferenceException(FunctionReferenceCodec.UnknownBuiltIn, $"'{key}' is not a registered built-in");

    private static BuiltInFunctionDefinition Def(string key, string category, int min, int max,
        Func<List<JsonNode?>, EvalContext, JsonNode?> invoke, bool authorable = true)
        => new(key, category, min, max, authorable, invoke);
}
