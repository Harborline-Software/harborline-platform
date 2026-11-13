using Harborline.Foundation.RuleEngine.Environments;

namespace Harborline.Blocks.EntityViews;

/// <summary>The Views filter environment admitted by Rules (views-ck-14).</summary>
public static class ViewsExpressionEnvironment
{
    /// <summary>The declared grammar, variables, effects and phases.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "views-ck-14",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string> { ["field"] = "typed record field", ["caller"] = "authenticated-principal", ["clock"] = "evaluated-at" },
        Operations: ["var", "missing", "missing_some", "==", "!=", "===", "!==", "!", "!!", "and", "or", "if", ">", ">=", "<", "<=", "+", "-", "*", "/", "%", "min", "max", "in", "cat", "money.add", "money.sub", "money.mul", "date.add", "date.diff", "date.today", "coding.is"],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "evaluated-at",
        TimeZone: "utc",
        Phases: new Dictionary<EvaluationPhase, bool>
        {
            [EvaluationPhase.AuthoringValidation] = false,
            [EvaluationPhase.PublishValidation] = false,
            [EvaluationPhase.Render] = true,
            [EvaluationPhase.Submission] = false,
            [EvaluationPhase.Run] = true,
            [EvaluationPhase.SignOff] = false,
        },
        Replay: "deterministic");

    /// <summary>The environment admitted by Rules.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);
    private static readonly string Canonical = BorrowerEnvironmentAdmission.CanonicalJson(Declaration);

    internal static void Require(AdmittedEnvironment? environment)
    {
        if (environment is null || environment.CanonicalJson != Canonical)
            throw new BorrowerEnvironmentException(BorrowerEnvironmentAdmission.NotAdmitted, "The Views expression environment is not admitted.");
    }

    internal static void Require(EvaluationAdmission? admission)
    {
        Require(admission?.Environment);
        if (admission!.Phase is not (EvaluationPhase.Render or EvaluationPhase.Run))
            throw new BorrowerEnvironmentException(BorrowerEnvironmentAdmission.PhaseNotAdmitted, "The Views evaluation phase is not admitted.");
    }
}
