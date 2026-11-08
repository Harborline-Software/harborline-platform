using System.Text.RegularExpressions;
using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// T-491/expression-environment, DES-0022 <c>scheduling-eng-11</c>: the solver's inner loop stays local arithmetic and
/// calls no expression language. Constraint expressions are Rules' grammar evaluated outside it
/// (<c>scheduling-auth-14</c>), so no file of the planning solver may name Rules' evaluators, the expression environment
/// or JsonLogic.
/// </summary>
public sealed partial class SchedulingInnerLoopArchitectureTests
{
    [Fact(DisplayName = "scheduling-eng-11: the planning solver's inner loop calls no expression language")]
    public void Scheduling_eng_11_planning_solver_calls_no_expression_language()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        var planning = Path.Combine(root.FullName, "projections", "dotnet", "blocks", "hlp.blocks.scheduling", "Planning");
        var files = Directory.EnumerateFiles(planning, "*.cs", SearchOption.AllDirectories).ToArray();
        Assert.Contains(files, path => Path.GetFileName(path) == "PlanningSolvers.cs");
        var offenders = files.Where(path => CallsExpressionLanguage(File.ReadAllText(path))).ToArray();
        Assert.True(offenders.Length == 0, "The solver's inner loop must stay local arithmetic: " + string.Join(", ", offenders));
    }

    [Theory(DisplayName = "scheduling-eng-11: a planted expression call in the solver is found")]
    [InlineData("var ok = new GuardEvaluator(TimeProvider.System).EvaluateGuard(rule, context, RuleEvalScope.Root, admission);")]
    [InlineData("if (!SchedulingExpressionEnvironment.Admitted.For(EvaluationPhase.Run).Allows(candidate)) continue;")]
    [InlineData("using Harborline.Foundation.RuleEngine;")]
    [InlineData("var verdict = JsonLogic.Apply(expression, candidate);")]
    [InlineData("var snapshot = RuleContextSnapshot.Capture(candidateFields);")]
    public void Scheduling_eng_11_canary_finds_a_planted_call(string planted) => Assert.True(CallsExpressionLanguage(planted));

    [Fact(DisplayName = "scheduling-eng-11: local arithmetic in the inner loop stays permitted")]
    public void Scheduling_eng_11_local_arithmetic_is_permitted() => Assert.False(CallsExpressionLanguage(
        "var consistent = AssignmentRules.CanAdd(candidate, assignments, problem.Precedence); if (left.End > right.Start) return false;"));

    private static bool CallsExpressionLanguage(string text) => ExpressionLanguage().IsMatch(text);

    [GeneratedRegex(@"\bHarborline\.Foundation\.RuleEngine\b|\bGuardEvaluator\b|\bRuleEvaluator\b|\bEvaluateGuard\b|\bSchedulingExpressionEnvironment\b|\bBorrowerEnvironment\w*\b|\bJsonLogic\w*\b|\bRuleContextSnapshot\b")]
    private static partial Regex ExpressionLanguage();
}
