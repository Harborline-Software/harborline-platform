using Harborline.Contracts.Rules;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Blocks.Workflow;

/// <summary>
/// Workflows' expression environment declaration (DES-0019 <c>workflows-ck-29</c>, ADR 0099 decision 8): the
/// borrower's own row, stated in Rules' shared <see cref="BorrowerEnvironmentDeclaration"/> and admitted by Rules.
/// The typed subject field, run identity, run timer, principal and evaluated-at clock; guard evaluation is field-read
/// only and effects stay registered actions outside expressions. Guards evaluate only while a run executes, and every
/// other phase is marked inapplicable: there is no ambient default.
/// </summary>
public static class WorkflowsExpressionEnvironment
{
    /// <summary>The declaration as Workflows states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "workflows-ck-29",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string>
        {
            ["caller"] = "principal",
            ["clock"] = "evaluated-at",
            ["field"] = "typed record/set/case subject field",
            ["timer"] = "run timer",
            ["wf"] = "run identity",
        },
        Operations:
        [
            "var", "missing", "missing_some", "==", "!=", "===", "!==", "!", "!!", "and", "or", "if",
            ">", ">=", "<", "<=", "+", "-", "*", "/", "%", "min", "max", "in", "cat",
            "money.add", "money.sub", "money.mul", "date.add", "date.diff", "date.today", "coding.is",
        ],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "evaluated-at",
        TimeZone: "utc",
        Phases: new Dictionary<EvaluationPhase, bool>
        {
            [EvaluationPhase.AuthoringValidation] = false,
            [EvaluationPhase.PublishValidation] = false,
            [EvaluationPhase.Render] = false,
            [EvaluationPhase.Submission] = false,
            [EvaluationPhase.Run] = true,
            [EvaluationPhase.SignOff] = false,
        },
        Replay: "deterministic");

    /// <summary>The admitted environment workflow admission and every guard evaluation present.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);

    private static readonly string Canonical = BorrowerEnvironmentAdmission.CanonicalJson(Declaration);

    /// <summary>Refuses admission evidence that is missing or is not this declaration's.</summary>
    /// <exception cref="BorrowerEnvironmentException"><c>rule.environment.not_admitted</c>.</exception>
    internal static void Require(AdmittedEnvironment? environment)
    {
        if (environment is null || environment.CanonicalJson != Canonical)
            throw new BorrowerEnvironmentException(BorrowerEnvironmentAdmission.NotAdmitted,
                "The Workflows expression environment is not admitted.");
    }

    /// <summary>
    /// Evaluates one workflow guard expression through the shared engine, presenting this environment's admission for
    /// <paramref name="phase"/>. Fail-closed: anything but a true verdict is invalid.
    /// </summary>
    /// <exception cref="BorrowerEnvironmentException">The phase is one the declaration marks inapplicable.</exception>
    public static Validity EvaluateGuard(GuardEvaluator evaluator, string guardId, string expression,
        RuleContextSnapshot context, EvaluationPhase phase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        var admission = Admitted.For(phase);
        var rule = new RuleDefinition
        {
            Id = $"workflow.guard.{guardId}",
            Tier = RuleTier.JsonLogic,
            Scope = RuleScope.Schema,
            ScopeTarget = guardId,
            Expression = expression,
            Action = RuleActionKind.Validate,
        };
        return evaluator.EvaluateGuard(rule, context, RuleEvalScope.Root, admission, cancellationToken);
    }
}
