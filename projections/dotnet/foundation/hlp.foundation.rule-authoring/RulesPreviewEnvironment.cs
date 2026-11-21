using Harborline.Foundation.RuleEngine.Environments;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>
/// Rules preview (rules-auth-11) evaluates authored rules over sample records while authoring. Its expression environment declaration (DES-0018 <c>rules-ck-28</c>, ADR 0099 decision 8) is this
/// borrower's own row; Rules admits it and every evaluation here presents the admission.
/// </summary>
public static class RulesPreviewEnvironment
{
    /// <summary>The declaration as this borrower states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "rules-auth-11",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string> { ["field"] = "sample record field", ["row"] = "sample record child row" },
        Operations: ["var", "missing", "missing_some", "==", "!=", "===", "!==", "!", "!!", "and", "or", "if", ">", ">=", "<", "<=", "+", "-", "*", "/", "%", "min", "max", "in", "cat", "agg", "money.add", "money.sub", "money.mul", "date.add", "date.diff", "date.today", "coding.is"],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "pinned-preview-clock",
        TimeZone: "utc",
        Phases: Enum.GetValues<EvaluationPhase>().ToDictionary(phase => phase, phase => phase is EvaluationPhase.AuthoringValidation),
        Replay: "deterministic-over-pinned-clock-and-snapshot");

    /// <summary>The admitted environment every evaluation here presents.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);
}
