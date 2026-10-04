using System.Diagnostics;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Baselines;
using Harborline.Experiments.SchedulingDecisionModel.Engine;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;

namespace Harborline.Experiments.SchedulingDecisionModel.Evaluation;

/// <summary>One method's run on one instance, judged against the oracle label.</summary>
public sealed record RunResult(
    string Method,
    string InstanceId,
    string GroupId,
    string Family,
    string SizeClass,
    string Split,
    string Oracle,
    string Status,
    long Work,
    double WallMs,
    bool PlanValid,
    bool Solved,
    bool InvalidAccepted,
    bool FalseInfeasible,
    int Fallbacks,
    string? FallbackReason);

/// <summary>A frozen learned-policy configuration (models are loaded and hash-checked once).</summary>
public sealed record LearnedConfig(
    string Name,
    Models.LinearModel? Activity,
    Models.LinearModel? Value,
    int Shortlist,
    long ChargePerActivity,
    long ChargePerCandidate,
    double ContextChargePerOperation)
{
    public Models.LearnedPolicy NewPolicy() => new(Activity, Value, Shortlist, ChargePerActivity, ChargePerCandidate, ContextChargePerOperation);
}

public static class Methods
{
    /// <summary>Learned configurations to run alongside the baselines (keyed by method name).</summary>
    public static Dictionary<string, LearnedConfig> Learned { get; } = new(StringComparer.Ordinal);

    public static readonly string[] All =
    [
        "incumbent",
        "incumbent+dedupe",
        "dom-dynamic",
        "least-slack",
        "dom-dynamic+lcv",
        "greedy-then-search",
        "cpsat",
    ];

    /// <summary>
    /// Runs <paramref name="method"/> under <paramref name="budget"/> work units (CP-SAT: the same
    /// wall-clock allowance, <paramref name="wallBudgetMs"/>, including model build).
    /// </summary>
    public static (SolveStatus Status, IReadOnlyList<AssignmentCandidate> Plan, long Work, int Fallbacks, string? Reason) Run(
        string method, SchedulingProfile profile, long budget, double wallBudgetMs)
    {
        var compiled = new FiniteCandidateCompiler().Compile(profile);
        var engine = new SearchEngine();
        EngineResult R(IOrderingPolicy p, bool dedupe, long b) => engine.Run(compiled, p, b, dedupe);
        (SolveStatus, IReadOnlyList<AssignmentCandidate>, long, int, string?) From(EngineResult r) =>
            (r.Status, r.Assignments, r.Work, r.Fallbacks, r.FirstFallbackReason);

        switch (method)
        {
            case "incumbent": return From(R(new IncumbentPolicy(), false, budget));
            case "incumbent+dedupe": return From(R(new IncumbentPolicy(), true, budget));
            case "dom-dynamic": return From(R(new DynamicDomainPolicy(), true, budget));
            case "least-slack": return From(R(new UrgencyPolicy(), true, budget));
            case "dom-dynamic+lcv": return From(R(new LeastConstrainingPolicy(), true, budget));
            case "greedy-then-search":
            {
                var total = compiled.CandidatesByActivity.Values.Sum(c => (long)c.Count);
                var greedyBudget = (int)Math.Min(budget, total);
                var greedy = new GreedyFirstFitSolver().Solve(compiled, greedyBudget);
                if (greedy.Status == SolveStatus.Feasible)
                {
                    return (SolveStatus.Feasible, greedy.Assignments, greedy.WorkUnits, 0, null);
                }

                var rest = R(new IncumbentPolicy(), false, budget - greedy.WorkUnits);
                return (rest.Status, rest.Assignments, greedy.WorkUnits + rest.Work, rest.Fallbacks, rest.FirstFallbackReason);
            }

            case "cpsat":
            {
                var result = new CpSatFeasibility().Solve(profile, wallBudgetMs / 1000.0, seed: 0);
                var status = result.Outcome switch
                {
                    CpSatOutcome.Feasible => SolveStatus.Feasible,
                    CpSatOutcome.Infeasible => SolveStatus.ProvenInfeasible,
                    _ => SolveStatus.Indeterminate,
                };
                return (status, result.Plan, 0, 0, null);
            }

            default:
                if (Learned.TryGetValue(method, out var config))
                {
                    return From(R(config.NewPolicy(), true, budget));
                }

                throw new ArgumentOutOfRangeException(nameof(method));
        }
    }

    public static RunResult Judge(
        string method,
        Data.ManifestEntry entry,
        string oracle,
        SchedulingProfile profile,
        long budget,
        double wallBudgetMs)
    {
        var watch = Stopwatch.StartNew();
        var (status, plan, work, fallbacks, reason) = Run(method, profile, budget, wallBudgetMs);
        watch.Stop();

        var planValid = status == SolveStatus.Feasible && new PlanChecker(profile).Check(plan).IsFeasible;
        var invalid = status == SolveStatus.Feasible && !planValid;
        var falseInfeasible = status == SolveStatus.ProvenInfeasible && oracle == "Feasible";
        var inTime = method != "cpsat" || watch.Elapsed.TotalMilliseconds <= wallBudgetMs;
        var solved = inTime &&
            ((status == SolveStatus.Feasible && planValid) ||
             (status == SolveStatus.ProvenInfeasible && oracle == "Infeasible"));

        return new RunResult(
            method,
            entry.InstanceId,
            entry.GroupId,
            entry.Family,
            entry.SizeClass,
            entry.Split,
            oracle,
            status.ToString(),
            work,
            Math.Round(watch.Elapsed.TotalMilliseconds, 3),
            planValid,
            solved,
            invalid,
            falseInfeasible,
            fallbacks,
            reason);
    }
}
