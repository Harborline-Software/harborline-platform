using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;


namespace Harborline.Foundation.RuleEngine.Skins;

// ─────────────────────────────────────────────────────────────────────────────
//  ADR 0146 D2 — the FORMULA authoring skin (compile layer).
//
//  A formula is a named, typed-input expression compiling to the SAME AST as a
//  hand-authored rule (binding constraint #3 — one expression language). The skin
//  adds one thing over a bare `RuleDefinition`: a declared input manifest. The
//  compiler enforces that every `var` the expression reads is a DECLARED input — an
//  undeclared reference is a publish rejection, not a silent free variable. This is
//  what "named typed inputs" buys: a formula's data dependencies are explicit and
//  checkable at authoring time.
//
//  The declared type is part of admission: it constrains the closed operator
//  contracts before lowering produces the ordinary RuleDefinition consumed by D1.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One declared input of a <see cref="FormulaSkin"/> — a var ref + its declared type.</summary>
/// <param name="Ref">The var path the expression references (e.g. <c>amount</c>, <c>field.qty</c>,
/// <c>self</c>). Must match the <c>var</c> path used in <see cref="FormulaSkin.Expression"/> exactly
/// (pre-lowering).</param>
/// <param name="Type">The declared type (<c>number</c> / <c>string</c> / <c>boolean</c> / <c>any</c>) —
/// authoring metadata in v1 (the compiler enforces declared-ness, not the type).</param>
public sealed record FormulaInput(string Ref, string Type = "any");

/// <summary>
/// The formula authoring skin (ADR 0146 D2). Compile it with <see cref="FormulaCompiler.Compile"/> to a
/// <see cref="RuleDefinition"/> the D1 core evaluates.
/// </summary>
/// <param name="RuleId">The stable rule id the produced <see cref="RuleDefinition"/> carries.</param>
/// <param name="Scope">The rule scope (where the outcome applies).</param>
/// <param name="ScopeTarget">The scope target (field/section path); empty for Schema.</param>
/// <param name="Action">The output action the outcome is interpreted as (typically Compute).</param>
/// <param name="Inputs">The declared named inputs — every var the expression reads must appear here.</param>
/// <param name="Expression">The harborline-jsonlogic/v1 expression (raw, pre-lowering).</param>
public sealed record FormulaSkin(
    string RuleId,
    RuleScope Scope,
    string ScopeTarget,
    RuleActionKind Action,
    IReadOnlyList<FormulaInput> Inputs,
    JsonNode? Expression);

/// <summary>
/// Lowers a <see cref="FormulaSkin"/> to a <see cref="RuleDefinition"/>, enforcing that every referenced var
/// is a declared input. Pure + deterministic — both tiers produce the byte-identical expression.
/// </summary>
public static class FormulaCompiler
{
    /// <summary>Compiles the skin; throws <see cref="RuleCompilationException"/> (a <c>rule.skin.*</c> code)
    /// on an empty expression or an undeclared reference.</summary>
    public static RuleDefinition Compile(FormulaSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        string id = skin.RuleId;

        if (skin.Expression is null)
            throw Reject(SkinCodes.FormulaEmpty, id, "a formula must declare a non-empty expression");

        var declared = new HashSet<string>(skin.Inputs.Select(i => i.Ref), StringComparer.Ordinal);
        var types = skin.Inputs.ToDictionary(i => i.Ref, i => ParseType(i.Type, id), StringComparer.Ordinal);
        foreach (var referenced in CollectVarPaths(skin.Expression))
        {
            if (!declared.Contains(referenced))
                throw Reject(SkinCodes.FormulaUndeclaredRef, id,
                    $"expression references '{referenced}', which is not a declared input (declared: {string.Join(", ", declared.OrderBy(x => x, StringComparer.Ordinal))})");
        }

        _ = Infer(skin.Expression, types, id);

        var definition = RuleDefinitionFactory.Create(
            Id: id,
            Tier: RuleTier.JsonLogic,
            Scope: skin.Scope,
            ScopeTarget: skin.ScopeTarget,
            Expression: skin.Expression.ToJsonString(),
            Action: skin.Action);
        // Authoring callers receive only a rule that has crossed the exact same deterministic
        // parser/grammar/arity/bound admission fence as a published bare rule.
        _ = RuleCompiler.Compile(new[] { definition });
        return definition;
    }

    /// <summary>Every distinct <c>var</c> path in the expression (pre-lowering), in first-seen order.</summary>
    private static IEnumerable<string> CollectVarPaths(JsonNode? node)
    {
        var seen = new List<string>();
        var set = new HashSet<string>(StringComparer.Ordinal);
        Walk(node, seen, set);
        return seen;
    }

