using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Functions;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.Documents;

/// <summary>
/// Documents' expression environment declaration (DES-0021 <c>documents-ck-25</c>, ADR 0099 decision 8): the
/// borrower's own row, stated in Rules' shared <see cref="BorrowerEnvironmentDeclaration"/> and admitted by Rules.
/// Typed record and per-row scope, the issuance principal and the pinned issue clock; field reads only; every
/// phase marked. Guard and merge evaluation perform no storage or network effect.
/// </summary>
public static class DocumentsExpressionEnvironment
{
    /// <summary>The declaration as Documents states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "documents-ck-25",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string>
        {
            ["caller"] = "issuance-principal",
            ["clock"] = "pinned-issue-clock",
            ["field"] = "typed record field",
            ["row"] = "per-row scope",
        },
        Operations: [.. BuiltInFunctionRegister.Functions.Select(function => function.Key)],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "pinned-issue-clock",
        TimeZone: "utc",
        Phases: new Dictionary<EvaluationPhase, bool>
        {
            [EvaluationPhase.AuthoringValidation] = false,
            [EvaluationPhase.PublishValidation] = false,
            [EvaluationPhase.Render] = true,
            [EvaluationPhase.Submission] = false,
            [EvaluationPhase.Run] = false,
            [EvaluationPhase.SignOff] = true,
        },
        Replay: "deterministic");

    /// <summary>The admitted environment every document guard evaluation presents.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);

    /// <summary>
    /// Evaluates one inline document guard at the surface root through the shared engine, presenting this
    /// environment's admission for <paramref name="phase"/>. Fail-closed: anything but a true verdict is invalid.
    /// </summary>
    /// <exception cref="BorrowerEnvironmentException">The phase is one the declaration marks inapplicable.</exception>
    public static RuleEngine.Model.Validity EvaluateGuard(GuardEvaluator evaluator, string blockId, string expression,
        RuleContextSnapshot context, EvaluationPhase phase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        var admission = Admitted.For(phase);
        var rule = new RuleDefinition
        {
            Id = $"documents.guard.{blockId}",
            Tier = RuleTier.JsonLogic,
            Scope = RuleScope.Schema,
            ScopeTarget = blockId,
            Expression = expression,
            Action = RuleActionKind.Validate,
        };
        return evaluator.EvaluateGuard(rule, context, RuleEvalScope.Root, admission, cancellationToken);
    }
}
