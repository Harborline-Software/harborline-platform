namespace Harborline.Blocks.Scheduling.Planning;

/// <summary>Outcome of a deterministic planning attempt.</summary>
public enum SolveStatus
{
    /// <summary>A complete assignment satisfies every hard constraint.</summary>
    Feasible,
    /// <summary>The searched candidate space contains no assignment satisfying every hard constraint.</summary>
    ProvenInfeasible,
    /// <summary>The solver assigned a subset of activities before reaching a dead end.</summary>
    Partial,
    /// <summary>The solver could not establish an outcome, commonly because inputs or budget were insufficient.</summary>
    Indeterminate,
    /// <summary>The solver does not support the requested profile.</summary>
    Unsupported,
    /// <summary>The result is based on input state that is no longer current.</summary>
    Stale,
    /// <summary>The planning attempt stopped because cancellation was requested.</summary>
    Cancelled,
}

/// <summary>Strength of the claim made by a planning solver about its result.</summary>
public enum SolverGuarantee
{
    /// <summary>A best-effort estimate without a completeness or feasibility proof.</summary>
    Estimate,
    /// <summary>A bounded heuristic result whose search is limited by an explicit strategy or budget.</summary>
    BoundedHeuristic,
    /// <summary>A feasible result has been established, but optimality is not claimed.</summary>
    FeasibleProof,
    /// <summary>The returned result is proven optimal for the supplied problem.</summary>
    OptimalProof,
}

/// <summary>An activity identified by a stable id and measured in integer planning slots.</summary>
/// <param name="Id">Stable activity identifier.</param>
/// <param name="DurationSlots">Positive duration in planning slots.</param>
public sealed record Activity(string Id, int DurationSlots);

/// <summary>The inclusive start bounds for an activity in planning slots.</summary>
/// <param name="ActivityId">Activity to which the window applies.</param>
/// <param name="EarliestStartSlot">Earliest permitted start slot.</param>
/// <param name="LatestStartSlot">Latest permitted start slot.</param>
public sealed record TimeWindow(string ActivityId, int EarliestStartSlot, int LatestStartSlot);

/// <summary>A resource and the capabilities and slots in which it may be assigned.</summary>
/// <param name="Id">Stable resource identifier.</param>
/// <param name="Capabilities">Capability labels available from the resource.</param>
/// <param name="AvailableSlots">Planning slots during which the resource is available.</param>
public sealed record PlanningResource(
    string Id,
    IReadOnlySet<string> Capabilities,
    IReadOnlySet<int> AvailableSlots);

/// <summary>A quantity of a capability required by one activity.</summary>
/// <param name="Id">Stable requirement identifier.</param>
/// <param name="ActivityId">Activity that owns the requirement.</param>
/// <param name="Capability">Capability that an assigned resource must provide.</param>
/// <param name="Quantity">Required quantity; the compiler enforces the available supply.</param>
public sealed record ResourceRequirement(
    string Id,
    string ActivityId,
    string Capability,
    int Quantity);

/// <summary>A minimum-gap ordering constraint between two activities.</summary>
/// <param name="Id">Stable constraint identifier.</param>
/// <param name="BeforeActivityId">Activity that must finish first.</param>
/// <param name="AfterActivityId">Activity that starts after the required gap.</param>
/// <param name="MinimumGapSlots">Non-negative gap in planning slots.</param>
public sealed record PrecedenceConstraint(
    string Id,
    string BeforeActivityId,
    string AfterActivityId,
    int MinimumGapSlots);

/// <summary>Canonical names for the fact sets consumed by scheduling.</summary>
public static class PlanningFactSets
{
    /// <summary>Fact set containing activity definitions.</summary>
    public const string Activities = "activities";
    /// <summary>Fact set containing permitted activity time windows.</summary>
    public const string TimeWindows = "time-windows";
    /// <summary>Fact set containing planning resources.</summary>
    public const string Resources = "resources";
    /// <summary>Fact set containing resource requirements.</summary>
    public const string ResourceRequirements = "resource-requirements";
    /// <summary>Fact set containing precedence constraints.</summary>
    public const string Precedence = "precedence";

    /// <summary>The fact sets that must be complete before a solver may claim a full result.</summary>
    public static IReadOnlyList<string> Required { get; } =
    [
        Activities,
        TimeWindows,
        Resources,
        ResourceRequirements,
        Precedence,
    ];
}

/// <summary>Records the version and completeness of one pinned planning fact set.</summary>
/// <param name="FactSet">Fact-set name, normally one of <see cref="PlanningFactSets.Required"/>.</param>
/// <param name="Version">Source version used by the planning profile.</param>
/// <param name="IsComplete">Whether the pinned source contains the complete fact set.</param>
public sealed record PlanningFactSetPin(
    string FactSet,
    string Version,
    bool IsComplete);

