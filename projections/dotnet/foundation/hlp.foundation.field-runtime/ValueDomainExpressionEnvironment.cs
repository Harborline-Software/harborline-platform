using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Functions;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>
/// Value-domain predicates (rules-bound-7) filter a source member by its pinned fields, never a child collection. Its expression environment declaration (DES-0018 <c>rules-ck-28</c>, ADR 0099 decision 8) is this
/// borrower's own row; Rules admits it and every evaluation here presents the admission.
/// </summary>
public static class ValueDomainExpressionEnvironment
{
    /// <summary>The declaration as this borrower states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "value-domain-predicate",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string> { ["field"] = "pinned source member fields" },
        Operations: [.. BuiltInFunctionRegister.Functions.Select(function => function.Key).Where(key => key != "agg")],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "resolver-clock",
        TimeZone: "utc",
        Phases: Enum.GetValues<EvaluationPhase>().ToDictionary(phase => phase, phase => phase is EvaluationPhase.Render or EvaluationPhase.Submission),
        Replay: "deterministic-over-pinned-clock-and-snapshot");

    /// <summary>The admitted environment every evaluation here presents.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);
}
