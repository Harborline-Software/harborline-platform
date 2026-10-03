using Google.OrTools.Sat;
using Google.OrTools.Util;
using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Baselines;

public enum CpSatOutcome
{
    Feasible,
    Infeasible,
    Unknown,
    ModelInvalid,
}

public sealed record CpSatResult(
    CpSatOutcome Outcome,
    IReadOnlyList<AssignmentCandidate> Plan,
    double WallSeconds,
    double DeterministicTime,
    long Branches,
    long Conflicts,
    string SolverStatus);

/// <summary>
/// CP-SAT model of profile <c>sdm.unit-resource.v1</c> (docs/profile-spec.md), written from the
/// spec with interval variables. It shares no code with the production compiler, solvers or the oracle.
/// </summary>
public sealed class CpSatFeasibility
{
    /// <param name="profile">Profile to solve.</param>
    /// <param name="maxSeconds">Wall-clock limit.</param>
    /// <param name="maxDeterministicTime">Deterministic-time limit; null for none.</param>
    /// <param name="seed">Random seed recorded with the run.</param>
    public CpSatResult Solve(SchedulingProfile profile, double maxSeconds = 10, double? maxDeterministicTime = null, int seed = 0)
    {
        var model = new CpModel();
        var starts = new Dictionary<string, IntVar>(StringComparer.Ordinal);
        var uses = new Dictionary<(string Activity, string Resource), BoolVar>();
        var intervalsByResource = profile.Resources.ToDictionary(r => r.Id, _ => new List<IntervalVar>(), StringComparer.Ordinal);

        foreach (var activity in profile.Activities)
        {
            var window = profile.TimeWindows.Single(w => w.ActivityId == activity.Id);
            var start = model.NewIntVar(window.EarliestStartSlot, window.LatestStartSlot, $"start[{activity.Id}]");
            starts[activity.Id] = start;
            var requirements = profile.ResourceRequirements.Where(q => q.ActivityId == activity.Id).ToArray();

            // seat[q, r]: resource r fills one unit of requirement q.
            var seatsByResource = new Dictionary<string, List<BoolVar>>(StringComparer.Ordinal);
            foreach (var requirement in requirements)
            {
                var seats = new List<BoolVar>();
                foreach (var resource in profile.Resources.Where(r => r.Capabilities.Contains(requirement.Capability)))
                {
                    var seat = model.NewBoolVar($"seat[{requirement.Id},{resource.Id}]");
                    seats.Add(seat);
                    if (!seatsByResource.TryGetValue(resource.Id, out var list))
                    {
                        seatsByResource[resource.Id] = list = [];
                    }

                    list.Add(seat);
                }

                if (seats.Count == 0)
                {
                    // No resource has the capability: the requirement can never be met.
                    var never = model.NewBoolVar($"never[{requirement.Id}]");
                    model.Add(never == 1);
                    model.Add(never == 0);
                }
                else
                {
                    model.Add(LinearExpr.Sum(seats) == requirement.Quantity);
                }
            }

            foreach (var (resourceId, seats) in seatsByResource)
            {
                // A resource fills at most one seat of this activity; "used" is that sum.
                var used = model.NewBoolVar($"use[{activity.Id},{resourceId}]");
                model.Add(LinearExpr.Sum(seats) == used);
                uses[(activity.Id, resourceId)] = used;

                var resource = profile.Resources.Single(r => r.Id == resourceId);
                var allowedStarts = Enumerable
                    .Range(window.EarliestStartSlot, window.LatestStartSlot - window.EarliestStartSlot + 1)
                    .Where(s => Enumerable.Range(s, activity.DurationSlots).All(resource.AvailableSlots.Contains))
                    .Select(s => (long)s)
                    .ToArray();
                if (allowedStarts.Length == 0)
                {
                    model.Add(used == 0);
                }
                else
                {
                    model.AddLinearExpressionInDomain(start, Domain.FromValues(allowedStarts)).OnlyEnforceIf(used);
                }

                intervalsByResource[resourceId].Add(model.NewOptionalFixedSizeIntervalVar(
                    start, activity.DurationSlots, used, $"iv[{activity.Id},{resourceId}]"));
            }
        }

        foreach (var intervals in intervalsByResource.Values.Where(list => list.Count > 1))
        {
            model.AddNoOverlap(intervals);
        }

        var durations = profile.Activities.ToDictionary(a => a.Id, a => a.DurationSlots, StringComparer.Ordinal);
        foreach (var c in profile.Precedence)
        {
            model.Add(starts[c.BeforeActivityId] + durations[c.BeforeActivityId] + c.MinimumGapSlots <= starts[c.AfterActivityId]);
        }

        var solver = new CpSolver
        {
            StringParameters = string.Join(' ', new[]
            {
                "num_workers:1",
                $"random_seed:{seed}",
                $"max_time_in_seconds:{maxSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                maxDeterministicTime is { } dt
                    ? $"max_deterministic_time:{dt.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                    : string.Empty,
            }.Where(p => p.Length > 0)),
        };

        var status = solver.Solve(model);
        var outcome = status switch
        {
            CpSolverStatus.Feasible or CpSolverStatus.Optimal => CpSatOutcome.Feasible,
            CpSolverStatus.Infeasible => CpSatOutcome.Infeasible,
            CpSolverStatus.ModelInvalid => CpSatOutcome.ModelInvalid,
            _ => CpSatOutcome.Unknown,
        };

        IReadOnlyList<AssignmentCandidate> plan = outcome == CpSatOutcome.Feasible
            ? profile.Activities
                .OrderBy(a => a.Id, StringComparer.Ordinal)
                .Select(a =>
                {
                    var s = (int)solver.Value(starts[a.Id]);
                    var resources = uses
                        .Where(u => u.Key.Activity == a.Id && solver.BooleanValue(u.Value))
                        .Select(u => u.Key.Resource)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    return new AssignmentCandidate(a.Id, s, s + a.DurationSlots, resources);
                })
                .ToArray()
            : [];

        return new CpSatResult(
            outcome,
            plan,
            solver.WallTime(),
            solver.Response?.DeterministicTime ?? 0,
            solver.NumBranches(),
            solver.NumConflicts(),
            status.ToString());
    }
}