/// <summary>Immutable planning input assembled from versioned domain fact sets.</summary>
/// <param name="ProfileId">Stable profile identifier.</param>
/// <param name="InputVersion">Version of the complete input snapshot.</param>
/// <param name="Activities">Activities to schedule.</param>
/// <param name="TimeWindows">Permitted start windows.</param>
/// <param name="Resources">Resources and their availability.</param>
/// <param name="ResourceRequirements">Capability demand for each activity.</param>
/// <param name="Precedence">Ordering and minimum-gap constraints.</param>
/// <param name="FactSetPins">Completeness and source-version pins for every input fact set.</param>
public sealed record SchedulingProfile(
    string ProfileId,
    string InputVersion,
    IReadOnlyList<Activity> Activities,
    IReadOnlyList<TimeWindow> TimeWindows,
    IReadOnlyList<PlanningResource> Resources,
    IReadOnlyList<ResourceRequirement> ResourceRequirements,
    IReadOnlyList<PrecedenceConstraint> Precedence,
    IReadOnlyList<PlanningFactSetPin> FactSetPins);

/// <summary>A concrete activity placement and its assigned resources.</summary>
/// <param name="ActivityId">Activity placed by the candidate.</param>
/// <param name="StartSlot">Inclusive start slot.</param>
/// <param name="EndSlotExclusive">Exclusive end slot.</param>
/// <param name="ResourceIds">Resources reserved for the placement.</param>
public sealed record AssignmentCandidate(
    string ActivityId,
    int StartSlot,
    int EndSlotExclusive,
    IReadOnlyList<string> ResourceIds);

/// <summary>Solver-ready planning input with finite candidate placements.</summary>
/// <param name="ProfileId">Profile identifier carried from the source input.</param>
/// <param name="InputVersion">Input version carried from the source profile.</param>
/// <param name="Activities">Activities for which candidates were compiled.</param>
/// <param name="CandidatesByActivity">Finite candidate placements keyed by activity id.</param>
/// <param name="Precedence">Constraints that candidate combinations must satisfy.</param>
/// <param name="FactSetPins">Input completeness pins carried into solver output.</param>
public sealed record CompiledPlanningProblem(
    string ProfileId,
    string InputVersion,
    IReadOnlyList<Activity> Activities,
    IReadOnlyDictionary<string, IReadOnlyList<AssignmentCandidate>> CandidatesByActivity,
    IReadOnlyList<PrecedenceConstraint> Precedence,
    IReadOnlyList<PlanningFactSetPin> FactSetPins)
{
    /// <summary>Fact-set names that are absent or marked incomplete in this problem.</summary>
    public IReadOnlyList<string> IncompleteFactSets => PlanningFactSets.Required
        .Where(required => !FactSetPins.Any(pin =>
            string.Equals(pin.FactSet, required, StringComparison.Ordinal) && pin.IsComplete))
        .ToArray();
}

/// <summary>Durable, deterministic output from a planning solver.</summary>
/// <param name="ProposalId">Stable id derived from solver, profile, version, and status.</param>
/// <param name="ProfileId">Profile solved.</param>
/// <param name="InputVersion">Input version solved.</param>
/// <param name="SolverId">Stable solver implementation id.</param>
/// <param name="Guarantee">Strength of the solver's claim.</param>
/// <param name="Status">Outcome of the solve.</param>
/// <param name="Assignments">Assignments produced; may be partial for non-feasible outcomes.</param>
/// <param name="WorkUnits">Deterministic work units consumed.</param>
/// <param name="ReasonCode">Machine-readable explanation for the outcome.</param>
/// <param name="IncompleteFactSets">Fact sets preventing a complete claim, if any.</param>
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

/// <summary>Contract for deterministic solvers operating on compiled planning problems.</summary>
public interface IPlanningSolver
{
    /// <summary>Stable identifier for this solver implementation.</summary>
    string SolverId { get; }

    /// <summary>Guarantee level attached to proposals returned by this solver.</summary>
    SolverGuarantee Guarantee { get; }

    /// <summary>Solves the compiled problem within the supplied deterministic work budget.</summary>
    /// <param name="problem">Finite, compiled planning problem.</param>
    /// <param name="deterministicWorkBudget">Maximum candidate evaluations permitted.</param>
    /// <returns>A proposal whose status and reason code describe the outcome.</returns>
    SchedulingProposal Solve(CompiledPlanningProblem problem, int deterministicWorkBudget);
}

/// <summary>Raised when a planning profile cannot be handled by the selected solver.</summary>
public sealed class UnsupportedProfileException(string message) : Exception(message);
