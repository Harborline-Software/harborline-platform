namespace Harborline.Blocks.Scheduling.Planning;

public sealed class DeterministicExhaustiveProofSolver : IPlanningSolver
{
    public string SolverId => "prototype.exhaustive-proof.v1";

    public SolverGuarantee Guarantee => SolverGuarantee.FeasibleProof;

    public SchedulingProposal Solve(CompiledPlanningProblem problem, int deterministicWorkBudget)
    {
        if (deterministicWorkBudget <= 0)
        {
            return Proposal(problem, SolveStatus.Indeterminate, [], 0, "WORK_BUDGET_EXHAUSTED");
        }

        var activityOrder = problem.Activities
            .OrderBy(value => problem.CandidatesByActivity[value.Id].Count)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .Select(value => value.Id)
            .ToArray();
        var assignments = new Dictionary<string, AssignmentCandidate>(StringComparer.Ordinal);
        var workUnits = 0;
        var budgetExhausted = false;

        bool Search(int index)
        {
            if (index == activityOrder.Length)
            {
                return true;
            }

            var activityId = activityOrder[index];
            foreach (var candidate in problem.CandidatesByActivity[activityId])
            {
                if (workUnits >= deterministicWorkBudget)
                {
                    budgetExhausted = true;
                    return false;
                }

                workUnits++;
                if (!AssignmentRules.CanAdd(candidate, assignments, problem.Precedence))
                {
                    continue;
                }

                assignments.Add(activityId, candidate);
                if (Search(index + 1))
                {
                    return true;
                }

                assignments.Remove(activityId);
                if (budgetExhausted)
                {
                    return false;
                }
            }

            return false;
        }

        if (Search(0))
        {
            return Proposal(
                problem,
                SolveStatus.Feasible,
                assignments.Values.OrderBy(value => value.ActivityId, StringComparer.Ordinal).ToArray(),
                workUnits,
                "ALL_HARD_CONSTRAINTS_SATISFIED");
        }

        return budgetExhausted
            ? Proposal(problem, SolveStatus.Indeterminate, [], workUnits, "WORK_BUDGET_EXHAUSTED")
            : Proposal(problem, SolveStatus.ProvenInfeasible, [], workUnits, "SEARCH_SPACE_EXHAUSTED");
    }

    private SchedulingProposal Proposal(
        CompiledPlanningProblem problem,
        SolveStatus status,
        IReadOnlyList<AssignmentCandidate> assignments,
        int workUnits,
        string reasonCode) =>
        new(
            $"{SolverId}:{problem.ProfileId}:{problem.InputVersion}:{status}",
            problem.ProfileId,
            problem.InputVersion,
            SolverId,
            Guarantee,
            status,
            assignments,
            workUnits,
            reasonCode);
}

public sealed class GreedyFirstFitSolver : IPlanningSolver
{
    public string SolverId => "prototype.greedy-first-fit.v1";

    public SolverGuarantee Guarantee => SolverGuarantee.BoundedHeuristic;

    public SchedulingProposal Solve(CompiledPlanningProblem problem, int deterministicWorkBudget)
    {
        if (deterministicWorkBudget <= 0)
        {
            return Proposal(problem, SolveStatus.Indeterminate, [], 0, "WORK_BUDGET_EXHAUSTED");
        }

        var assignments = new Dictionary<string, AssignmentCandidate>(StringComparer.Ordinal);
        var workUnits = 0;
        foreach (var activity in problem.Activities.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            AssignmentCandidate? selected = null;
            foreach (var candidate in problem.CandidatesByActivity[activity.Id])
            {
                if (workUnits >= deterministicWorkBudget)
                {
                    return Proposal(
                        problem,
                        SolveStatus.Indeterminate,
                        assignments.Values.ToArray(),
                        workUnits,
                        "WORK_BUDGET_EXHAUSTED");
                }

                workUnits++;
                if (AssignmentRules.CanAdd(candidate, assignments, problem.Precedence))
                {
                    selected = candidate;
                    break;
                }
            }

            if (selected is null)
            {
                return Proposal(
                    problem,
                    SolveStatus.Partial,
                    assignments.Values.ToArray(),
                    workUnits,
                    "GREEDY_DEAD_END");
            }

            assignments.Add(activity.Id, selected);
        }

        return Proposal(
            problem,
            SolveStatus.Feasible,
            assignments.Values.OrderBy(value => value.ActivityId, StringComparer.Ordinal).ToArray(),
            workUnits,
            "HEURISTIC_COMPLETE_ASSIGNMENT");
    }

    private SchedulingProposal Proposal(
        CompiledPlanningProblem problem,
        SolveStatus status,
        IReadOnlyList<AssignmentCandidate> assignments,
        int workUnits,
        string reasonCode) =>
        new(
            $"{SolverId}:{problem.ProfileId}:{problem.InputVersion}:{status}",
            problem.ProfileId,
            problem.InputVersion,
            SolverId,
            Guarantee,
            status,
            assignments,
            workUnits,
            reasonCode);
}

internal static class AssignmentRules
{
    public static bool CanAdd(
        AssignmentCandidate candidate,
        IReadOnlyDictionary<string, AssignmentCandidate> assignments,
        IReadOnlyList<PrecedenceConstraint> precedence)
    {
        foreach (var existing in assignments.Values)
        {
            if (Overlaps(candidate, existing) && candidate.ResourceIds.Intersect(existing.ResourceIds).Any())
            {
                return false;
            }
        }

        foreach (var constraint in precedence)
        {
            if (constraint.BeforeActivityId == candidate.ActivityId &&
                assignments.TryGetValue(constraint.AfterActivityId, out var after) &&
                candidate.EndSlotExclusive + constraint.MinimumGapSlots > after.StartSlot)
            {
                return false;
            }

            if (constraint.AfterActivityId == candidate.ActivityId &&
                assignments.TryGetValue(constraint.BeforeActivityId, out var before) &&
                before.EndSlotExclusive + constraint.MinimumGapSlots > candidate.StartSlot)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Overlaps(AssignmentCandidate left, AssignmentCandidate right) =>
        left.StartSlot < right.EndSlotExclusive && right.StartSlot < left.EndSlotExclusive;
}
