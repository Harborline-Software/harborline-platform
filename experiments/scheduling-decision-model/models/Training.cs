using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Engine;

namespace Harborline.Experiments.SchedulingDecisionModel.Models;

/// <summary>One activity-choice snapshot: features of every unassigned activity and its exact consistent-candidate count.</summary>
public sealed record ActivitySnapshot(string InstanceId, double[][] Features, int[] TrueCounts);

/// <summary>One explored candidate: features at ordering time, and whether its subtree reached a complete plan.</summary>
public sealed record ValueRow(string InstanceId, double[] Features, int Label);

/// <summary>
/// Records training traces from a teacher search (dynamic smallest-domain, the selected Phase 2
/// baseline). Snapshot counts are computed offline with uncharged checks; the search itself is
/// unchanged. Train split only.
/// </summary>
public sealed class TraceCollector : IOrderingPolicy, ISearchObserver
{
    private readonly DynamicDomainPolicy _teacher = new();
    private readonly string _instanceId;
    private readonly int _maxSnapshots;
    private readonly Dictionary<int, (string Activity, Dictionary<AssignmentCandidate, double[]> Features)> _frames = [];
    private FeatureContext? _context;

    public TraceCollector(string instanceId, int maxSnapshots)
    {
        _instanceId = instanceId;
        _maxSnapshots = maxSnapshots;
    }

    public List<ActivitySnapshot> Snapshots { get; } = [];

    public List<ValueRow> Values { get; } = [];

    public string Id => "trace(dom-dynamic)";

    public string SelectActivity(SearchState state)
    {
        _context ??= new FeatureContext(state);
        if (Snapshots.Count < _maxSnapshots)
        {
            var unassigned = state.Unassigned.ToArray();
            if (unassigned.Length > 1)
            {
                var features = new double[unassigned.Length][];
                var counts = new int[unassigned.Length];
                for (var i = 0; i < unassigned.Length; i++)
                {
                    features[i] = new double[FeatureContext.ActivityNames.Length];
                    _context.ActivityFeatures(state, unassigned[i], features[i]);
                    counts[i] = state.Candidates[unassigned[i]].Count(c =>
                        Consistency.CanAdd(c, state.Assigned, state.Problem.Precedence));
                }

                Snapshots.Add(new ActivitySnapshot(_instanceId, features, counts));
            }
        }

        return _teacher.SelectActivity(state);
    }

    public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates)
    {
        _context ??= new FeatureContext(state);
        if (Values.Count < _maxSnapshots * 20 && candidates.Count > 1)
        {
            var earliest = candidates.Min(c => c.StartSlot);
            var features = new Dictionary<AssignmentCandidate, double[]>(ReferenceEqualityComparer.Instance);
            foreach (var c in candidates)
            {
                var x = new double[FeatureContext.CandidateNames.Length];
                _context.CandidateFeatures(state, activity, c, earliest, x);
                features[c] = x;
            }

            _frames[state.Assigned.Count] = (activity, features);
        }
        else
        {
            _frames.Remove(state.Assigned.Count);
        }

        return _teacher.OrderValues(state, activity, candidates);
    }

    public void Enter(SearchState state, string activity, AssignmentCandidate candidate)
    {
    }

    public void Leave(SearchState state, string activity, AssignmentCandidate candidate, bool succeeded, bool budgetExhausted)
    {
        if (budgetExhausted)
        {
            return; // An unfinished subtree is unknown, never a negative label.
        }

        var depth = succeeded ? state.Assigned.Count - 1 : state.Assigned.Count;
        if (_frames.TryGetValue(depth, out var frame) && frame.Activity == activity && frame.Features.TryGetValue(candidate, out var x))
        {
            Values.Add(new ValueRow(_instanceId, x, succeeded ? 1 : 0));
        }
    }

    public static TraceCollector Collect(string instanceId, CompiledPlanningProblem problem, long budget, int maxSnapshots)
    {
        var collector = new TraceCollector(instanceId, maxSnapshots);
        new SearchEngine().Run(problem, collector, budget, dedupe: true, observer: collector);
        return collector;
    }
}

public static class ModelTrainer
{
    /// <summary>
    /// Activity model: pairwise logistic on feature differences between the activity with the
    /// fewest consistent candidates (ties: list order) and up to <paramref name="pairsPerSnapshot"/> others.
    /// </summary>
    public static LinearModel TrainActivity(IReadOnlyList<ActivitySnapshot> snapshots, double l2, int pairsPerSnapshot, ulong seed, string trainedOn)
    {
        var rows = new List<double[]>();
        var labels = new List<int>();
        var state = seed;
        ulong Next()
        {
            var z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        foreach (var s in snapshots)
        {
            var best = 0;
            for (var i = 1; i < s.TrueCounts.Length; i++)
            {
                if (s.TrueCounts[i] < s.TrueCounts[best])
                {
                    best = i;
                }
            }

            var others = Enumerable.Range(0, s.TrueCounts.Length).Where(i => s.TrueCounts[i] > s.TrueCounts[best]).ToArray();
            foreach (var o in others.Select(i => (i, Key: Next())).OrderBy(x => x.Key).Select(x => x.i).Take(pairsPerSnapshot))
            {
                var diff = s.Features[best].Zip(s.Features[o], (a, b) => a - b).ToArray();
                rows.Add(diff);
                labels.Add(1);
                rows.Add(diff.Select(v => -v).ToArray());
                labels.Add(0);
            }
        }

        var (mean, std, w, b) = LogisticTrainer.Fit(rows, labels, l2, fitBias: false);
        return new LinearModel("activity", FeatureContext.Version, FeatureContext.ActivityNames, mean, std, w, b, trainedOn, l2);
    }

    public static LinearModel TrainValue(IReadOnlyList<ValueRow> values, double l2, string trainedOn)
    {
        var (mean, std, w, b) = LogisticTrainer.Fit(values.Select(v => v.Features).ToArray(), values.Select(v => v.Label).ToArray(), l2, fitBias: true);
        return new LinearModel("value", FeatureContext.Version, FeatureContext.CandidateNames, mean, std, w, b, trainedOn, l2);
    }
}
