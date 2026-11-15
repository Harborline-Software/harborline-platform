using System.Text.Json.Nodes;

using Harborline.Blocks.Workflow.Durable;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// T-488/expression-environment: Workflows declares its guard-expression environment (DES-0019 <c>workflows-ck-29</c>)
/// through Rules' shared borrower type, and admission and guard evaluation require it. The oracle is Rules' released
/// borrower fixture (<c>conformance/hlp.foundation.rule-runtime/borrower-contracts.json</c>), which this slice does not
/// change and production does not read: its canonical form, admitted and inapplicable phases, and admitted and refused cases.
/// </summary>
public sealed class WorkflowsExpressionEnvironmentTests
{
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "borrower-contracts.json")))!["borrowers"]!
        .AsArray().Single(entry => entry!["declaration"]?["borrower"]?.GetValue<string>() == "workflows-ck-29")!;

    private static Harborline.Foundation.RuleEngine.Model.Validity Evaluate(JsonNode expression, JsonObject context, EvaluationPhase phase) =>
        WorkflowsExpressionEnvironment.EvaluateGuard(
            new GuardEvaluator(TimeProvider.System),
            "fixture",
            expression.ToJsonString(),
            RuleContextSnapshot.Capture(context.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone())),
            phase);

    private static WorkflowDefinition Approval() => new()
    {
        Key = "approval",
        Version = "1.0.0",
        Tenant = "tenant:acme",
        InitialState = "Draft",
        Mutability = WorkflowMutability.Locked,
        States =
        [
            new() { Id = "Draft", Kind = WorkflowStateKind.Normal },
            new() { Id = "Approved", Kind = WorkflowStateKind.Terminal },
        ],
        Triggers = [new() { Id = "approve", Kind = WorkflowTriggerKind.HumanAction }],
        Transitions = [new() { Id = "t-approve", From = "Draft", On = "approve", To = "Approved" }],
    };

    private static WorkflowAdmissionValidator ValidatorWith(AdmittedEnvironment? environment) =>
        new(CapabilityAuthorityRegistry.Canonical, null, null, null, environment);

    [Fact(DisplayName = "workflows-ck-29: the typed declaration states every member of the released borrower fixture, canonical form included")]
    public void Workflows_ck_29_declaration_matches_the_released_borrower_contract()
    {
        Assert.Equal(Fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(WorkflowsExpressionEnvironment.Declaration));
        Assert.Equal(Fixture["canonical"]!.GetValue<string>(), WorkflowsExpressionEnvironment.Admitted.CanonicalJson);
    }

    [Fact(DisplayName = "workflows-ck-29: Run is the one applicable phase and every other phase is marked inapplicable and refused")]
    public void Workflows_ck_29_marks_every_inapplicable_phase()
    {
        Assert.Equal("Run", Fixture["admittedPhase"]!.GetValue<string>());
        Assert.Equal(6, WorkflowsExpressionEnvironment.Declaration.Phases.Count);
        Assert.True(WorkflowsExpressionEnvironment.Admitted.For(EvaluationPhase.Run).Phase == EvaluationPhase.Run);
        foreach (var phase in new[] { EvaluationPhase.AuthoringValidation, EvaluationPhase.PublishValidation, EvaluationPhase.Render, EvaluationPhase.Submission, EvaluationPhase.SignOff })
        {
            Assert.False(WorkflowsExpressionEnvironment.Declaration.Phases[phase]);
            Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted,
                Assert.Throws<BorrowerEnvironmentException>(() => WorkflowsExpressionEnvironment.Admitted.For(phase)).Code);
        }
    }

    [Fact(DisplayName = "workflows-ck-29: a declaration that widens its effects or leaves a phase unmarked is refused by Rules' admission")]
    public void Workflows_ck_29_widened_or_unmarked_declaration_is_refused()
    {
        Assert.Equal(BorrowerEnvironmentAdmission.DeclarationRefused, Assert.Throws<BorrowerEnvironmentException>(
            () => BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with
            {
                Effects = [BorrowerEnvironmentAdmission.FieldRead, "network"],
            })).Code);
        Assert.Equal(BorrowerEnvironmentAdmission.DeclarationRefused, Assert.Throws<BorrowerEnvironmentException>(
            () => BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with
            {
                Phases = new Dictionary<EvaluationPhase, bool> { [EvaluationPhase.Run] = true },
            })).Code);
    }

    [Fact(DisplayName = "workflows-ck-29: guard evaluation presents the admitted environment, admits the fixture's Run case and refuses its undeclared operation")]
    public void Workflows_ck_29_guard_evaluation_requires_the_admitted_environment()
    {
        Assert.True(Evaluate(Fixture["admitted"]!["expression"]!, Fixture["admitted"]!["context"]!.AsObject(), EvaluationPhase.Run).Ok);

        var refused = Evaluate(Fixture["refused"]!["expression"]!, new JsonObject(), EvaluationPhase.Run);
        Assert.False(refused.Ok);
        Assert.Equal(Fixture["refused"]!["code"]!.GetValue<string>(), refused.Error?.Code);
    }

    [Fact(DisplayName = "workflows-ck-29: guard evaluation in an inapplicable phase is refused before any value is read")]
    public void Workflows_ck_29_guard_evaluation_refuses_an_inapplicable_phase()
    {
        Assert.Equal("Render", Fixture["inapplicablePhase"]!.GetValue<string>());
        Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
            () => Evaluate(Fixture["admitted"]!["expression"]!, Fixture["admitted"]!["context"]!.AsObject(), EvaluationPhase.Render)).Code);
    }

    [Fact(DisplayName = "workflows-ck-29: admission requires the admitted Workflows environment and refuses missing or foreign evidence")]
    public void Workflows_ck_29_admission_requires_the_admitted_environment()
    {
        Assert.True(new WorkflowAdmissionValidator().Validate(Approval()).IsValid);
        Assert.True(ValidatorWith(WorkflowsExpressionEnvironment.Admitted).Validate(Approval()).IsValid);

        var foreign = BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with { Borrower = "scheduling-ck-23" });
        foreach (var evidence in new[] { null, foreign })
        {
            Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
                () => ValidatorWith(evidence).Validate(Approval())).Code);
            Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
                () => ValidatorWith(evidence).EnsureAdmissible(Approval())).Code);
        }
    }
}
