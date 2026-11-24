using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleCandidateEnumerationProofTests
{
    [Fact(DisplayName = "scheduling-eng-10 scheduling-bound-3 finite-candidate complete ordered inclusive starts")]
    public void FiniteCandidatesPreserveCompleteOrderedInclusiveStarts()
    {
        var candidates = new FiniteCandidateCompiler().Compile(DurationSupplyProfile()).CandidatesByActivity["a"];

        // Oracle: candidate-enumeration-proof acceptance 1, four independently specified tuples.
        Assert.Collection(candidates,
            candidate => AssertCandidate(candidate, 0, 2, ["r1", "r2"]),
            candidate => AssertCandidate(candidate, 0, 2, ["r1", "r3"]),
            candidate => AssertCandidate(candidate, 0, 2, ["r2", "r3"]),
            candidate => AssertCandidate(candidate, 1, 3, ["r1", "r2"]));
    }

    [Fact(DisplayName = "scheduling-eng-10 scheduling-bound-3 finite-candidate whole-duration supply")]
    public void FiniteCandidatesRequireWholeDurationSupply()
    {
        var candidates = new FiniteCandidateCompiler().Compile(DurationSupplyProfile()).CandidatesByActivity["a"];

        // At start 1, r3 lacks occupied slot 2; r4 has the wrong capability.
        Assert.Collection(candidates.Where(candidate => candidate.StartSlot == 1),
            candidate => AssertCandidate(candidate, 1, 3, ["r1", "r2"]));
    }

    [Fact(DisplayName = "scheduling-eng-10 scheduling-bound-3 finite-candidate distinct demands pinned-implementation characterization")]
    public void FiniteCandidatesExcludeReuseAcrossDistinctDemandsCharacterization()
    {
        var profile = new SchedulingProfile("distinct-demands", "v1",
            [new("a", 1)], [new("a", 0, 0)],
            [new("r1", new HashSet<string> { "skill" }, new HashSet<int> { 0 })],
            [new("d1", "a", "skill", 1), new("d2", "a", "skill", 1)], [], CompletePins());
        var compiler = new FiniteCandidateCompiler();

        // Oracle: acceptance 2 characterizes the pinned compiler's distinct-resource guard;
        // the design rows alone do not require distinctness across separate demands.
        Assert.Empty(compiler.Compile(profile).CandidatesByActivity["a"]);

        var oneDemand = profile with { ResourceRequirements = [new("d1", "a", "skill", 1)] };
        Assert.Collection(compiler.Compile(oneDemand).CandidatesByActivity["a"],
            candidate => AssertCandidate(candidate, 0, 1, ["r1"]));
    }

    private static SchedulingProfile DurationSupplyProfile() => new("duration-supply", "v1",
        [new("a", 2)], [new("a", 0, 1)],
        [
            new("r4", new HashSet<string> { "other" }, new HashSet<int> { 0, 1, 2 }),
            new("r3", new HashSet<string> { "skill" }, new HashSet<int> { 0, 1 }),
            new("r2", new HashSet<string> { "skill" }, new HashSet<int> { 0, 1, 2 }),
            new("r1", new HashSet<string> { "skill" }, new HashSet<int> { 0, 1, 2 }),
        ],
        [new("d1", "a", "skill", 2)], [], CompletePins());

    private static PlanningFactSetPin[] CompletePins() =>
    [
        new("activities", "v1", true),
        new("time-windows", "v1", true),
        new("resources", "v1", true),
        new("resource-requirements", "v1", true),
        new("precedence", "v1", true),
    ];

    private static void AssertCandidate(AssignmentCandidate candidate, int start, int endExclusive, string[] resourceIds)
    {
        Assert.Equal("a", candidate.ActivityId);
        Assert.Equal(start, candidate.StartSlot);
        Assert.Equal(endExclusive, candidate.EndSlotExclusive);
        Assert.Equal(resourceIds, candidate.ResourceIds);
    }
}
