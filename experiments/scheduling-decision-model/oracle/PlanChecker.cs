using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Oracle;

/// <summary>One broken rule from docs/profile-spec.md.</summary>
public sealed record Violation(string Code, string Subject, string Detail);

/// <summary>Result of checking a plan against profile <c>sdm.unit-resource.v1</c>.</summary>
public sealed record CheckResult(IReadOnlyList<Violation> Violations)
{
    public bool IsFeasible => Violations.Count == 0;
}

/// <summary>
/// Independent checker written from docs/profile-spec.md. It must not call FiniteCandidateCompiler,
/// AssignmentRules or any production solver.
/// </summary>
public sealed class PlanChecker
{
    private readonly SchedulingProfile _profile;
    private readonly Dictionary<string, Activity> _activities;
    private readonly Dictionary<string, TimeWindow> _windows;
    private readonly Dictionary<string, PlanningResource> _resources;
    private readonly Dictionary<string, ResourceRequirement[]> _requirements;

    public PlanChecker(SchedulingProfile profile)
    {
        _profile = profile;
        _activities = profile.Activities.ToDictionary(a => a.Id, StringComparer.Ordinal);
        _windows = profile.TimeWindows.ToDictionary(w => w.ActivityId, StringComparer.Ordinal);
        _resources = profile.Resources.ToDictionary(r => r.Id, StringComparer.Ordinal);
        _requirements = profile.ResourceRequirements
            .GroupBy(q => q.ActivityId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    public CheckResult Check(IReadOnlyList<AssignmentCandidate> plan)
    {
        var violations = new List<Violation>();

        // R1: exactly one placement per activity, none for unknown activities.
        foreach (var group in plan.GroupBy(p => p.ActivityId, StringComparer.Ordinal))
        {
            if (!_activities.ContainsKey(group.Key))
            {
                violations.Add(new("R1-coverage", group.Key, "placement names an unknown activity"));
            }
            else if (group.Count() > 1)
            {
                violations.Add(new("R1-coverage", group.Key, $"{group.Count()} placements"));
            }
        }

        var placed = plan.Select(p => p.ActivityId).ToHashSet(StringComparer.Ordinal);
        foreach (var activity in _profile.Activities.Where(a => !placed.Contains(a.Id)))
        {
            violations.Add(new("R1-coverage", activity.Id, "activity has no placement"));
        }

        foreach (var placement in plan.Where(p => _activities.ContainsKey(p.ActivityId)))
        {
            violations.AddRange(CheckPlacement(placement));
        }

        // R7 and R8 are pairwise rules across placements.
        var known = plan.Where(p => _activities.ContainsKey(p.ActivityId)).ToArray();
        for (var i = 0; i < known.Length; i++)
        {
            for (var j = i + 1; j < known.Length; j++)
            {
                var shared = SharedResourceWhileOverlapping(known[i], known[j]);
                if (shared is not null)
                {
                    violations.Add(new(
                        "R7-exclusive",
                        $"{known[i].ActivityId}+{known[j].ActivityId}",
                        $"both hold {shared} in overlapping intervals"));
                }
            }
        }

        var byActivity = known
            .GroupBy(p => p.ActivityId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var constraint in _profile.Precedence)
        {
            if (byActivity.TryGetValue(constraint.BeforeActivityId, out var before) &&
                byActivity.TryGetValue(constraint.AfterActivityId, out var after) &&
                !PrecedenceHolds(before, after, constraint.MinimumGapSlots))
            {
                violations.Add(new(
                    "R8-precedence",
                    constraint.Id,
                    $"{before.ActivityId} ends {before.EndSlotExclusive} + gap {constraint.MinimumGapSlots} > {after.ActivityId} start {after.StartSlot}"));
            }
        }

        return new CheckResult(violations);
    }

    /// <summary>Rules R2 through R6 for one placement in isolation.</summary>
    public IEnumerable<Violation> CheckPlacement(AssignmentCandidate placement)
    {
        var activity = _activities[placement.ActivityId];
        var id = placement.ActivityId;

        if (placement.EndSlotExclusive != placement.StartSlot + activity.DurationSlots)
        {
            yield return new("R2-duration", id, $"end {placement.EndSlotExclusive} != start {placement.StartSlot} + {activity.DurationSlots}");
        }

        if (!_windows.TryGetValue(id, out var window) ||
            placement.StartSlot < window.EarliestStartSlot ||
            placement.StartSlot > window.LatestStartSlot)
        {
            yield return new("R3-window", id, $"start {placement.StartSlot} outside its start window");
        }

        var distinct = placement.ResourceIds.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length != placement.ResourceIds.Count)
        {
            yield return new("R4-resource-known", id, "a resource is listed twice");
        }

        var unknown = distinct.Where(r => !_resources.ContainsKey(r)).ToArray();
        foreach (var resource in unknown)
        {
            yield return new("R4-resource-known", id, $"unknown resource {resource}");
        }

        var knownResources = distinct.Where(_resources.ContainsKey).ToArray();
        if (!RequirementsMatch(id, knownResources) || unknown.Length > 0)
        {
            yield return new("R5-requirements", id, "resources cannot be split exactly across the requirements");
        }

        foreach (var resource in knownResources)
        {
            var slots = _resources[resource].AvailableSlots;
            for (var slot = placement.StartSlot; slot < placement.EndSlotExclusive; slot++)
            {
                if (!slots.Contains(slot))
                {
                    yield return new("R6-availability", id, $"{resource} unavailable at slot {slot}");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// R5 as a bipartite matching: one requirement "seat" per unit of quantity, and every listed
    /// resource must fill exactly one seat whose capability it has.
    /// </summary>
    public bool RequirementsMatch(string activityId, IReadOnlyList<string> resources)
    {
        var seats = _requirements.TryGetValue(activityId, out var requirements)
            ? requirements.SelectMany(q => Enumerable.Repeat(q.Capability, q.Quantity)).ToArray()
            : [];
        if (seats.Length != resources.Count)
        {
            return false;
        }

        var seatOwner = new int[seats.Length];
        Array.Fill(seatOwner, -1);
        for (var r = 0; r < resources.Count; r++)
        {
            if (!TryAugment(r, new bool[seats.Length]))
            {
                return false;
            }
        }

        return true;

        bool TryAugment(int resourceIndex, bool[] visited)
        {
            var capabilities = _resources[resources[resourceIndex]].Capabilities;
            for (var s = 0; s < seats.Length; s++)
            {
                if (visited[s] || !capabilities.Contains(seats[s]))
                {
                    continue;
                }

                visited[s] = true;
                if (seatOwner[s] < 0 || TryAugment(seatOwner[s], visited))
                {
                    seatOwner[s] = resourceIndex;
                    return true;
                }
            }

            return false;
        }
    }

    public static string? SharedResourceWhileOverlapping(AssignmentCandidate left, AssignmentCandidate right)
    {
        var overlap = left.StartSlot < right.EndSlotExclusive && right.StartSlot < left.EndSlotExclusive;
        return overlap
            ? left.ResourceIds.FirstOrDefault(r => right.ResourceIds.Contains(r, StringComparer.Ordinal))
            : null;
    }

    public static bool PrecedenceHolds(AssignmentCandidate before, AssignmentCandidate after, int gap) =>
        before.EndSlotExclusive + gap <= after.StartSlot;
}
