using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Functions;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// The engine suite's own borrower: the whole register, every scope token, every phase. Tests that are
/// not about admission present it; the admission tests use narrower declarations.
/// </summary>
internal static class TestAdmission
{
    internal static BorrowerEnvironmentDeclaration Declaration(
        IReadOnlyList<string>? operations = null, IReadOnlyList<string>? variables = null, IReadOnlyList<EvaluationPhase>? phases = null) => new(
        Borrower: "rule-engine-tests",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: (variables ?? ["field", "row", "wf", "timer"]).ToDictionary(name => name, name => "test " + name),
        Operations: operations ?? [.. BuiltInFunctionRegister.Functions.Select(function => function.Key)],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "injected-test-clock",
        TimeZone: "utc",
        Phases: Enum.GetValues<EvaluationPhase>().ToDictionary(phase => phase, phase => phases?.Contains(phase) ?? true),
        Replay: "deterministic");

    internal static EvaluationAdmission Any { get; } = BorrowerEnvironmentAdmission.Admit(Declaration()).For(EvaluationPhase.Run);
}
