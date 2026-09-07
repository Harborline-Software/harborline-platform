using System.Text.RegularExpressions;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// D1 ratification fix 3 (2026-07-01): the <b>operator-catalog erosion guard</b>. A STANDING
/// arch-test that pins the CLOSED <c>harborline-jsonlogic/v1</c> operator set + the regex-exclusion
/// invariant, so a new operator (or a regex/pattern construct) cannot slip into the evaluator at a
/// version bump without tripping this test — the "one convenience operator erodes A into B, one PR
/// at a time" (Form.io) trajectory the D1 council named. Adding/removing/renaming an operator
/// REQUIRES a deliberate edit to <see cref="FrozenV1Operators"/> below — which is exactly the review
/// checkpoint the guard exists to force. It source-scans the evaluator so it is author-independent
/// (it cannot be defeated by forgetting to update a parallel catalog constant).
/// </summary>
public sealed class OperatorCatalogArchTests
{
    /// <summary>
    /// The frozen, closed <c>harborline-jsonlogic/v1</c> operator set (design §1.4 / README). DELIBERATELY
    /// EXCLUDED: any regex/pattern operator (ReDoS structurally absent — Decision DG); and
    /// <c>map</c>/<c>filter</c>/<c>reduce</c>/<c>merge</c> (deferred — child-table folds use <c>agg</c>).
    /// The TS tier pins the IDENTICAL set in <c>operator-catalog.test.ts</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> FrozenV1Operators = new HashSet<string>(StringComparer.Ordinal)
    {
        "var", "missing", "missing_some",
        "==", "!=", "===", "!==", "!", "!!", "and", "or", "if",
        ">", ">=", "<", "<=",
        "+", "-", "*", "/", "%", "min", "max",
        "in", "cat",
        "agg", "money.add", "money.sub", "money.mul",
        "date.add", "date.diff", "date.today", "coding.is",
    };

    [Fact]
    public void Evaluator_implements_EXACTLY_the_frozen_v1_operator_set()
    {
        string source = ReadEvaluatorSource();
        int switchAt = source.IndexOf("return op switch", StringComparison.Ordinal);
        Assert.True(switchAt >= 0, "could not locate the `return op switch` operator dispatch in HarborlineJsonLogic.cs");
        string switchBody = source[switchAt..];

        // Every operator arm is a double-quoted string literal immediately before `=>`. The default arm
        // is `_ =>` (no literal), and the arm expressions carry no `"literal" =>`, so this captures the
        // operator KEYS exactly. (Char-literal switches elsewhere use single quotes.)
        var implemented = Regex.Matches(switchBody, "\"([^\"\\\\]+)\"\\s*=>")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(implemented.Count > 0, "extracted no operator arms — the scan regex or the switch shape changed");

        var added = implemented.Except(FrozenV1Operators).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var removed = FrozenV1Operators.Except(implemented).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.True(added.Count == 0,
            $"NEW operator(s) added to the evaluator without deliberate review: [{string.Join(", ", added)}]. "
            + "The harborline-jsonlogic/v1 set is CLOSED (D1). If intended, amend FrozenV1Operators + the TS "
            + "operator-catalog test + the README operator table + design §1.4 — never silently.");
        Assert.True(removed.Count == 0,
            $"operator(s) removed from the evaluator: [{string.Join(", ", removed)}]. Removing an operator "
            + "breaks the closed v1 set — amend FrozenV1Operators deliberately if this is intended.");
    }

    [Fact]
    public void Evaluator_uses_NO_regex_construct_so_ReDoS_is_structurally_absent()
    {
        string source = ReadEvaluatorSource();
        foreach (var banned in new[] { "Regex", "RegularExpressions", "GeneratedRegex" })
        {
            Assert.False(source.Contains(banned, StringComparison.Ordinal),
                $"the Tier-2 evaluator must not use '{banned}': regex/pattern is excluded from v1 (Decision DG) "
                + "so the ReDoS class is structurally absent. Pattern validation stays in Tier-1 JSON-Schema.");
        }
    }

    [Fact]
    public void No_frozen_operator_names_a_regex_or_pattern_construct()
    {
        foreach (var op in FrozenV1Operators)
        {
            foreach (var banned in new[] { "regex", "pattern", "match", "search" })
            {
                Assert.False(op.Contains(banned, StringComparison.OrdinalIgnoreCase),
                    $"operator '{op}' names a regex/pattern construct — excluded from v1 (Decision DG).");
            }
        }
    }

    private static string ReadEvaluatorSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        Assert.True(dir is not null, "could not locate the Harborline Platform repository root to read the evaluator source");
        string path = Path.Combine(dir!.FullName, "projections", "dotnet", "foundation", "hlp.foundation.rule-runtime", "Evaluation", "HarborlineJsonLogic.cs");
        Assert.True(File.Exists(path), $"evaluator source not found at {path}");
        return File.ReadAllText(path);
    }
}
