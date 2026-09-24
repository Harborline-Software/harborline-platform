using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Functions;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>
/// Layout block guards run at render over root and per-row values. Its expression environment declaration (DES-0018 <c>rules-ck-28</c>, ADR 0099 decision 8) is this
/// borrower's own row; Rules admits it and every evaluation here presents the admission.
/// </summary>
public static class LayoutExpressionEnvironment
{
    /// <summary>The declaration as this borrower states it.</summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "layout-block-guard",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string> { ["field"] = "layout root value", ["row"] = "repeating-collection row value" },
        Operations: [.. BuiltInFunctionRegister.Functions.Select(function => function.Key)],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "render-clock",
        TimeZone: "utc",
        Phases: Enum.GetValues<EvaluationPhase>().ToDictionary(phase => phase, phase => phase is EvaluationPhase.Render),
        Replay: "deterministic-over-pinned-clock-and-snapshot");

    /// <summary>The admitted environment every evaluation here presents.</summary>
    public static AdmittedEnvironment Admitted { get; } = BorrowerEnvironmentAdmission.Admit(Declaration);
}
