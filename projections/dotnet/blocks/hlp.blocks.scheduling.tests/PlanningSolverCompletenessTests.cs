using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class PlanningSolverCompletenessTests
{
    [Fact]
    public void ExhaustedSearch_WithCompletePinnedFacts_IsProvenInfeasible()
    {
        var proposal = Solve(CreatePins(isResourcesComplete: true), deterministicWorkBudget: 10);

        Assert.Equal(SolveStatus.ProvenInfeasible, proposal.Status);
        Assert.Equal("SEARCH_SPACE_EXHAUSTED", proposal.ReasonCode);
        Assert.Empty(proposal.IncompleteFactSets);
    }

    [Fact]
    public void IncompletePinnedFacts_AreNamedAndIndeterminate()
    {
        var proposal = Solve(CreatePins(isResourcesComplete: false), deterministicWorkBudget: 10);

        Assert.Equal(SolveStatus.Indeterminate, proposal.Status);
        Assert.Equal("PINNED_FACTS_INCOMPLETE", proposal.ReasonCode);
        Assert.Equal([PlanningFactSets.Resources], proposal.IncompleteFactSets);
    }

    [Fact]
    public void MissingPinnedFacts_AreNamedAndIndeterminate()
    {
        var pins = CreatePins(isResourcesComplete: true)
            .Where(pin => pin.FactSet != PlanningFactSets.TimeWindows)
            .ToArray();

        var proposal = Solve(pins, deterministicWorkBudget: 10);

        Assert.Equal(SolveStatus.Indeterminate, proposal.Status);
        Assert.Equal([PlanningFactSets.TimeWindows], proposal.IncompleteFactSets);
    }

    [Fact]
    public void CompletePinnedFacts_WithExhaustedBudget_RemainIndeterminate()
    {
        var proposal = Solve(CreatePins(isResourcesComplete: true), deterministicWorkBudget: 0);

        Assert.Equal(SolveStatus.Indeterminate, proposal.Status);
        Assert.Equal("WORK_BUDGET_EXHAUSTED", proposal.ReasonCode);
        Assert.Empty(proposal.IncompleteFactSets);
    }

    private static SchedulingProposal Solve(
        IReadOnlyList<PlanningFactSetPin> pins,
        int deterministicWorkBudget)
    {
        var profile = new SchedulingProfile(
            ProfileId: "profile-1",
            InputVersion: "input-v1",
            Activities: [new Activity("activity-1", DurationSlots: 1)],
            TimeWindows: [new TimeWindow("activity-1", EarliestStartSlot: 0, LatestStartSlot: 0)],
            Resources: [],
            ResourceRequirements:
            [
                new ResourceRequirement(
                    "requirement-1",
                    "activity-1",
                    Capability: "capability-1",
                    Quantity: 1),
            ],
            Precedence: [],
            FactSetPins: pins);
        var problem = new FiniteCandidateCompiler().Compile(profile);

        return new DeterministicExhaustiveProofSolver().Solve(problem, deterministicWorkBudget);
    }

    private static IReadOnlyList<PlanningFactSetPin> CreatePins(bool isResourcesComplete) =>
    [
        new(PlanningFactSets.Activities, "activities-v1", IsComplete: true),
        new(PlanningFactSets.TimeWindows, "windows-v1", IsComplete: true),
        new(PlanningFactSets.Resources, "resources-v1", isResourcesComplete),
        new(PlanningFactSets.ResourceRequirements, "requirements-v1", IsComplete: true),
        new(PlanningFactSets.Precedence, "precedence-v1", IsComplete: true),
    ];
}
