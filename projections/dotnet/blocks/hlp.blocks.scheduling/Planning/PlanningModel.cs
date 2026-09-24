namespace Harborline.Blocks.Scheduling.Planning;

public enum SolveStatus
{
    Feasible,
    ProvenInfeasible,
    Partial,
    Indeterminate,
    Unsupported,
    Stale,
    Cancelled,
}

public enum SolverGuarantee
{
    Estimate,
    BoundedHeuristic,
    FeasibleProof,
    OptimalProof,
}

public sealed record Activity(string Id, int DurationSlots);

public sealed record TimeWindow(string ActivityId, int EarliestStartSlot, int LatestStartSlot);

public sealed record PlanningResource(
    string Id,
    IReadOnlySet<string> Capabilities,
    IReadOnlySet<int> AvailableSlots);

public sealed record ResourceRequirement(
    string Id,
    string ActivityId,
    string Capability,
    int Quantity);

public sealed record PrecedenceConstraint(
    string Id,
    string BeforeActivityId,
    string AfterActivityId,
    int MinimumGapSlots);

public static class PlanningFactSets
{
    public const string Activities = "activities";
    public const string TimeWindows = "time-windows";
    public const string Resources = "resources";
    public const string ResourceRequirements = "resource-requirements";
    public const string Precedence = "precedence";

    public static IReadOnlyList<string> Required { get; } =
    [
        Activities,
        TimeWindows,
        Resources,
        ResourceRequirements,
        Precedence,
    ];
}

public sealed record PlanningFactSetPin(
    string FactSet,
    string Version,
    bool IsComplete);

public sealed record SchedulingProfile(
    string ProfileId,
    string InputVersion,
    IReadOnlyList<Activity> Activities,
    IReadOnlyList<TimeWindow> TimeWindows,
    IReadOnlyList<PlanningResource> Resources,
    IReadOnlyList<ResourceRequirement> ResourceRequirements,
    IReadOnlyList<PrecedenceConstraint> Precedence,
    IReadOnlyList<PlanningFactSetPin> FactSetPins);

public sealed record AssignmentCandidate(
    string ActivityId,
    int StartSlot,
    int EndSlotExclusive,
    IReadOnlyList<string> ResourceIds);

public sealed record CompiledPlanningProblem(
    string ProfileId,
    string InputVersion,
    IReadOnlyList<Activity> Activities,
    IReadOnlyDictionary<string, IReadOnlyList<AssignmentCandidate>> CandidatesByActivity,
    IReadOnlyList<PrecedenceConstraint> Precedence,
    IReadOnlyList<PlanningFactSetPin> FactSetPins)
{
    public IReadOnlyList<string> IncompleteFactSets => PlanningFactSets.Required
        .Where(required => !FactSetPins.Any(pin =>
            string.Equals(pin.FactSet, required, StringComparison.Ordinal) && pin.IsComplete))
        .ToArray();
}

public sealed record SchedulingProposal(
    string ProposalId,
    string ProfileId,
    string InputVersion,
    string SolverId,
    SolverGuarantee Guarantee,
    SolveStatus Status,
    IReadOnlyList<AssignmentCandidate> Assignments,
    int WorkUnits,
    string ReasonCode,
    IReadOnlyList<string> IncompleteFactSets);

public interface IPlanningSolver
{
    string SolverId { get; }

    SolverGuarantee Guarantee { get; }

    SchedulingProposal Solve(CompiledPlanningProblem problem, int deterministicWorkBudget);
}

public sealed class UnsupportedProfileException(string message) : Exception(message);
