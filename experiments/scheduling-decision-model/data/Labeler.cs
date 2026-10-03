using System.Diagnostics;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Baselines;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;

namespace Harborline.Experiments.SchedulingDecisionModel.Data;

/// <summary>
/// Labels for one instance, with provenance. OracleOutcome is the enumerator's when it finished,
/// otherwise CP-SAT's; Unknown is never folded into Infeasible.
/// </summary>
public sealed record InstanceLabel(
    string InstanceId,
    string InputSha256,
    string OracleMethod,
    string OracleOutcome,
    string EnumeratorOutcome,
    long EnumeratorNodes,
    string CpSatOutcome,
    double CpSatWallMs,
    bool CpSatPlanChecked,
    string IncumbentStatus,
    int IncumbentWork,
    bool IncumbentPlanChecked,
    string GreedyStatus,
    int GreedyWork,
    long CompiledCandidates,
    double IncumbentWallMs,
    bool Disagreement,
    string DisagreementDetail);

public static class Labeler
{
    public const long EnumeratorNodeCap = 2_000_000;
    public const int IncumbentWorkCap = 5_000_000;
    public const double CpSatSeconds = 10;

    public static InstanceLabel Label(ManifestEntry entry, SchedulingProfile profile)
    {
        var checker = new PlanChecker(profile);

        var enumerated = new ExhaustiveEnumerator().Solve(profile, EnumeratorNodeCap);
        var cpSat = new CpSatFeasibility().Solve(profile, CpSatSeconds, seed: 0);
        var cpSatChecked = cpSat.Outcome != CpSatOutcome.Feasible || checker.Check(cpSat.Plan).IsFeasible;

        var compiled = new FiniteCandidateCompiler().Compile(profile);
        var candidates = compiled.CandidatesByActivity.Values.Sum(c => (long)c.Count);
        var watch = Stopwatch.StartNew();
        var incumbent = new DeterministicExhaustiveProofSolver().Solve(compiled, IncumbentWorkCap);
        watch.Stop();
        var incumbentChecked = incumbent.Status != SolveStatus.Feasible || checker.Check(incumbent.Assignments).IsFeasible;
        var greedy = new GreedyFirstFitSolver().Solve(compiled, IncumbentWorkCap);

        var (method, outcome) = enumerated.Outcome != OracleOutcome.Unknown
            ? ("enumerator", enumerated.Outcome.ToString())
            : cpSat.Outcome is CpSatOutcome.Feasible or CpSatOutcome.Infeasible
                ? ("cpsat", cpSat.Outcome.ToString())
                : ("none", "Unknown");

        var problems = new List<string>();
        if (!cpSatChecked)
        {
            problems.Add("cpsat plan fails checker");
        }

        if (!incumbentChecked)
        {
            problems.Add("incumbent plan fails checker");
        }

        if (outcome is "Feasible" or "Infeasible")
        {
            if (cpSat.Outcome is CpSatOutcome.Feasible or CpSatOutcome.Infeasible && cpSat.Outcome.ToString() != outcome)
            {
                problems.Add($"cpsat {cpSat.Outcome} vs {method} {outcome}");
            }

            if (incumbent.Status == SolveStatus.Feasible && outcome == "Infeasible")
            {
                problems.Add("incumbent Feasible vs oracle Infeasible");
            }

            if (incumbent.Status == SolveStatus.ProvenInfeasible && outcome == "Feasible")
            {
                problems.Add("incumbent ProvenInfeasible vs oracle Feasible");
            }
        }

        return new InstanceLabel(
            entry.InstanceId,
            entry.InputSha256,
            method,
            outcome,
            enumerated.Outcome.ToString(),
            enumerated.Nodes,
            cpSat.Outcome.ToString(),
            Math.Round(cpSat.WallSeconds * 1000, 3),
            cpSatChecked,
            incumbent.Status.ToString(),
            incumbent.WorkUnits,
            incumbentChecked,
            greedy.Status.ToString(),
            greedy.WorkUnits,
            candidates,
            Math.Round(watch.Elapsed.TotalMilliseconds, 3),
            problems.Count > 0,
            string.Join("; ", problems));
    }
}
