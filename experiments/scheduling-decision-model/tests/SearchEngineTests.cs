using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Evaluation;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;

namespace Harborline.Experiments.SchedulingDecisionModel.Tests;

/// <summary>
/// The ordering seam: a policy may only reorder. Faults fall back to the incumbent order, and
/// the result's status is still judged against the hand-proved outcomes in <see cref="Profiles"/>.
/// </summary>
public sealed class SearchEngineTests
{
    private static CompiledPlanningProblem Compile(SchedulingProfile p) => new FiniteCandidateCompiler().Compile(p);

    [Theory]
    [InlineData(5, SolveStatus.Feasible)]
    [InlineData(4, SolveStatus.ProvenInfeasible)]
    public void IncumbentPolicy_MatchesTheProductionSolverExactly(int job2Inspect, SolveStatus handProved)
    {
        var problem = Compile(Profiles.TwoJobs(job2Inspect));
        var production = new DeterministicExhaustiveProofSolver().Solve(problem, 1_000_000);
        var engine = new SearchEngine().Run(problem, new IncumbentPolicy(), 1_000_000);

        Assert.Equal(handProved, engine.Status);
        Assert.Equal(production.Status, engine.Status);
        Assert.Equal(production.WorkUnits, engine.Work);
        Assert.Equal(production.Assignments, engine.Assignments);
    }

    public static TheoryData<string> Faults => new()
    {
        "select-throws", "select-assigned", "select-unknown", "order-throws",
        "order-drops", "order-duplicates", "order-foreign", "order-null",
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public void FaultyPolicy_FallsBack_AndKeepsTheTruthfulOutcome(string fault)
    {
        foreach (var (job2Inspect, handProved) in new[] { (5, SolveStatus.Feasible), (4, SolveStatus.ProvenInfeasible) })
        {
            var profile = Profiles.TwoJobs(job2Inspect);
            var result = new SearchEngine().Run(Compile(profile), new FaultyPolicy(fault), 1_000_000);

            Assert.Equal(handProved, result.Status);
            Assert.True(result.Fallbacks > 0);
            if (result.Status == SolveStatus.Feasible)
            {
                Assert.Empty(new PlanChecker(profile).Check(result.Assignments).Violations);
            }
        }
    }

    [Fact]
    public void ReversedValueOrder_StillReachesEveryCandidate()
    {
        // The two-units gap-2 case is hand-proved infeasible (OracleAgreementTests); exhausting it
        // proves every candidate was tried whatever the order: 3 starts for X, then for each X
        // start all 3 Y starts are checked, so 3 + 9 = 12 checks under any value order.
        var problem = Compile(Profiles.OneResourceTwoUnits().Before("X", "Y", 2).Build());
        var result = new SearchEngine().Run(problem, new ReversePolicy(), 1_000);

        Assert.Equal(SolveStatus.ProvenInfeasible, result.Status);
        Assert.Equal(12, result.Work);
        Assert.Equal(0, result.Fallbacks);
    }

    [Fact]
    public void BudgetExhaustion_IsIndeterminate_NeverInfeasible()
    {
        var problem = Compile(Profiles.TwoJobs(4));
        foreach (IOrderingPolicy policy in new IOrderingPolicy[] { new IncumbentPolicy(), new DynamicDomainPolicy(), new UrgencyPolicy(), new LeastConstrainingPolicy() })
        {
            var result = new SearchEngine().Run(problem, policy, 3);
            Assert.Equal(SolveStatus.Indeterminate, result.Status);
            Assert.True(result.Work <= 3);
        }
    }

    [Theory]
    [InlineData(5, SolveStatus.Feasible)]
    [InlineData(4, SolveStatus.ProvenInfeasible)]
    public void EveryHeuristic_ReachesTheHandProvedOutcome(int job2Inspect, SolveStatus handProved)
    {
        var profile = Profiles.TwoJobs(job2Inspect);
        foreach (IOrderingPolicy policy in new IOrderingPolicy[] { new DynamicDomainPolicy(), new UrgencyPolicy(), new LeastConstrainingPolicy() })
        {
            var result = new SearchEngine().Run(Compile(profile), policy, 1_000_000, dedupe: true);
            Assert.Equal(handProved, result.Status);
            Assert.Equal(0, result.Fallbacks);
            if (result.Status == SolveStatus.Feasible)
            {
                Assert.Empty(new PlanChecker(profile).Check(result.Assignments).Violations);
            }
        }
    }

    private sealed class ReversePolicy : IOrderingPolicy
    {
        private readonly IncumbentPolicy _inner = new();

        public string Id => "reverse";

        public string SelectActivity(SearchState state) => _inner.SelectActivity(state);

        public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates) =>
            candidates.Reverse().ToArray();
    }

    private sealed class FaultyPolicy(string fault) : IOrderingPolicy
    {
        private readonly IncumbentPolicy _inner = new();

        public string Id => fault;

        public string SelectActivity(SearchState state) => fault switch
        {
            "select-throws" => throw new InvalidOperationException("scorer crashed"),
            "select-assigned" when state.Assigned.Count > 0 => state.Assigned.Keys.First(),
            "select-unknown" => "ghost",
            _ => _inner.SelectActivity(state),
        };

        public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates) => fault switch
        {
            "order-throws" => throw new InvalidOperationException("scorer crashed"),
            "order-drops" when candidates.Count > 0 => candidates.Skip(1).ToArray(),
            "order-duplicates" when candidates.Count > 0 => [candidates[0], .. candidates.Skip(1), candidates[0]],
            "order-foreign" when candidates.Count > 0 => [new AssignmentCandidate(activity, -9, -8, ["bench"]), .. candidates.Skip(1)],
            "order-null" => null!,
            _ => candidates,
        };
    }
}
