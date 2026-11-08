using Harborline.Foundation.RuleEngine.Environments;

namespace Harborline.Blocks.Scheduling.Definitions;

/// <summary>
/// Scheduling's expression environment declaration (DES-0022 <c>scheduling-ck-23</c>, ADR 0099 decision 8): the
/// borrower's own row, stated in Rules' shared <see cref="BorrowerEnvironmentDeclaration"/> and admitted by Rules. A
/// constraint expression reads a candidate assignment or a resource, chain or occurrence fact and the planning clock,
/// in the Run phase only, with field reads as its only effect (<c>scheduling-auth-14</c>). Its functions are Rules' kernel
/// functions, without table aggregation (<c>scheduling-bound-5</c>). It is evaluated outside the solver's inner loop, which stays local arithmetic
/// and calls no expression language (<c>scheduling-eng-11</c>); attaching an expression to a constraint is a later slice.
/// </summary>
public static class SchedulingExpressionEnvironment
{
    /// <summary>The declaration as Scheduling states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "scheduling-ck-23",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string>
        {
            ["clock"] = "planning clock",
            ["field"] = "candidate assignment or resource/chain/occurrence fact",
        },
        // Rules' kernel functions as released for this edge; table aggregation (agg) is not part of a constraint expression.
        Operations:
        [
            "var", "missing", "missing_some", "==", "!=", "===", "!==", "!", "!!", "and", "or", "if", ">", ">=", "<", "<=",
            "+", "-", "*", "/", "%", "min", "max", "in", "cat", "money.add", "money.sub", "money.mul", "date.add", "date.diff",
            "date.today", "coding.is",
        ],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "planning-clock",
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

    /// <summary>The admitted environment every constraint expression evaluation presents.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);
}
