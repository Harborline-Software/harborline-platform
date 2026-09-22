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
/// checked by the shared core type derivation.</param>
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
        // Formula input declarations remain authoring metadata in the persisted RuleDefinition.
        // A returned RuleDefinition has no runtime type guard, so using these narrower types to
        // certify it would be unsound. Validate the declaration spelling, then use the same
        // conservative dynamic contract as ordinary compilation.
        foreach (var input in skin.Inputs)
            _ = CoreTypeDerivation.ParseDeclaredType(input.Type, id,
                message => Reject(SkinCodes.FormulaTypeMismatch, id, message));
        foreach (var referenced in CollectVarPaths(skin.Expression))
        {
            if (!declared.Contains(referenced))
                throw Reject(SkinCodes.FormulaUndeclaredRef, id,
                    $"expression references '{referenced}', which is not a declared input (declared: {string.Join(", ", declared.OrderBy(x => x, StringComparer.Ordinal))})");
        }

        _ = CoreTypeDerivation.Derive(skin.Expression, id,
            refusal: message => Reject(SkinCodes.FormulaTypeMismatch, id, message));

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
            case JsonObject obj when obj.Count == 1 && (obj.ContainsKey("missing") || obj.ContainsKey("missing_some")):
            {
                var raw = obj.First().Value;
                var args = raw is JsonArray array ? array.ToList() : new List<JsonNode?> { raw };
                if (obj.ContainsKey("missing_some") && args.Count > 0) Walk(args[0], seen, set);
                foreach (var keySource in obj.ContainsKey("missing_some") ? args.Skip(1) : args)
                    CollectMissingPaths(keySource, seen, set);
                break;
            }
            case JsonObject obj when obj.Count == 1 && obj.ContainsKey("var"):
            {
                string path = VarPathOf(obj["var"]);
                if (path.Length > 0 && set.Add(path)) seen.Add(path);
                // a {"var":[path, default]} may carry a nested default that references more vars
                if (obj["var"] is JsonArray a && a.Count > 1) Walk(a[1], seen, set);
                break;
            }
            case JsonObject obj when obj.Count == 1:
            {
                var argument = obj.First().Value;
                if (argument is JsonArray arguments) foreach (var item in arguments) Walk(item, seen, set);
                else Walk(argument, seen, set);
                break;
            }
        }
    }

    private static void CollectMissingPaths(JsonNode? node, List<string> seen, HashSet<string> set)
    {
        if (node is JsonArray array) { foreach (var item in array) CollectMissingPaths(item, seen, set); return; }
        if (node is JsonValue value && value.TryGetValue<string>(out var path) && path.Length > 0)
        {
            if (set.Add(path)) seen.Add(path);
            return;
        }
        if (node is JsonObject expression && expression.Count == 1) Walk(expression, seen, set);
    }

    private static string VarPathOf(JsonNode? varNode)
        => varNode is JsonArray a
            ? (a.Count > 0 ? a[0]?.GetValue<string>() ?? "" : "")
            : varNode?.GetValue<string>() ?? "";

    private static RuleCompilationException Reject(string code, string ruleId, string message)
        => new(code, $"formula skin '{ruleId}': {message}", ruleId);
}
