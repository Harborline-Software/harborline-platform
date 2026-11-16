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
/// Refusal codes are pinned as literal wire codes, never read from Rules' constants (T-724 ruling 121).
/// </summary>
public sealed class WorkflowsExpressionEnvironmentTests
{
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "borrower-contracts.json")))!["borrowers"]!
        .AsArray().Single(entry => entry!["declaration"]?["borrower"]?.GetValue<string>() == "workflows-ck-29")!;

    private const string NotAdmitted = "rule.environment.not_admitted";
    private const string PhaseNotAdmitted = "rule.environment.phase_not_admitted";
    private const string DeclarationRefused = "rule.environment.declaration_refused";

    /// <summary>Counts clock reads: the evaluator reads the evaluated-at instant once on entry, before any context value.</summary>
    private sealed class CountingClock : TimeProvider
    {
        public int Reads;

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return base.GetUtcNow();
        }

        public override long GetTimestamp()
        {
            Reads++;
            return base.GetTimestamp();
        }
    }

    private static Harborline.Foundation.RuleEngine.Model.Validity Evaluate(JsonNode expression, JsonObject context, EvaluationPhase phase, TimeProvider? clock = null) =>
        WorkflowsExpressionEnvironment.EvaluateGuard(
            new GuardEvaluator(clock ?? TimeProvider.System),
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
            Assert.Equal(PhaseNotAdmitted,
                Assert.Throws<BorrowerEnvironmentException>(() => WorkflowsExpressionEnvironment.Admitted.For(phase)).Code);
        }
    }

    [Fact(DisplayName = "workflows-ck-29: a declaration that widens its effects or leaves a phase unmarked is refused by Rules' admission")]
    public void Workflows_ck_29_widened_or_unmarked_declaration_is_refused()
    {
        Assert.Equal(DeclarationRefused, Assert.Throws<BorrowerEnvironmentException>(
            () => BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with
            {
                Effects = [BorrowerEnvironmentAdmission.FieldRead, "network"],
            })).Code);
        Assert.Equal(DeclarationRefused, Assert.Throws<BorrowerEnvironmentException>(
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

    [Theory(DisplayName = "workflows-ck-29: only a JSON true verdict passes")]
    [InlineData("\"false\"")]
    [InlineData("[false]")]
    [InlineData("{\"+\":[1,1]}")]
    [InlineData("{\"==\":[1,2],\"note\":\"x\"}")]
    [InlineData("{\"table.sum\":[\"a\"],\"agg\":[\"sum\",\"s\",\"c\"]}")]
    public void Workflows_ck_29_non_boolean_verdict_is_refused(string expression)
    {
        Assert.False(Evaluate(JsonNode.Parse(expression)!, new JsonObject(), EvaluationPhase.Run).Ok);
    }

    [Theory(DisplayName = "workflows-ck-29: malformed objects in guards are invalid expressions")]
    [InlineData("{\"and\":[{\"==\":[1,2],\"note\":\"x\"},true]}")]
    [InlineData("{\"!!\":[{\"table.sum\":[\"a\"],\"agg\":[\"sum\",\"s\",\"c\"]}]}")]
    [InlineData("{\"==\":[1,1],\"==\":[1,1]}")]
    [InlineData("{\"and\":[[{}],true]}")]
    public void Workflows_ck_29_malformed_objects_are_invalid_expressions(string expression)
    {
        var result = WorkflowsExpressionEnvironment.EvaluateGuard(new GuardEvaluator(TimeProvider.System), "test", expression,
            RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?>()), EvaluationPhase.Run);
        Assert.False(result.Ok);
        Assert.Equal("rule.compile.invalid_expression", result.Error?.Code);
    }

    [Fact(DisplayName = "workflows-ck-29: declaration dictionaries cannot be mutated")]
    public void Workflows_ck_29_declaration_dictionaries_are_read_only()
    {
        var phases = (IDictionary<EvaluationPhase, bool>)WorkflowsExpressionEnvironment.Declaration.Phases;
        var variables = (IDictionary<string, string>)WorkflowsExpressionEnvironment.Declaration.Variables;
        Assert.Throws<NotSupportedException>(() => phases[EvaluationPhase.Render] = true);
        Assert.Throws<NotSupportedException>(() => variables["candidate"] = "secret");
        Assert.False(BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration).Declaration.Phases[EvaluationPhase.Render]);
    }

    [Fact(DisplayName = "workflows-ck-29: guard evaluation in an inapplicable phase is refused before any value is read")]
    public void Workflows_ck_29_guard_evaluation_refuses_an_inapplicable_phase()
    {
        Assert.Equal("Render", Fixture["inapplicablePhase"]!.GetValue<string>());
        var expression = Fixture["admitted"]!["expression"]!;
        var context = Fixture["admitted"]!["context"]!.AsObject();

        // Positive control: an admitted Run evaluation reads the clock, so a zero count below means the evaluator was not entered.
        var run = new CountingClock();
        Assert.True(Evaluate(expression, context, EvaluationPhase.Run, run).Ok);
        Assert.True(run.Reads > 0);

        var render = new CountingClock();
        Assert.Equal(PhaseNotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
            () => Evaluate(expression, context, EvaluationPhase.Render, render)).Code);
        Assert.Equal(0, render.Reads);
    }

    [Fact(DisplayName = "workflows-ck-29: admission requires the admitted Workflows environment and refuses missing or foreign evidence")]
    public void Workflows_ck_29_admission_requires_the_admitted_environment()
    {
        Assert.True(new WorkflowAdmissionValidator().Validate(Approval()).IsValid);
        Assert.True(ValidatorWith(WorkflowsExpressionEnvironment.Admitted).Validate(Approval()).IsValid);

        var foreign = BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with { Borrower = "scheduling-ck-23" });
        foreach (var evidence in new[] { null, foreign })
        {
            Assert.Equal(NotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
                () => ValidatorWith(evidence).Validate(Approval())).Code);
            Assert.Equal(NotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
                () => ValidatorWith(evidence).EnsureAdmissible(Approval())).Code);
        }
    }

    [Fact(DisplayName = "workflows-ck-29: admission refuses evidence that keeps the workflows-ck-29 borrower id but admits a different declaration")]
    public void Workflows_ck_29_admission_refuses_same_borrower_evidence_with_changed_contents()
    {
        var renderEnabled = WorkflowsExpressionEnvironment.Declaration.Phases.ToDictionary();
        renderEnabled[EvaluationPhase.Render] = true;
        var altered = new[]
        {
            BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with { Phases = renderEnabled }),
            BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with
            {
                Operations = [.. WorkflowsExpressionEnvironment.Declaration.Operations.Where(op => op != "date.today")],
            }),
            BorrowerEnvironmentAdmission.Admit(WorkflowsExpressionEnvironment.Declaration with
            {
                Variables = WorkflowsExpressionEnvironment.Declaration.Variables.Where(v => v.Key != "timer").ToDictionary(),
            }),
        };

        foreach (var evidence in altered)
        {
            Assert.Equal("workflows-ck-29", evidence.Declaration.Borrower);
            Assert.NotEqual(Fixture["canonical"]!.GetValue<string>(), evidence.CanonicalJson);
            Assert.Equal(NotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
                () => ValidatorWith(evidence).Validate(Approval())).Code);
            Assert.Equal(NotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
                () => ValidatorWith(evidence).EnsureAdmissible(Approval())).Code);
        }
    }
}
