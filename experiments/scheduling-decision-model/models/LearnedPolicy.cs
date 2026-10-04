using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Engine;

namespace Harborline.Experiments.SchedulingDecisionModel.Models;

/// <summary>
/// Learned ordering. Activity choice: score every unassigned activity with the activity model,
/// shortlist the top <c>k</c>, then pick the shortlisted activity with the fewest consistent
/// candidates (exact, charged checks); k = 1 skips the count. Candidate order: descending
/// value-model score, ties in compiler order. Every scored item is charged its measured cost.
/// With no activity model, shortlist &lt; 0 selects by full dynamic smallest-domain (the Phase 2
/// baseline), otherwise the incumbent order. A non-finite score throws, so the engine falls back to the incumbent order and counts it.
/// </summary>
public sealed class LearnedPolicy(
    LinearModel? activityModel,
    LinearModel? valueModel,
    int shortlist,
    long chargePerActivity,
    long chargePerCandidate,
    double contextChargePerOperation = 0) : IOrderingPolicy
{
    private readonly IncumbentPolicy _incumbent = new();
    private readonly DynamicDomainPolicy _dynamic = new();
    private FeatureContext? _context;
    private SearchState? _contextFor;
    private readonly double[] _ax = new double[FeatureContext.ActivityNames.Length];
    private readonly double[] _cx = new double[FeatureContext.CandidateNames.Length];

    /// <summary>Diagnostics for charge calibration: items scored and Stopwatch ticks spent on features + scoring.</summary>
    public long ActivitiesScored { get; private set; }

    public long CandidatesScored { get; private set; }

    public long ActivityTicks { get; private set; }

    public long CandidateTicks { get; private set; }

    public string Id => $"learned(a={(activityModel is null ? "-" : "on")},v={(valueModel is null ? "-" : "on")},k={shortlist})";

    /// <summary>Ticks spent building feature contexts (diagnostics for charge calibration).</summary>
    public long ContextTicks { get; private set; }

    public long ContextOperations { get; private set; }

    private FeatureContext Context(SearchState state)
    {
        if (!ReferenceEquals(_contextFor, state))
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _context = new FeatureContext(state);
            ContextTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            ContextOperations += _context.BuildOperations;
            _contextFor = state;

            // The one-off build is charged too, deterministically from its operation count.
            state.Charge((long)Math.Ceiling(_context.BuildOperations * contextChargePerOperation));
        }

        return _context!;
    }

    public string SelectActivity(SearchState state)
    {
        if (activityModel is null)
        {
            return shortlist < 0 ? _dynamic.SelectActivity(state) : _incumbent.SelectActivity(state);
        }

        var context = Context(state);
        var scored = new List<(string Id, double Score)>();
        foreach (var activity in state.Unassigned)
        {
            if (!state.Charge(chargePerActivity))
            {
                return activity;
            }

            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            context.ActivityFeatures(state, activity, _ax);
            var s = activityModel.Score(_ax);
            ActivityTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            ActivitiesScored++;
            if (!double.IsFinite(s))
            {
                throw new InvalidOperationException("NON_FINITE_ACTIVITY_SCORE");
            }

            scored.Add((activity, s));
        }

        var ranked = scored.OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Id).ToArray();
        if (shortlist <= 1)
        {
            return ranked[0];
        }

        string? best = null;
        var bestCount = int.MaxValue;
        foreach (var activity in ranked.Take(shortlist))
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

    public IReadOnlyList<AssignmentCandidate> OrderValues(SearchState state, string activity, IReadOnlyList<AssignmentCandidate> candidates)
    {
        if (valueModel is null || candidates.Count <= 1)
        {
            return candidates;
        }

        var context = Context(state);
        var earliest = candidates.Min(c => c.StartSlot);
        var scored = new (AssignmentCandidate Candidate, int Index, double Score)[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!state.Charge(chargePerCandidate))
            {
                return candidates;
            }

            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            context.CandidateFeatures(state, activity, candidates[i], earliest, _cx);
            var s = valueModel.Score(_cx);
            CandidateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            CandidatesScored++;
            if (!double.IsFinite(s))
            {
                throw new InvalidOperationException("NON_FINITE_CANDIDATE_SCORE");
            }

            scored[i] = (candidates[i], i, s);
        }

        return scored.OrderByDescending(x => x.Score).ThenBy(x => x.Index).Select(x => x.Candidate).ToArray();
    }
}
