using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Baselines;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;

namespace Harborline.Experiments.SchedulingDecisionModel.Tests;

/// <summary>
/// Hand-counted tiny cases fix the enumerator. CP-SAT (an independent formulation) and the
/// pinned production exhaustive solver must then agree with it on outcome, and every plan
/// either one returns must pass the independent checker.
/// </summary>
public sealed class OracleAgreementTests
{
    public static TheoryData<string, long> HandCounted => new()
    {
        // X and Y take distinct slots of {0,1,2} on one resource: 3 x 2 ordered pairs.
        { "two-units", 6 },
        // X before Y, gap 0: (0,1), (0,2), (1,2).
        { "two-units-precedence", 3 },
        // Y starts at or after X + 2: only (0,2).
        { "two-units-gap-1", 1 },
        // Y starts at or after X + 3: impossible in [0,2].
        { "two-units-gap-2", 0 },
        // Both start at 0 for 2 slots on two interchangeable resources: X->R1,Y->R2 or the swap.
        { "two-resources-swap", 2 },
        // X needs both resources at 0; Y needs one, also only at 0.
        { "quantity-2-clash", 0 },
        // As above, but Y may move to slot 1 and take either resource.
        { "quantity-2-shift", 2 },
        // A requirement no resource can meet.
        { "no-capable-resource", 0 },
        // Job 2 inspects at 5 or 4; see Profiles.TwoJobs. Infeasibility is hand-proved; the
        // feasible variant's count is not hand-counted, so it is checked for feasibility only.
        { "two-jobs-infeasible", 0 },
    };

    private static SchedulingProfile Case(string name) => name switch
    {
        "two-units" => Profiles.OneResourceTwoUnits().Build(),
        "two-units-precedence" => Profiles.OneResourceTwoUnits().Before("X", "Y").Build(),
        "two-units-gap-1" => Profiles.OneResourceTwoUnits().Before("X", "Y", 1).Build(),
        "two-units-gap-2" => Profiles.OneResourceTwoUnits().Before("X", "Y", 2).Build(),
        "two-resources-swap" => new ProfileBuilder()
            .Resource("R1", "c", 0, 3).Resource("R2", "c", 0, 3)
            .Activity("X", 2, 0, 0, ("c", 1)).Activity("Y", 2, 0, 0, ("c", 1)).Build(),
        "quantity-2-clash" => new ProfileBuilder()
            .Resource("R1", "c", 0, 3).Resource("R2", "c", 0, 3)
            .Activity("X", 1, 0, 0, ("c", 2)).Activity("Y", 1, 0, 0, ("c", 1)).Build(),
        "quantity-2-shift" => new ProfileBuilder()
            .Resource("R1", "c", 0, 3).Resource("R2", "c", 0, 3)
            .Activity("X", 1, 0, 0, ("c", 2)).Activity("Y", 1, 0, 1, ("c", 1)).Build(),
        "no-capable-resource" => new ProfileBuilder()
            .Resource("R1", "c", 0, 3)
            .Activity("X", 1, 0, 0, ("d", 1)).Build(),
        "two-jobs-infeasible" => Profiles.TwoJobs(4),
        "two-jobs-feasible" => Profiles.TwoJobs(5),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(HandCounted))]
    public void Enumerator_MatchesHandCount(string name, long expected)
    {
        var result = new ExhaustiveEnumerator().Solve(Case(name), solutionCap: long.MaxValue);

        Assert.Equal(expected, result.SolutionCount);
        Assert.True(result.SolutionCountIsExact);
        Assert.Equal(expected == 0 ? OracleOutcome.Infeasible : OracleOutcome.Feasible, result.Outcome);
    }

    public static TheoryData<string> AllCases =>
        new(HandCounted.Select(row => (string)row[0]).Append("two-jobs-feasible"));

    [Theory]
    [MemberData(nameof(AllCases))]
    public void CpSat_AgreesWithEnumerator_AndItsPlanPassesTheChecker(string name)
    {
        var profile = Case(name);
        var oracle = new ExhaustiveEnumerator().Solve(profile);
        var cpSat = new CpSatFeasibility().Solve(profile);

        Assert.Equal(oracle.Outcome == OracleOutcome.Feasible ? CpSatOutcome.Feasible : CpSatOutcome.Infeasible, cpSat.Outcome);
        if (cpSat.Outcome == CpSatOutcome.Feasible)
        {
            Assert.Empty(new PlanChecker(profile).Check(cpSat.Plan).Violations);
        }
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void ProductionExhaustive_AgreesWithEnumerator_AndItsPlanPassesTheChecker(string name)
    {
        var profile = Case(name);
        var oracle = new ExhaustiveEnumerator().Solve(profile);
        var proposal = new DeterministicExhaustiveProofSolver()
            .Solve(new FiniteCandidateCompiler().Compile(profile), 1_000_000);

        var expected = oracle.Outcome == OracleOutcome.Feasible ? SolveStatus.Feasible : SolveStatus.ProvenInfeasible;
        Assert.Equal(expected, proposal.Status);
        if (proposal.Status == SolveStatus.Feasible)
        {
            Assert.Empty(new PlanChecker(profile).Check(proposal.Assignments).Violations);
        }
    }

    [Fact]
    public void IncompletePins_AreUnknown_NeverInfeasible()
    {
        var profile = Profiles.OneResourceTwoUnits().Before("X", "Y", 2).Build(complete: false);

        Assert.Equal(OracleOutcome.Unknown, new ExhaustiveEnumerator().Solve(profile).Outcome);
    }

    [Fact]
    public void NodeCap_WithoutWitness_IsUnknown_NeverInfeasible()
    {
        // The gap-2 case is infeasible, but a cap of 2 placements cannot finish proving it.
        var profile = Profiles.OneResourceTwoUnits().Before("X", "Y", 2).Build();

        var result = new ExhaustiveEnumerator().Solve(profile, nodeCap: 2);

        Assert.Equal(OracleOutcome.Unknown, result.Outcome);
        Assert.False(result.SolutionCountIsExact);
    }

    /// <summary>
    /// Characterisation (T-1053 Phase 1 finding). Two requirements of the same capability make
    /// the production compiler emit one resource set once per ordering, so the domain holds
    /// duplicates; the enumerator's set-based placements do not. Duplicates inflate the domain
    /// sizes that the incumbent's fewest-candidates-first ordering reads, and they add work.
    /// </summary>
    [Fact]
    public void ProductionCompiler_DuplicatesSameCapabilitySets()
    {
        var profile = new ProfileBuilder()
            .Resource("R1", "c", 0, 0).Resource("R2", "c", 0, 0)
            .Activity("X", 1, 0, 0, ("c", 1), ("c", 1))
            .Build();

        var compiled = new FiniteCandidateCompiler().Compile(profile).CandidatesByActivity["X"];
        var placements = ExhaustiveEnumerator.Placements(profile, new PlanChecker(profile), profile.Activities[0]);

        Assert.Equal(2, compiled.Count);
        Assert.Single(placements);
    }
}
