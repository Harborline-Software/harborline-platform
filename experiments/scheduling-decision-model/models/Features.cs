using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Engine;

namespace Harborline.Experiments.SchedulingDecisionModel.Models;

/// <summary>
/// Feature contract <c>sdm-features.v1</c>. Decision-time data only: the compiled problem's
/// structure and the current partial assignment. No labels, outcomes, solutions or identities.
/// No feature performs a consistency check; all are bounded arithmetic, scaled to roughly [-1, 1].
/// </summary>
public sealed class FeatureContext
{
    public const string Version = "sdm-features.v1";

    public static readonly string[] ActivityNames =
    [
        "log-domain", "window-slack", "effective-slack", "duration", "prec-assigned-frac",
        "prec-pred-unassigned", "res-neigh-assigned", "res-neigh-unassigned", "resources-needed",
        "mean-resource-demand", "domain-in-effective-window",
    ];

    public static readonly string[] CandidateNames =
    [
        "rel-start-in-window", "resource-demand", "resources", "successor-room", "pred-gap",
        "resource-busy", "is-earliest",
    ];

    private readonly Dictionary<string, Info> _info = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _demand = new(StringComparer.Ordinal);
    private readonly int _n;
    private readonly double _horizon;
    private readonly double _maxDuration;
    private readonly double _maxResources;
    private readonly double _logMaxDomain;

    private sealed record Info(
        int Earliest,
        int Latest,
        int Duration,
        int Domain,
        int ResourcesPerCandidate,
        (string Other, int Gap)[] Preds,
        (string Other, int Gap)[] Succs,
        string[] ResourceNeighbours,
        string[] Resources);

    /// <summary>Operation count of building this context, used to charge it: Σ candidates + n·(n + |precedence|).</summary>
    public long BuildOperations { get; }

    public FeatureContext(SearchState state)
    {
        var problem = state.Problem;
        _n = problem.Activities.Count;
        foreach (var (activity, cands) in state.Candidates)
        {
            foreach (var r in cands.SelectMany(c => c.ResourceIds).Distinct(StringComparer.Ordinal))
            {
                _demand[r] = _demand.GetValueOrDefault(r) + 1;
            }
        }

        foreach (var a in problem.Activities)
        {
            var cands = state.Candidates[a.Id];
            var resources = cands.SelectMany(c => c.ResourceIds).Distinct(StringComparer.Ordinal).ToArray();
            var precNeighbours = problem.Precedence
                .Where(p => p.BeforeActivityId == a.Id || p.AfterActivityId == a.Id)
                .Select(p => p.BeforeActivityId == a.Id ? p.AfterActivityId : p.BeforeActivityId)
                .ToHashSet(StringComparer.Ordinal);
            _info[a.Id] = new Info(
                cands.Count == 0 ? 0 : cands.Min(c => c.StartSlot),
                cands.Count == 0 ? 0 : cands.Max(c => c.StartSlot),
                a.DurationSlots,
                cands.Count,
                cands.Count == 0 ? 0 : cands[0].ResourceIds.Count,
                problem.Precedence.Where(p => p.AfterActivityId == a.Id).Select(p => (p.BeforeActivityId, p.MinimumGapSlots)).ToArray(),
                problem.Precedence.Where(p => p.BeforeActivityId == a.Id).Select(p => (p.AfterActivityId, p.MinimumGapSlots)).ToArray(),
                state.Neighbours[a.Id].Where(x => !precNeighbours.Contains(x)).ToArray(),
                resources);
        }

        _horizon = 1 + state.Candidates.Values.SelectMany(c => c).Select(c => c.EndSlotExclusive).DefaultIfEmpty(0).Max();
        _maxDuration = 1 + problem.Activities.Max(a => a.DurationSlots);
        _maxResources = 1 + _info.Values.Max(i => i.ResourcesPerCandidate);
        _logMaxDomain = Math.Log(2 + _info.Values.Max(i => i.Domain));
        BuildOperations = state.Candidates.Values.Sum(c => (long)c.Count) + ((long)_n * (_n + problem.Precedence.Count));
    }

