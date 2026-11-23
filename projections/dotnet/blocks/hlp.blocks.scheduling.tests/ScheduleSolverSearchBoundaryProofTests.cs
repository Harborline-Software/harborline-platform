using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleSolverSearchBoundaryProofTests
{
    [Theory]
    [InlineData(0, "Indeterminate", "WORK_BUDGET_EXHAUSTED", 0)]
    [InlineData(1, "Indeterminate", "WORK_BUDGET_EXHAUSTED", 1)]
    [InlineData(2, "Feasible", "ALL_HARD_CONSTRAINTS_SATISFIED", 2)]
    public void scheduling_eng_10_scheduling_ck_18_solver_boundary_budget(int budget, string status, string reason, int work)
    {
        var result = new DeterministicExhaustiveProofSolver().Solve(Single(true), budget);
        Assert.Equal(status, result.Status.ToString());
        Assert.Equal("FeasibleProof", result.Guarantee.ToString());
        Assert.Equal(reason, result.ReasonCode);
        Assert.Equal(work, result.WorkUnits);
        if (budget == 2) AssertAssignment(Assert.Single(result.Assignments), "a", 0, 1);
        else Assert.Empty(result.Assignments);
    }

    [Fact]
    public void scheduling_eng_10_scheduling_ck_18_solver_boundary_incomplete_resources()
    {
        var result = new DeterministicExhaustiveProofSolver().Solve(Single(false), 2);
        Assert.Equal("Indeterminate", result.Status.ToString());
        Assert.Equal("FeasibleProof", result.Guarantee.ToString());
        Assert.Equal("PINNED_FACTS_INCOMPLETE", result.ReasonCode);
        Assert.Equal(new[] { "resources" }, result.IncompleteFactSets);
        Assert.Equal(0, result.WorkUnits);
        Assert.Empty(result.Assignments);
    }

    [Theory]
    [InlineData(1, "Feasible", "ALL_HARD_CONSTRAINTS_SATISFIED")]
    [InlineData(0, "ProvenInfeasible", "SEARCH_SPACE_EXHAUSTED")]
    public void scheduling_eng_10_scheduling_ck_18_solver_boundary_half_open(int bStart, string status, string reason)
    {
        var problem = new CompiledPlanningProblem("p", "v1", [new("a", 1), new("b", 1)],
            new Dictionary<string, IReadOnlyList<AssignmentCandidate>> {
                ["a"] = [new("a", 0, 1, ["r"])],
                ["b"] = [new("b", bStart, bStart + 1, ["r"])],
            }, [], Pins(true));
        var result = new DeterministicExhaustiveProofSolver().Solve(problem, 20);
        Assert.Equal(status, result.Status.ToString());
        Assert.Equal("FeasibleProof", result.Guarantee.ToString());
        Assert.Equal(reason, result.ReasonCode);
        if (bStart == 1)
        {
            Assert.Equal(2, result.Assignments.Count);
            AssertAssignment(result.Assignments[0], "a", 0, 1);
            AssertAssignment(result.Assignments[1], "b", 1, 2);
        }
        else Assert.Empty(result.Assignments);
    }

    private static CompiledPlanningProblem Single(bool complete) => new("p", "v1", [new("a", 1)],
        new Dictionary<string, IReadOnlyList<AssignmentCandidate>> {
            ["a"] = [new("a", 0, 1, ["r"]), new("a", 1, 2, ["r"])],
        }, [], Pins(complete));

    private static PlanningFactSetPin[] Pins(bool complete) =>
    [new("activities", "v1", true), new("time-windows", "v1", true),
     new("resources", "v1", complete), new("resource-requirements", "v1", true), new("precedence", "v1", true)];

    private static void AssertAssignment(AssignmentCandidate assignment, string id, int start, int end)
    {
        Assert.Equal(id, assignment.ActivityId);
        Assert.Equal(start, assignment.StartSlot);
        Assert.Equal(end, assignment.EndSlotExclusive);
        Assert.Equal(new[] { "r" }, assignment.ResourceIds);
    }
}
