using System.Globalization;
using System.Text.Json.Nodes;
using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.RuleEngine.Context;

/// <summary>Owned finite Taxonomy answers joined to the host's independently admitted pins.</summary>
public sealed class CodingSubsumptionSnapshot
{
    private readonly string[][] _bindings;
    private readonly Answer[] _answers;
    private sealed record Answer(string System, string Ancestor, string Descendant, bool Subsumes);

    private CodingSubsumptionSnapshot(string[][] bindings, Answer[] answers)
    {
        _bindings = bindings;
        _answers = answers;
    }

    /// <summary>Copies bounded tuple documents and refuses mismatched tenant, pins, order or keys.</summary>
    public static CodingSubsumptionSnapshot FromJsonText(string answersJsonText, string expectedBindingsJsonText)
    {
        var expected = Tuple(RuntimeInputEnvelope.Parse(expectedBindingsJsonText, nameof(expectedBindingsJsonText)), 3);
        var actual = Tuple(RuntimeInputEnvelope.Parse(answersJsonText, nameof(answersJsonText)), 4);
        if (Text(expected[0]) != "harborline-taxonomy-coding-bindings/1"
            || Text(actual[0]) != "harborline-taxonomy-coding-answers/1"
            || Text(expected[1]) != Text(actual[1])) throw Invalid();
        var bindings = Bindings(expected[2]);
        var supplied = Bindings(actual[2]);
        if (bindings.Length != supplied.Length || bindings.Where((row, i) => !row.SequenceEqual(supplied[i], StringComparer.Ordinal)).Any()) throw Invalid();
        if (actual[3] is not JsonArray { Count: > 0 } rows) throw Invalid();
        var answers = new List<Answer>();
        foreach (var node in rows)
        {
            var row = Tuple(node, 4);
            var system = Text(row[0]); var ancestor = Text(row[1]); var descendant = Text(row[2]);
            if (row[3] is not JsonValue boolean || !boolean.TryGetValue<bool>(out var subsumes)
                || !bindings.Any(binding => binding[0] == system)) throw Invalid();
            var answer = new Answer(system, ancestor, descendant, subsumes);
            if (answers.Count > 0 && Compare(answers[^1], answer) >= 0) throw Invalid();
            answers.Add(answer);
        }
        return new CodingSubsumptionSnapshot(bindings, answers.ToArray());
    }

    internal void RequireBinding(string system, EvalBudget budget)
    {
        foreach (var binding in _bindings)
        {
            budget.Charge();
            if (Equal(binding[0], system, budget)) return;
        }
        throw new RuleEvalException(RuleError.Of(RuleEngineCodes.CodingBindingMissing));
    }

    internal bool Lookup(string system, string ancestor, string descendant, EvalBudget budget)
    {
        foreach (var answer in _answers)
        {
            budget.Charge();
            if (Equal(answer.System, system, budget) && Equal(answer.Ancestor, ancestor, budget)
                && Equal(answer.Descendant, descendant, budget)) return answer.Subsumes;
        }
        throw new RuleEvalException(RuleError.Of(RuleEngineCodes.CodingAnswerMissing));
    }

    internal static bool Equal(string left, string right, EvalBudget budget)
    {
        budget.ChargeSize((long)left.Length + right.Length + 1);
        return StringComparer.Ordinal.Equals(left, right);
    }

    private static int Compare(Answer left, Answer right)
    {
        var result = StringComparer.Ordinal.Compare(left.System, right.System);
        if (result == 0) result = StringComparer.Ordinal.Compare(left.Ancestor, right.Ancestor);
        return result == 0 ? StringComparer.Ordinal.Compare(left.Descendant, right.Descendant) : result;
    }

    private static string[][] Bindings(JsonNode? node)
    {
        if (node is not JsonArray { Count: > 0 } rows) throw Invalid();
        var result = new List<string[]>();
        foreach (var item in rows)
        {
            var row = Tuple(item, 4).Select(Text).ToArray();
            var id = row[1].Split('.'); var version = row[2].Split('.');
            if (id.Length != 3 || id.Any(string.IsNullOrEmpty) || version.Length != 3
                || version.Any(part => !uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                || row[3].Length != 64 || row[3].Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                || result.Count > 0 && StringComparer.Ordinal.Compare(result[^1][0], row[0]) >= 0) throw Invalid();
            result.Add(row);
        }
        return result.ToArray();
    }

    private static JsonArray Tuple(JsonNode? node, int count)
        => node is JsonArray array && array.Count == count ? array : throw Invalid();
    private static string Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text) ? text : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid pinned Taxonomy coding capture.");
}
