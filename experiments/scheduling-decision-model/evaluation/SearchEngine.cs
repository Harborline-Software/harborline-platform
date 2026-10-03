using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Evaluation;

/// <summary>
/// Search state visible to an ordering policy. Policies may only read it and pay for consistency
/// checks through <see cref="Check"/>; they cannot assign, remove candidates, or claim a proof.
/// </summary>
public sealed class SearchState
{
    private readonly Dictionary<AssignmentCandidate, bool> _memo = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, AssignmentCandidate> _assigned = new(StringComparer.Ordinal);

    internal SearchState(CompiledPlanningProblem problem, IReadOnlyDictionary<string, IReadOnlyList<AssignmentCandidate>> candidates, long budget)
    {
        Problem = problem;
        Candidates = candidates;
        Budget = budget;
        Neighbours = BuildNeighbours(problem, candidates);
    }

    public CompiledPlanningProblem Problem { get; }

    /// <summary>The complete permitted candidates per activity (after optional de-duplication).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<AssignmentCandidate>> Candidates { get; }

    /// <summary>Activities that share a possible resource or a precedence edge (structural, free to read).</summary>
    public IReadOnlyDictionary<string, string[]> Neighbours { get; }

    public IReadOnlyDictionary<string, AssignmentCandidate> Assigned => _assigned;

    public long Budget { get; }

    public long Work { get; private set; }

    public bool Exhausted { get; private set; }

    public IEnumerable<string> Unassigned => Problem.Activities.Select(a => a.Id).Where(id => !_assigned.ContainsKey(id));

    /// <summary>
    /// Is <paramref name="candidate"/> consistent with the current partial assignment? Costs one
    /// work unit on a cache miss; the cache is cleared whenever the assignment changes. Returns
    /// false once the budget is exhausted.
    /// </summary>
    public bool Check(AssignmentCandidate candidate)
    {
        if (_memo.TryGetValue(candidate, out var cached))
        {
            return cached;
        }

        if (Work >= Budget)
        {
            Exhausted = true;
            return false;
        }

        Work++;
        var ok = Consistency.CanAdd(candidate, _assigned, Problem.Precedence);
        _memo[candidate] = ok;
        return ok;
    }

    /// <summary>Charges work that is not a consistency check (for example scorer inference).</summary>
    public bool Charge(long units)
    {
        if (Work + units > Budget)
        {
            Work = Budget;
            Exhausted = true;
            return false;
        }

        Work += units;
        return true;
    }

    internal void Assign(string activity, AssignmentCandidate candidate)
    {
        _assigned.Add(activity, candidate);
        _memo.Clear();
    }

    internal void Unassign(string activity)
    {
        _assigned.Remove(activity);
        _memo.Clear();
    }

    private static Dictionary<string, string[]> BuildNeighbours(
        CompiledPlanningProblem problem,
        IReadOnlyDictionary<string, IReadOnlyList<AssignmentCandidate>> candidates)
    {
        var resources = candidates.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.SelectMany(c => c.ResourceIds).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        return problem.Activities.ToDictionary(
            a => a.Id,
            a => problem.Activities
                .Where(b => b.Id != a.Id &&
                    (resources[a.Id].Overlaps(resources[b.Id]) ||
                     problem.Precedence.Any(p =>
                         (p.BeforeActivityId == a.Id && p.AfterActivityId == b.Id) ||
                         (p.BeforeActivityId == b.Id && p.AfterActivityId == a.Id))))
                .Select(b => b.Id)
                .ToArray(),
            StringComparer.Ordinal);
    }
}

/// <summary>An ordering policy: which activity next, and in what order to try its candidates.</summary>
public interface IOrderingPolicy
{
    string Id { get; }

    /// <summary>Returns an unassigned activity id.</summary>
    string SelectActivity(SearchState state);

    /// <summary>Returns a permutation of <paramref name="candidates"/> (same references, each exactly once).</summary>
    IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates);
}

public sealed record EngineResult(
    string PolicyId,
    SolveStatus Status,
    IReadOnlyList<AssignmentCandidate> Assignments,
    long Work,
    string ReasonCode,
    int Fallbacks,
    string? FirstFallbackReason);

