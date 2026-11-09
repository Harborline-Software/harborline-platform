using System.Text.Json.Nodes;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Contracts.Rules;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Blocks.Scheduling.Tests;

/// <summary>
/// T-491/expression-environment: Scheduling declares its constraint-expression environment through Rules' shared
/// borrower type. The oracle is Rules' released borrower fixture (<c>conformance/hlp.foundation.rule-runtime/borrower-contracts.json</c>),
/// which this slice does not change: its canonical form, admitted and inapplicable phases, and admitted and refused cases.
/// </summary>
public sealed class SchedulingExpressionEnvironmentTests
{
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "borrower-contracts.json")))!["borrowers"]!
        .AsArray().Single(entry => entry!["declaration"]?["borrower"]?.GetValue<string>() == "scheduling-ck-23")!;

    private static Validity Evaluate(JsonNode expression, JsonObject context, EvaluationPhase phase) =>
        new GuardEvaluator(TimeProvider.System).EvaluateGuard(
            new RuleDefinition
            {
                Id = "scheduling.constraint.fixture",
                Tier = RuleTier.JsonLogic,
                Scope = RuleScope.Schema,
                ScopeTarget = "constraint",
                Expression = expression.ToJsonString(),
                Action = RuleActionKind.Validate,
            },
            RuleContextSnapshot.Capture(context.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone())),
            RuleEvalScope.Root,
            SchedulingExpressionEnvironment.Admitted.For(phase));

    [Fact(DisplayName = "scheduling-ck-23: the typed declaration matches Rules' released borrower fixture, canonical form included")]
    public void Scheduling_ck_23_declaration_matches_the_released_borrower_contract()
    {
        Assert.Equal(Fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(SchedulingExpressionEnvironment.Declaration));
        Assert.Same(SchedulingExpressionEnvironment.Declaration, SchedulingExpressionEnvironment.Admitted.Declaration);
    }

    [Fact(DisplayName = "scheduling-auth-14: a constraint expression is admitted in the Run phase and refused in every other phase")]
    public void Scheduling_auth_14_constraint_expression_runs_only_in_the_run_phase()
    {
        Assert.Equal("Run", Fixture["admittedPhase"]!.GetValue<string>());
        Assert.True(Evaluate(Fixture["admitted"]!["expression"]!, Fixture["admitted"]!["context"]!.AsObject(), EvaluationPhase.Run).Ok);
        foreach (var phase in Enum.GetValues<EvaluationPhase>().Where(phase => phase != EvaluationPhase.Run))
        {
            Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted,
                Assert.Throws<BorrowerEnvironmentException>(() => SchedulingExpressionEnvironment.Admitted.For(phase)).Code);
        }

        Assert.Equal("Render", Fixture["inapplicablePhase"]!.GetValue<string>());
    }

    [Fact(DisplayName = "scheduling-bound-5: constraint expressions use Rules' kernel functions; an undeclared operation is refused with the fixture's code")]
    public void Scheduling_bound_5_functions_are_the_kernel_vocabulary_and_others_refuse()
    {
        var refused = Evaluate(Fixture["refused"]!["expression"]!, new JsonObject(), EvaluationPhase.Run);

        Assert.False(refused.Ok);
        Assert.Equal(Fixture["refused"]!["code"]!.GetValue<string>(), refused.Error?.Code);
        Assert.Equal(Fixture["declaration"]!["operations"]!.AsArray().Select(node => node!.GetValue<string>()), SchedulingExpressionEnvironment.Declaration.Operations);
    }

    [Fact(DisplayName = "scheduling-ck-23: a declaration that widens its effects is refused by Rules' admission")]
    public void Scheduling_ck_23_widened_declaration_is_refused() =>
        Assert.Equal(BorrowerEnvironmentAdmission.DeclarationRefused, Assert.Throws<BorrowerEnvironmentException>(
            () => BorrowerEnvironmentAdmission.Admit(SchedulingExpressionEnvironment.Declaration with
            {
                Effects = [BorrowerEnvironmentAdmission.FieldRead, "network"],
            })).Code);
}
