using System.Text.Json.Nodes;

namespace Harborline.Foundation.RuleEngine.Compilation;

/// <summary>Finite abstract result domain used by the only JsonLogic admission fence.</summary>
[Flags]
public enum CoreJsonType
{
    None = 0, Null = 1, Boolean = 2, Number = 4, String = 8, Array = 16, Object = 32,
    AnyJson = Null | Boolean | Number | String | Array | Object,
}

/// <summary>
/// Derives every executable node's possible JSON result and possible runtime disposition.
/// It intentionally models the interpreter's coercions: arithmetic/date/money may still return a
/// coded runtime type error for dynamic scalar input; only a closed impossible container operand is
/// refused. Literal arrays and multi-key objects are data, never recursively executed.
/// </summary>
public static class CoreTypeDerivation
{
    public sealed record Result(CoreJsonType Types, bool CanError = false, bool CanPending = false);

    public static Result Derive(JsonNode? node, string ruleId,
        Func<string, CoreJsonType>? declaredInput = null,
        Func<string, RuleCompilationException>? refusal = null)
    {
        Result Visit(JsonNode? value)
        {
            if (value is null) return new(CoreJsonType.Null);
            if (value is JsonArray) return new(CoreJsonType.Array);
            if (value is JsonValue scalar)
            {
                if (scalar.TryGetValue<bool>(out _)) return new(CoreJsonType.Boolean);
                if (scalar.TryGetValue<string>(out _)) return new(CoreJsonType.String);
                return new(CoreJsonType.Number);
            }
            if (value is not JsonObject expression || expression.Count != 1) return new(CoreJsonType.Object);
            var (op, rawArgs) = expression.First();
            var args = rawArgs is JsonArray array ? array.ToList() : new List<JsonNode?> { rawArgs };
            var children = args.Select(Visit).ToArray(); // Visit all executable operands, even short-circuited ones.
            Result Join(CoreJsonType types, bool error = false, bool pending = false)
                => new(types, error || children.Any(x => x.CanError), pending || children.Any(x => x.CanPending));
            void Require(string name, CoreJsonType accepted, params int[] positions)
            {
                foreach (var index in positions.Where(i => i < children.Length))
                {
                    var actual = children[index].Types;
                    if ((actual & accepted) == 0)
                        throw (refusal?.Invoke(name) ?? new RuleCompilationException(RuleEngineCodes.CompileInvalidExpression,
                            $"rule '{ruleId}': operator '{name}' cannot coerce a closed container operand.", ruleId));
                }
            }
            switch (op)
            {
                case "var":
                {
                    var path = args.Count == 0 ? "" : VarPath(args[0]);
                    var primary = declaredInput?.Invoke(path) ?? CoreJsonType.AnyJson;
                    // A statically known graph producer is always represented by its computed
                    // cell; an unresolved dynamic path may yield null.  Preserve that null only
                    // for the AnyJson (unproven) resolver contract.
                    var fallback = children.Length > 1 ? children[1].Types
                        : primary == CoreJsonType.AnyJson ? CoreJsonType.Null : CoreJsonType.None;
                    // A resolver may carry an upstream Error/Pending independently of its
                    // value's JSON kind.  The fallback is evaluated eagerly by the evaluator.
                    return Join(primary | fallback, error: true, pending: true);
                }
                case "missing": return Join(CoreJsonType.Array, error: true, pending: true);
                case "missing_some": return Join(CoreJsonType.Array, error: true, pending: true);
                case "==": case "!=": case "===": case "!==": case "!": case "!!":
                case ">": case ">=": case "<": case "<=": case "in": case "coding.is":
                    // Relational comparisons use numeric coercion; every comparison also
                    // evaluates children which can surface resolver failure.
                    return Join(CoreJsonType.Boolean, error: op is ">" or ">=" or "<" or "<=");
                case "and": case "or":
                    return Join(children.Length == 0
                        ? CoreJsonType.Boolean
                        : children.Aggregate(CoreJsonType.None, (set, child) => set | child.Types));
                case "if":
                {
                    var output = CoreJsonType.Null;
                    for (var i = 1; i < children.Length; i += 2) output |= children[i].Types;
                    if (children.Length % 2 == 1) output |= children[^1].Types;
                    return Join(output);
                }
                case "cat": return Join(CoreJsonType.String);
                case "+": case "-": case "*": case "/": case "%": case "min": case "max":
                    Require(op, CoreJsonType.Null | CoreJsonType.Boolean | CoreJsonType.Number | CoreJsonType.String, Enumerable.Range(0, children.Length).ToArray()); return Join(CoreJsonType.Number, error: true);
                case "money.add": case "money.sub": case "money.mul":
                    // Money and date are JSON strings at the evaluator boundary.  Do not add
                    // nominal JSON tags which would reject their valid scalar coercions.
                    Require(op, CoreJsonType.Number | CoreJsonType.String, Enumerable.Range(0, children.Length).ToArray()); return Join(CoreJsonType.String, error: true);
                case "date.add": case "date.diff":
                    Require(op, CoreJsonType.String, 0);
                    if (op == "date.add") { Require(op, CoreJsonType.Null | CoreJsonType.Boolean | CoreJsonType.Number | CoreJsonType.String, 1); Require(op, CoreJsonType.String, 2); }
                    else Require(op, CoreJsonType.String, 1);
                    return Join(op == "date.diff" ? CoreJsonType.Number : CoreJsonType.String, error: true);
                case "date.today": return new(CoreJsonType.String);
                case "agg": return Join(CoreJsonType.AnyJson, error: true, pending: true);
                default: return Join(CoreJsonType.AnyJson, error: true, pending: true);
            }
        }
        return Visit(node);
    }

    public static CoreJsonType ParseDeclaredType(string? type, string ruleId, Func<string, RuleCompilationException> refusal)
        => (type ?? "any").ToLowerInvariant() switch
        {
            "any" => CoreJsonType.AnyJson, "null" => CoreJsonType.Null, "boolean" => CoreJsonType.Boolean,
            "number" => CoreJsonType.Number, "string" or "text" or "money" or "date" => CoreJsonType.String,
            "array" => CoreJsonType.Array, "object" or "coding" => CoreJsonType.Object,
            _ => throw refusal($"input declares unknown type '{type}'"),
        };

    private static string VarPath(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var path) => path,
        JsonArray a when a.Count > 0 && a[0] is JsonValue v && v.TryGetValue<string>(out var path) => path,
        _ => "",
    };
}
