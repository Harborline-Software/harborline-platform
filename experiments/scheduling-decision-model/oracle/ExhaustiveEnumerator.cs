using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Oracle;

public enum OracleOutcome
{
    Feasible,
    Infeasible,
    Unknown,
}

/// <summary>What the enumerator established, and how much it explored.</summary>
/// <param name="Outcome">Feasible with a witness, Infeasible after a complete search, or Unknown.</param>
/// <param name="Witness">The first feasible plan found, in activity id order; empty otherwise.</param>
/// <param name="SolutionCount">Feasible plans counted, up to the requested cap.</param>
/// <param name="SolutionCountIsExact">True when the whole space was explored, so the count is exact.</param>
/// <param name="Nodes">Placements tried.</param>
/// <param name="Reason">Machine-readable explanation.</param>
public sealed record OracleResult(
    OracleOutcome Outcome,
    IReadOnlyList<AssignmentCandidate> Witness,
    long SolutionCount,
    bool SolutionCountIsExact,
    long Nodes,
    string Reason);

/// <summary>
/// Exhaustive reference enumerator for profile <c>sdm.unit-resource.v1</c>, written from
/// docs/profile-spec.md. It builds placements as resource <em>sets</em> (not per-requirement
/// products), searches activities in profile order with no heuristic pruning, and passes every
/// complete plan through <see cref="PlanChecker"/> before counting it.
/// </summary>
public sealed class ExhaustiveEnumerator
{
    private static readonly string[] RequiredFactSets =
        ["activities", "time-windows", "resources", "resource-requirements", "precedence"];

    /// <param name="profile">Profile to enumerate.</param>
    /// <param name="nodeCap">Maximum placements tried before reporting Unknown.</param>
    /// <param name="solutionCap">Stop counting after this many feasible plans (1 = feasibility only).</param>
    public OracleResult Solve(SchedulingProfile profile, long nodeCap = 10_000_000, long solutionCap = 1)
    {
        var incomplete = RequiredFactSets.Where(name =>
            !profile.FactSetPins.Any(pin => pin.FactSet == name && pin.IsComplete)).ToArray();
        if (incomplete.Length > 0)
        {
            return new(OracleOutcome.Unknown, [], 0, false, 0, "PINNED_FACTS_INCOMPLETE");
        }

        var checker = new PlanChecker(profile);
        var activities = profile.Activities.ToArray();
        var domains = activities.Select(a => Placements(profile, checker, a)).ToArray();

        var chosen = new AssignmentCandidate[activities.Length];
        IReadOnlyList<AssignmentCandidate> witness = [];
        long solutions = 0;
        long nodes = 0;
        var capped = false;
        var precedence = profile.Precedence.ToArray();
        var index = activities.Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);

        Search(0);

        if (capped)
        {
            return solutions > 0
                ? new(OracleOutcome.Feasible, witness, solutions, false, nodes, "NODE_CAP_AFTER_WITNESS")
                : new(OracleOutcome.Unknown, [], 0, false, nodes, "NODE_CAP_EXHAUSTED");
        }

        var exact = solutions < solutionCap;
        return solutions > 0
            ? new(OracleOutcome.Feasible, witness, solutions, exact, nodes, "WITNESS_CHECKED")
            : new(OracleOutcome.Infeasible, [], 0, true, nodes, "COMPLETE_SPACE_EXHAUSTED");

        // Returns false to stop the whole search.
        bool Search(int depth)
        {
            if (depth == activities.Length)
            {
                var plan = chosen.OrderBy(p => p.ActivityId, StringComparer.Ordinal).ToArray();
                if (!checker.Check(plan).IsFeasible)
                {
                    throw new InvalidOperationException("Enumerator produced a plan its own checker rejects.");
                }

                if (solutions == 0)
                {
                    witness = plan;
                }

                solutions++;
                return solutions < solutionCap;
            }

            foreach (var placement in domains[depth])
            {
                if (++nodes > nodeCap)
                {
                    capped = true;
                    return false;
                }

                if (!ConsistentWithChosen(placement, depth))
                {
                    continue;
                }

                chosen[depth] = placement;
                if (!Search(depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        bool ConsistentWithChosen(AssignmentCandidate placement, int depth)
        {
            for (var k = 0; k < depth; k++)
            {
                if (PlanChecker.SharedResourceWhileOverlapping(placement, chosen[k]) is not null)
                {
                    return false;
                }
            }

            foreach (var c in precedence)
            {
                if (c.BeforeActivityId == placement.ActivityId &&
                    index[c.AfterActivityId] < depth &&
                    !PlanChecker.PrecedenceHolds(placement, chosen[index[c.AfterActivityId]], c.MinimumGapSlots))
                {
                    return false;
                }

                if (c.AfterActivityId == placement.ActivityId &&
                    index[c.BeforeActivityId] < depth &&
                    !PlanChecker.PrecedenceHolds(chosen[index[c.BeforeActivityId]], placement, c.MinimumGapSlots))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Every placement that satisfies R2–R6 on its own, as unique resource sets.</summary>
    public static IReadOnlyList<AssignmentCandidate> Placements(
        SchedulingProfile profile,
        PlanChecker checker,
        Activity activity)
    {
        var window = profile.TimeWindows.Single(w => w.ActivityId == activity.Id);
        var needed = profile.ResourceRequirements
            .Where(q => q.ActivityId == activity.Id)
            .ToArray();
        var size = needed.Sum(q => q.Quantity);
        var capabilities = needed.Select(q => q.Capability).ToHashSet(StringComparer.Ordinal);
        var relevant = profile.Resources
            .Where(r => r.Capabilities.Overlaps(capabilities))
            .Select(r => r.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var result = new List<AssignmentCandidate>();
        for (var start = window.EarliestStartSlot; start <= window.LatestStartSlot; start++)
        {
            foreach (var subset in Subsets(relevant, size))
            {
                var candidate = new AssignmentCandidate(activity.Id, start, start + activity.DurationSlots, subset);
                if (!checker.CheckPlacement(candidate).Any())
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    private static IEnumerable<string[]> Subsets(string[] items, int size)
    {
        if (size == 0 || size > items.Length)
        {
            yield break;
        }

        var picks = Enumerable.Range(0, size).ToArray();
        while (true)
        {
            yield return picks.Select(i => items[i]).ToArray();
            var k = size - 1;
            while (k >= 0 && picks[k] == items.Length - size + k)
            {
                k--;
            }

            if (k < 0)
            {
                yield break;
            }

            picks[k]++;
            for (var m = k + 1; m < size; m++)
            {
                picks[m] = picks[m - 1] + 1;
            }
        }
    }
}
