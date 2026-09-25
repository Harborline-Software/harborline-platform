using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Functions;

namespace Harborline.Foundation.Authorization;

/// <summary>
/// Access scope guards borrow the Rules grammar over a flat fact bag, so child-table folds are not admitted. Its expression environment declaration (DES-0018 <c>rules-ck-28</c>, ADR 0099 decision 8) is this
/// borrower's own row; Rules admits it and every evaluation here presents the admission.
/// </summary>
public static class AccessExpressionEnvironment
{
    /// <summary>The declaration as this borrower states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "access-scope-guard",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string> { ["field"] = "access facts: principal, tenant, instant, record.id, record.kind and declared record.<field>" },
        Operations: [.. BuiltInFunctionRegister.Functions.Select(function => function.Key).Where(key => key != "agg")],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "request-instant",
        TimeZone: "utc",
        Phases: Enum.GetValues<EvaluationPhase>().ToDictionary(phase => phase, phase => phase is EvaluationPhase.Run),
        Replay: "deterministic-over-pinned-clock-and-snapshot");

    /// <summary>The admitted environment every evaluation here presents.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);
}
