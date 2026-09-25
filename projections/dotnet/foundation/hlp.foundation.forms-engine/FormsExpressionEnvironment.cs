using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Functions;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>
/// Forms' expression environment declaration (DES-0016 <c>forms-ck-13</c>, ADR 0099 decision 8): the
/// borrower's own row, admitted by Rules. Render and submission evaluate; the other phases are marked.
/// </summary>
public static class FormsExpressionEnvironment
{
    /// <summary>The declaration as Forms states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "forms-ck-13",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string>
        {
            ["candidate"] = "form-submission-candidate",
            ["caller"] = "authenticated-principal",
            ["clock"] = "evaluated-at",
            ["record_type"] = "required-record-type-id",
            ["field"] = "record-type-schema-field",
            ["row"] = "record-type-schema-child-row",
        },
        Operations: [.. BuiltInFunctionRegister.Functions.Select(function => function.Key)],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "evaluated-at",
        TimeZone: "utc",
        Phases: new Dictionary<EvaluationPhase, bool>
        {
            [EvaluationPhase.AuthoringValidation] = false,
            [EvaluationPhase.PublishValidation] = false,
            [EvaluationPhase.Render] = true,
            [EvaluationPhase.Submission] = true,
            [EvaluationPhase.Run] = false,
            [EvaluationPhase.SignOff] = false,
        },
        Replay: "deterministic-over-pinned-clock-and-candidate");

    /// <summary>The admitted environment every Forms evaluation presents.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);
}
