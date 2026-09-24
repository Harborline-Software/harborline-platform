using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-590 slice 2: borrower environment admission at every evaluation entry point.</summary>
public sealed class BorrowerEnvironmentTests
{
    private static readonly DateTimeOffset Clock = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    private static RuleDefinition Rule(string id, string target, string expr, RuleActionKind action = RuleActionKind.Compute, RuleScope scope = RuleScope.Field)
        => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, scope, target, expr, action);

    private static EvaluationAdmission Narrow(IReadOnlyList<string>? operations = null, IReadOnlyList<string>? variables = null)
        => BorrowerEnvironmentAdmission.Admit(TestAdmission.Declaration(operations, variables)).For(EvaluationPhase.Run);

    private static string? Code(RuleEvaluationResult result) => result.Validations.SingleOrDefault(o => o.RuleId == "rule.engine")?.Validity?.Error?.Code;

    [Fact(DisplayName = "rules-ck-28: a typed borrower declaration is admitted; one missing a member, borrowing another grammar, asking for an effect, naming an unregistered function or leaving a phase unmarked refuses")]
    public void Declarations_are_admitted_or_refused_by_member()
    {
        var good = TestAdmission.Declaration();
        Assert.Same(good, BorrowerEnvironmentAdmission.Admit(good).Declaration);

        BorrowerEnvironmentDeclaration[] refused =
        [
            good with { Borrower = " " },
            good with { Grammar = "harborline-jsonlogic/v2" },
            good with { Variables = new Dictionary<string, string>() },
            good with { Operations = ["cat", "http.get"] },
            good with { Effects = [BorrowerEnvironmentAdmission.FieldRead, "network"] },
            good with { MissingValues = "" },
            good with { TimeZone = "" },
            good with { Phases = new Dictionary<EvaluationPhase, bool> { [EvaluationPhase.Run] = true } },
            good with { Replay = "" },
        ];
        foreach (var declaration in refused)
            Assert.Equal(BorrowerEnvironmentAdmission.DeclarationRefused,
                Assert.Throws<BorrowerEnvironmentException>(() => BorrowerEnvironmentAdmission.Admit(declaration)).Code);

        // Only admission constructs the evidence an entry point requires.
        Assert.Empty(typeof(AdmittedEnvironment).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(EvaluationAdmission).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact(DisplayName = "rules-eng-26: a phase the declaration marks inapplicable cannot be presented")]
    public void Inapplicable_phase_refuses()
    {
        var environment = BorrowerEnvironmentAdmission.Admit(TestAdmission.Declaration(phases: [EvaluationPhase.Render]));
        Assert.Equal(EvaluationPhase.Render, environment.For(EvaluationPhase.Render).Phase);
        Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted,
            Assert.Throws<BorrowerEnvironmentException>(() => environment.For(EvaluationPhase.Submission)).Code);
    }

    [Fact(DisplayName = "rules-eng-26: guard and value overloads evaluate under an admitted declaration and refuse the unadmitted counterpart")]
    public void Guard_and_value_overloads_require_admission()
    {
        var guard = new GuardEvaluator(new FixedClock(Clock));
        var rule = Rule("g", "ok", """{">":[{"var":"amount"},10]}""", RuleActionKind.Validate);
        var value = Rule("v", "out", """{"cat":["a",{"var":"amount"}]}""");
        var context = RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?> { ["amount"] = 20 });

        Assert.True(guard.EvaluateGuard(rule, context, RuleEvalScope.Root, TestAdmission.Any).Ok);
        Assert.Equal("a20", guard.EvaluateValue(value, context, RuleEvalScope.Root, TestAdmission.Any).Value!.GetValue<string>());

        Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, guard.EvaluateGuard(rule, context, RuleEvalScope.Root, null).Error!.Code);
        Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, guard.EvaluateValue(value, context, RuleEvalScope.Root, null).Error!.Code);

        // An admitted environment that does not lend `cat` (or `>`) refuses the program that uses it.
        var withoutCat = Narrow(operations: ["var", ">"]);
        Assert.True(guard.EvaluateGuard(rule, context, RuleEvalScope.Root, withoutCat).Ok);
        Assert.Equal(BorrowerEnvironmentAdmission.OperationNotAdmitted, guard.EvaluateValue(value, context, RuleEvalScope.Root, withoutCat).Error!.Code);
        Assert.Equal(BorrowerEnvironmentAdmission.OperationNotAdmitted, guard.EvaluateGuard(rule, context, RuleEvalScope.Root, Narrow(operations: ["var"])).Error!.Code);
    }

    [Fact(DisplayName = "rules-eng-26: full graph evaluation evaluates under an admitted declaration and refuses the unadmitted counterpart")]
    public void Full_graph_requires_admission()
    {
        var compiled = RuleCompiler.Compile([Rule("t", "total", """{"+":[{"var":"a"},1]}""")]);
        var instance = RuleInstance.FromJson(JsonNode.Parse("""{"a":2}""")!.AsObject());

        Assert.Equal(3L, new FormRuleGraph(compiled, new FixedClock(Clock), TestAdmission.Any).EvaluateInstance(instance).Values["field:total"].Value!.GetValue<long>());

        var refused = new FormRuleGraph(compiled, new FixedClock(Clock), null).EvaluateInstance(instance);
        Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, Code(refused));
        Assert.False(refused.Values.ContainsKey("field:total"));
        Assert.Equal(BorrowerEnvironmentAdmission.OperationNotAdmitted,
            Code(new FormRuleGraph(compiled, new FixedClock(Clock), Narrow(operations: ["var"])).EvaluateInstance(instance)));
    }

    [Fact(DisplayName = "rules-eng-26: incremental graph evaluation (field change, row add, row remove) evaluates under admission and refuses the unadmitted counterpart")]
    public void Incremental_graph_requires_admission()
    {
        var compiled = RuleCompiler.Compile(
        [
            Rule("t", "total", """{"var":"table.sum(items.amount)"}"""),
            Rule("d", "double", """{"*":[{"var":"a"},2]}"""),
        ]);
        var row = new RuleRow("r1", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(5) });

        var admitted = new FormRuleGraph(compiled, new FixedClock(Clock), TestAdmission.Any);
        admitted.EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse("""{"a":1}""")!.AsObject()));
        Assert.Equal(6L, admitted.Reevaluate("a", RuleInputValue.FromJsonText("3")).Values["field:double"].Value!.GetValue<long>());
        Assert.Equal(5L, admitted.AddRow("items", row).Values["field:total"].Value!.GetValue<long>());
        Assert.Equal(0L, admitted.RemoveRow("items", "r1").Values["field:total"].Value!.GetValue<long>());

        foreach (var admission in new[] { null, Narrow(variables: ["field"]) })
        {
            var refused = new FormRuleGraph(compiled, new FixedClock(Clock), admission);
            var code = admission is null ? BorrowerEnvironmentAdmission.NotAdmitted : BorrowerEnvironmentAdmission.VariableNotAdmitted;
            Assert.Equal(code, Code(refused.EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse("""{"a":1}""")!.AsObject()))));
            Assert.Equal(code, Code(refused.Reevaluate("a", RuleInputValue.FromJsonText("3"))));
            Assert.Equal(code, Code(refused.Reevaluate("a", JsonValue.Create(3))));
            Assert.Equal(code, Code(refused.AddRow("items", row)));
            Assert.Equal(code, Code(refused.RemoveRow("items", "r1")));
        }
    }

    /// <summary>
    /// Every production call site that reaches an evaluation entry point, by file. A new file that calls one
    /// fails here until it is inventoried with its borrower. The raw seams below the entry points are internal.
    /// </summary>
    private static readonly string[] InventoriedCallSites =
    [
        "projections/dotnet/blocks/hlp.blocks.layout-runtime/LayoutBindingResolution.cs",
        "projections/dotnet/foundation/hlp.foundation.actor/AccessScope.cs",
        "projections/dotnet/foundation/hlp.foundation.field-runtime/ValueDomainRuntime.cs",
        "projections/dotnet/foundation/hlp.foundation.forms-engine/FormCandidateEvaluator.cs",
        "projections/dotnet/foundation/hlp.foundation.forms-engine/FormEngine.cs",
        "projections/dotnet/foundation/hlp.foundation.rule-authoring/SkinLowering.cs",
    ];

    [Fact(DisplayName = "rules-eng-26: architecture fence — evaluation call sites are inventoried, each presents an admission, and the raw seams stay internal")]
    public void Evaluation_call_sites_are_inventoried()
    {
        var root = FunctionRegisterTests.RepositoryRoot();
        var entry = new Regex(@"new FormRuleGraph\(|\.EvaluateGuard\(|\.EvaluateValue\(", RegexOptions.CultureInvariant);
        var found = Directory.EnumerateFiles(Path.Combine(root, "projections", "dotnet"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "projections", "blazor"), "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal)
                && !path.Contains(".tests/", StringComparison.Ordinal) && !path.Contains("/hlp.foundation.rule-runtime/", StringComparison.Ordinal))
            .Where(path => entry.IsMatch(File.ReadAllText(Path.Combine(root, path))))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(InventoriedCallSites, found);

        foreach (var path in found)
        {
            var source = File.ReadAllText(Path.Combine(root, path));
            foreach (Match call in entry.Matches(source))
            {
                var statement = source[call.Index..source.IndexOf(';', call.Index)];
                Assert.True(statement.Contains(".For(EvaluationPhase.", StringComparison.Ordinal) || statement.Contains("admission", StringComparison.Ordinal),
                    $"{path}: an evaluation call does not present an admission: {statement}");
            }
        }

        var engine = typeof(GuardEvaluator).Assembly;
        foreach (var raw in new[] { "Harborline.Foundation.RuleEngine.Evaluation.HarborlineJsonLogic", "Harborline.Foundation.RuleEngine.RuleEvaluator", "Harborline.Foundation.RuleEngine.Evaluation.EvalContext" })
            Assert.False(engine.GetType(raw, throwOnError: true)!.IsPublic, $"{raw} must stay an internal seam");
    }
}