/// <summary>
/// Exhaustive depth-first search over the compiled candidates with a pluggable ordering. A policy
/// changes exploration order only: every candidate stays reachable, and any policy fault falls back
/// to the incumbent order (fewest compiled candidates first, stable id; compiler value order).
/// </summary>
public sealed class SearchEngine
{
    public EngineResult Run(CompiledPlanningProblem problem, IOrderingPolicy policy, long budget, bool dedupe = false)
    {
        if (problem.IncompleteFactSets.Count > 0)
        {
            return new(policy.Id, SolveStatus.Indeterminate, [], 0, "PINNED_FACTS_INCOMPLETE", 0, null);
        }

        if (budget <= 0)
        {
            return new(policy.Id, SolveStatus.Indeterminate, [], 0, "WORK_BUDGET_EXHAUSTED", 0, null);
        }

        var candidates = dedupe ? Dedupe(problem.CandidatesByActivity) : problem.CandidatesByActivity;
        var state = new SearchState(problem, candidates, budget);
        var fallback = new IncumbentPolicy();
        var fallbacks = 0;
        string? firstReason = null;

        void Fault(string reason)
        {
            fallbacks++;
            firstReason ??= reason;
        }

        bool Search()
        {
            if (state.Assigned.Count == problem.Activities.Count)
            {
                return true;
            }

            var activity = SafeSelect();
            if (state.Exhausted)
            {
                return false;
            }

            var ordered = SafeOrder(activity);
            foreach (var candidate in ordered)
            {
                if (!state.Check(candidate))
                {
                    if (state.Exhausted)
                    {
                        return false;
                    }

                    continue;
                }

                state.Assign(activity, candidate);
                if (Search())
                {
                    return true;
                }

                state.Unassign(activity);
                if (state.Exhausted)
                {
                    return false;
                }
            }

            return false;
        }

        string SafeSelect()
        {
            try
            {
                var chosen = policy.SelectActivity(state);
                if (chosen is not null && candidates.ContainsKey(chosen) && !state.Assigned.ContainsKey(chosen))
                {
                    return chosen;
                }

                Fault("SELECT_INVALID_ACTIVITY");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Fault($"SELECT_THREW:{ex.GetType().Name}");
            }

            return fallback.SelectActivity(state);
        }

        IReadOnlyList<AssignmentCandidate> SafeOrder(string activity)
        {
            var original = candidates[activity];
            try
            {
                var ordered = policy.OrderValues(state, activity, original);
                if (IsPermutation(original, ordered))
                {
                    return ordered;
                }

                Fault("ORDER_NOT_A_PERMUTATION");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Fault($"ORDER_THREW:{ex.GetType().Name}");
            }

            return original;
        }

        var found = Search();
        if (found)
        {
            return new(
                policy.Id,
                SolveStatus.Feasible,
                state.Assigned.Values.OrderBy(a => a.ActivityId, StringComparer.Ordinal).ToArray(),
                state.Work,
                "ALL_HARD_CONSTRAINTS_SATISFIED",
                fallbacks,
                firstReason);
        }

        return state.Exhausted
            ? new(policy.Id, SolveStatus.Indeterminate, [], state.Work, "WORK_BUDGET_EXHAUSTED", fallbacks, firstReason)
            : new(policy.Id, SolveStatus.ProvenInfeasible, [], state.Work, "SEARCH_SPACE_EXHAUSTED", fallbacks, firstReason);
    }

    public static bool IsPermutation(IReadOnlyList<AssignmentCandidate> original, IReadOnlyList<AssignmentCandidate>? ordered)
    {
        if (ordered is null || ordered.Count != original.Count)
        {
            return false;
        }

        var remaining = new Dictionary<AssignmentCandidate, int>(ReferenceEqualityComparer.Instance);
        foreach (var c in original)
        {
            remaining[c] = remaining.GetValueOrDefault(c) + 1;
        }

        foreach (var c in ordered)
        {
            if (c is null || !remaining.TryGetValue(c, out var n) || n == 0)
            {
                return false;
            }

            remaining[c] = n - 1;
        }

        return true;
    }

    /// <summary>Removes candidates that repeat an earlier candidate's start and resource set. Sound: the placement is identical.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<AssignmentCandidate>> Dedupe(
        IReadOnlyDictionary<string, IReadOnlyList<AssignmentCandidate>> candidates) =>
        candidates.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<AssignmentCandidate>)kv.Value
                .DistinctBy(c => (c.StartSlot, string.Join('|', c.ResourceIds.Order(StringComparer.Ordinal))))
                .ToArray(),
            StringComparer.Ordinal);
}

/// <summary>Pairwise consistency, re-stated from the profile spec (R7, R8) for the search engine.</summary>
internal static class Consistency
{
    public static bool CanAdd(
        AssignmentCandidate candidate,
        IReadOnlyDictionary<string, AssignmentCandidate> assigned,
        IReadOnlyList<PrecedenceConstraint> precedence)
    {
        foreach (var existing in assigned.Values)
        {
            if (candidate.StartSlot < existing.EndSlotExclusive &&
                existing.StartSlot < candidate.EndSlotExclusive &&
                candidate.ResourceIds.Any(r => existing.ResourceIds.Contains(r)))
            {
                return false;
            }
        }

        foreach (var c in precedence)
        {
            if (c.BeforeActivityId == candidate.ActivityId &&
                assigned.TryGetValue(c.AfterActivityId, out var after) &&
                candidate.EndSlotExclusive + c.MinimumGapSlots > after.StartSlot)
            {
                return false;
            }

            if (c.AfterActivityId == candidate.ActivityId &&
                assigned.TryGetValue(c.BeforeActivityId, out var before) &&
                before.EndSlotExclusive + c.MinimumGapSlots > candidate.StartSlot)
            {
                return false;
            }
        }

        return true;
    }
}