    /// <summary>Effective start bounds given assigned predecessors and successors.</summary>
    private (int Earliest, int Latest) Effective(SearchState state, Info info)
    {
        var earliest = info.Earliest;
        foreach (var (pred, gap) in info.Preds)
        {
            if (state.Assigned.TryGetValue(pred, out var p))
            {
                earliest = Math.Max(earliest, p.EndSlotExclusive + gap);
            }
        }

        var latest = info.Latest;
        foreach (var (succ, gap) in info.Succs)
        {
            if (state.Assigned.TryGetValue(succ, out var s))
            {
                latest = Math.Min(latest, s.StartSlot - gap - info.Duration);
            }
        }

        return (earliest, latest);
    }

    public void ActivityFeatures(SearchState state, string activity, Span<double> x)
    {
        var info = _info[activity];
        var (eff, efl) = Effective(state, info);
        var precAssigned = info.Preds.Count(p => state.Assigned.ContainsKey(p.Other)) + info.Succs.Count(s => state.Assigned.ContainsKey(s.Other));
        var precDegree = info.Preds.Length + info.Succs.Length;
        var resAssigned = info.ResourceNeighbours.Count(state.Assigned.ContainsKey);
        var inWindow = 0;
        foreach (var c in state.Candidates[activity])
        {
            if (c.StartSlot >= eff && c.StartSlot <= efl)
            {
                inWindow++;
            }
        }

        x[0] = Math.Log(1 + info.Domain) / _logMaxDomain;
        x[1] = (info.Latest - info.Earliest) / _horizon;
        x[2] = Math.Clamp((efl - eff) / _horizon, -1, 1);
        x[3] = info.Duration / _maxDuration;
        x[4] = precDegree == 0 ? 0 : (double)precAssigned / precDegree;
        x[5] = info.Preds.Count(p => !state.Assigned.ContainsKey(p.Other)) / (1.0 + _n);
        x[6] = resAssigned / (1.0 + _n);
        x[7] = (info.ResourceNeighbours.Length - resAssigned) / (1.0 + _n);
        x[8] = info.ResourcesPerCandidate / _maxResources;
        x[9] = info.Resources.Length == 0 ? 0 : info.Resources.Average(r => _demand[r]) / (1.0 + _n);
        x[10] = info.Domain == 0 ? 0 : (double)inWindow / info.Domain;
    }

    public void CandidateFeatures(SearchState state, string activity, AssignmentCandidate c, int earliestStart, Span<double> x)
    {
        var info = _info[activity];
        var (eff, efl) = Effective(state, info);
        var room = 1.0;
        foreach (var (succ, gap) in info.Succs)
        {
            if (!state.Assigned.ContainsKey(succ))
            {
                room = Math.Min(room, (_info[succ].Latest - (c.EndSlotExclusive + gap)) / _horizon);
            }
        }

        var busy = 0;
        foreach (var placed in state.Assigned.Values)
        {
            foreach (var r in c.ResourceIds)
            {
                if (placed.ResourceIds.Contains(r))
                {
                    busy++;
                }
            }
        }

        x[0] = Math.Clamp((c.StartSlot - eff) / (1.0 + Math.Max(0, efl - eff)), -1, 1);
        x[1] = c.ResourceIds.Count == 0 ? 0 : c.ResourceIds.Average(r => _demand[r]) / (1.0 + _n);
        x[2] = c.ResourceIds.Count / _maxResources;
        x[3] = Math.Clamp(room, -1, 1);
        x[4] = Math.Clamp((c.StartSlot - eff) / _horizon, -1, 1);
        x[5] = busy / (1.0 + _n);
        x[6] = c.StartSlot == earliestStart ? 1 : 0;
    }
}
