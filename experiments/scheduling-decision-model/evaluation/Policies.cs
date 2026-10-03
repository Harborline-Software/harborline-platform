using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Evaluation;

/// <summary>
/// The pinned production ordering: activities by compiled candidate count, then ordinal id, fixed
/// once; candidates in compiler order. Free of charge, as in production.
/// </summary>
public sealed class IncumbentPolicy : IOrderingPolicy
{
    private string[]? _order;

    public string Id => "incumbent";

    public string SelectActivity(SearchState state)
    {
        _order ??= state.Problem.Activities
            .OrderBy(a => state.Candidates[a.Id].Count)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => a.Id)
            .ToArray();
        return _order.First(id => !state.Assigned.ContainsKey(id));
    }

    public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates) =>
        candidates;
}

/// <summary>
/// Dynamic smallest-domain-first: the unassigned activity with the fewest candidates consistent
/// with the current assignment (each check charged), ties by ordinal id. A zero domain is picked
/// at once, so the engine backtracks immediately (sound forward checking).
/// </summary>
public sealed class DynamicDomainPolicy(string id = "dom-dynamic") : IOrderingPolicy
{
    public string Id => id;

    public string SelectActivity(SearchState state)
    {
        string? best = null;
        var bestCount = int.MaxValue;
        foreach (var activity in state.Unassigned.Order(StringComparer.Ordinal))
        {
            var count = 0;
            foreach (var c in state.Candidates[activity])
            {
                if (state.Check(c) && ++count >= bestCount)
                {
                    break;
                }

                if (state.Exhausted)
                {
                    return best ?? activity;
                }
            }

            if (count < bestCount)
            {
                best = activity;
                bestCount = count;
                if (count == 0)
                {
                    break;
                }
            }
        }

        return best!;
    }

    public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates) =>
        candidates;
}

/// <summary>
/// Most urgent first (least slack): the unassigned activity whose latest consistent start is
/// earliest; ties by fewer consistent candidates, then ordinal id. Values earliest start first.
/// </summary>
public sealed class UrgencyPolicy : IOrderingPolicy
{
    public string Id => "least-slack";

    public string SelectActivity(SearchState state)
    {
        string? best = null;
        var bestKey = (Latest: int.MaxValue, Count: int.MaxValue);
        foreach (var activity in state.Unassigned.Order(StringComparer.Ordinal))
        {
            var latest = int.MinValue;
            var count = 0;
            foreach (var c in state.Candidates[activity])
            {
                if (state.Check(c))
                {
                    count++;
                    latest = Math.Max(latest, c.StartSlot);
                }

                if (state.Exhausted)
                {
                    return best ?? activity;
                }
            }

            if (count == 0)
            {
                return activity;
            }

            var key = (latest, count);
            if (key.CompareTo(bestKey) < 0)
            {
                best = activity;
                bestKey = key;
            }
        }

        return best!;
    }

    public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates) =>
        candidates.Select((c, i) => (c, i)).OrderBy(x => x.c.StartSlot).ThenBy(x => x.i).Select(x => x.c).ToArray();
}

/// <summary>
/// Dynamic smallest-domain-first plus least-constraining value: each consistent candidate is
/// ranked by how many candidates of structurally neighbouring unassigned activities it would rule
/// out (each pairwise test charged one work unit). Ties keep compiler order.
/// </summary>
public sealed class LeastConstrainingPolicy : IOrderingPolicy
{
    private readonly DynamicDomainPolicy _select = new();

    public string Id => "dom-dynamic+lcv";

    public string SelectActivity(SearchState state) => _select.SelectActivity(state);

    public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates)
    {
        var neighbours = state.Neighbours[activity].Where(n => !state.Assigned.ContainsKey(n)).ToArray();
        var precedence = state.Problem.Precedence;
        var scored = new List<(AssignmentCandidate Candidate, int Index, int Conflicts)>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var conflicts = 0;
            if (state.Check(candidate))
            {
                var probe = new Dictionary<string, AssignmentCandidate>(StringComparer.Ordinal) { [activity] = candidate };
                foreach (var n in neighbours)
                {
                    foreach (var other in state.Candidates[n])
                    {
                        if (!state.Charge(1))
                        {
                            return candidates;
                        }

                        if (!Consistency.CanAdd(other, probe, precedence))
                        {
                            conflicts++;
                        }
                    }
                }
            }
            else if (state.Exhausted)
            {
                return candidates;
            }
            else
            {
                conflicts = int.MaxValue;
            }

            scored.Add((candidate, i, conflicts));
        }

        return scored.OrderBy(s => s.Conflicts).ThenBy(s => s.Index).Select(s => s.Candidate).ToArray();
    }
}