    private static void Walk(JsonNode? node, List<string> seen, HashSet<string> set)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count == 1 && obj.ContainsKey("var"):
            {
                string path = VarPathOf(obj["var"]);
                if (path.Length > 0 && set.Add(path)) seen.Add(path);
                // a {"var":[path, default]} may carry a nested default that references more vars
                if (obj["var"] is JsonArray a && a.Count > 1) Walk(a[1], seen, set);
                break;
            }
            case JsonObject obj:
                foreach (var (_, v) in obj) Walk(v, seen, set);
                break;
            case JsonArray arr:
                foreach (var item in arr) Walk(item, seen, set);
                break;
        }
    }

    private static string VarPathOf(JsonNode? varNode)
        => varNode is JsonArray a
            ? (a.Count > 0 ? a[0]?.GetValue<string>() ?? "" : "")
            : varNode?.GetValue<string>() ?? "";

    private enum FormulaType { Any, Null, Boolean, Number, String, Array, Object, Coding, Money, Date }

    private static FormulaType ParseType(string? type, string ruleId) => (type ?? "any").ToLowerInvariant() switch
    {
        "any" => FormulaType.Any,
        "null" => FormulaType.Null,
        "boolean" => FormulaType.Boolean,
        "number" => FormulaType.Number,
        "string" or "text" => FormulaType.String,
        "array" => FormulaType.Array,
        "object" => FormulaType.Object,
        "coding" => FormulaType.Coding,
        "money" => FormulaType.Money,
        "date" => FormulaType.Date,
        _ => throw Reject(SkinCodes.FormulaTypeMismatch, ruleId, $"input declares unknown type '{type}'"),
    };

    private static FormulaType Infer(JsonNode? node, IReadOnlyDictionary<string, FormulaType> declared, string ruleId)
    {
        if (node is null) return FormulaType.Null;
        if (node is JsonArray) return FormulaType.Array;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out _)) return FormulaType.Boolean;
            if (value.TryGetValue<string>(out _)) return FormulaType.String;
            if (value.TryGetValue<double>(out _)) return FormulaType.Number;
            return FormulaType.Any;
        }
        if (node is not JsonObject expression || expression.Count != 1) return FormulaType.Object;
        var (op, argument) = expression.First();
        var args = argument is JsonArray array ? array.ToList() : new List<JsonNode?> { argument };
        FormulaType Arg(int index) => index < args.Count ? Infer(args[index], declared, ruleId) : FormulaType.Null;
        void Require(int index, params FormulaType[] accepted)
        {
            var actual = Arg(index);
            if (actual != FormulaType.Any && !accepted.Contains(actual))
                throw Reject(SkinCodes.FormulaTypeMismatch, ruleId,
                    $"operator '{op}' argument {index + 1} requires {string.Join("/", accepted).ToLowerInvariant()} but declared type is {actual.ToString().ToLowerInvariant()}");
        }

        switch (op)
        {
            case "var":
                var path = VarPathOf(argument);
                return declared.TryGetValue(path, out var type) ? type : FormulaType.Any;
            case "money.add": case "money.sub": case "money.mul":
                for (int i = 0; i < args.Count; i++) Require(i, FormulaType.String, FormulaType.Number, FormulaType.Money);
                return FormulaType.Money;
            case "date.add":
                Require(0, FormulaType.String, FormulaType.Date); Require(1, FormulaType.Number, FormulaType.String, FormulaType.Boolean); Require(2, FormulaType.String);
                return FormulaType.Date;
            case "date.diff":
                Require(0, FormulaType.String, FormulaType.Date); Require(1, FormulaType.String, FormulaType.Date);
                return FormulaType.Number;
            case "date.today": return FormulaType.Date;
            case "coding.is":
                Require(0, FormulaType.Coding, FormulaType.Array, FormulaType.Object); Require(1, FormulaType.String); Require(2, FormulaType.String);
                return FormulaType.Boolean;
            case "!": case "!!": case "==": case "!=": case "===": case "!==": case ">": case ">=": case "<": case "<=": case "in": return FormulaType.Boolean;
            case "+": case "-": case "*": case "/": case "%": case "min": case "max":
                for (int i = 0; i < args.Count; i++) Require(i, FormulaType.Number, FormulaType.String, FormulaType.Boolean);
                return FormulaType.Number;
            case "cat": return FormulaType.String;
            case "missing": case "missing_some": return FormulaType.Array;
            default: return FormulaType.Any;
        }
    }

    private static RuleCompilationException Reject(string code, string ruleId, string message)
        => new(code, $"formula skin '{ruleId}': {message}", ruleId);
}
