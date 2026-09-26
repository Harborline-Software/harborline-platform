using System.Diagnostics.Metrics;
using Harborline.Blocks.Scheduling.Durable;
using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class SchedulingTelemetryTests
{
    [Fact]
    public void Invalid_profile_records_one_admission_refusal_with_a_reason_code()
    {
        using var measurements = new SchedulingMeterMeasurements();
        var profile = Profile(
            activities: [new("activity-1", 1)],
            timeWindows: [new("activity-1", 0, 0)],
            resources: [new("resource-1", new HashSet<string> { "capability-1" }, new HashSet<int> { 0 })],
            requirements: [new("requirement-1", "activity-1", "capability-1", 1)],
            pins:
            [
                new(PlanningFactSets.Activities, "activities-v1", true),
                new(PlanningFactSets.Activities, "activities-v2", true),
            ]);

        Assert.Throws<UnsupportedProfileException>(() => new FiniteCandidateCompiler().Compile(profile));

        var refusal = Assert.Single(measurements.ForInstrument("scheduling.admission.refusals"));
        Assert.Equal(1, refusal.Value);
        Assert.Equal("duplicate_fact_set_pin", refusal.Tags["reason"]);
    }

    [Fact]
    public void Greedy_partial_plan_records_duration_and_nonzero_unassigned_outcome()
    {
        using var measurements = new SchedulingMeterMeasurements();
        var solver = new GreedyFirstFitSolver();
        var problem = new FiniteCandidateCompiler().Compile(Profile(
            activities: [new("activity-1", 1), new("activity-2", 1)],
            timeWindows: [new("activity-1", 0, 0), new("activity-2", 0, 0)],
            resources: [new("resource-1", new HashSet<string> { "capability-1" }, new HashSet<int> { 0 })],
            requirements:
            [
                new("requirement-1", "activity-1", "capability-1", 1),
                new("requirement-2", "activity-2", "capability-1", 1),
            ]));

        var proposal = solver.Solve(problem, deterministicWorkBudget: 10);

        var duration = Assert.Single(measurements.ForInstrument("scheduling.plan.duration_ms"));
        Assert.Equal(solver.SolverId, duration.Tags["solver_id"]);
        var unassigned = Assert.Single(measurements.ForInstrument("scheduling.plan.unassigned"));
        Assert.Equal(problem.Activities.Count - proposal.Assignments.Count, unassigned.Value);
        Assert.Equal(solver.SolverId, unassigned.Tags["solver_id"]);
        Assert.Equal(proposal.ReasonCode, unassigned.Tags["reason_code"]);
        Assert.True(unassigned.Value > 0);
    }

    [Fact]
    public void Greedy_feasible_plan_records_zero_unassigned_outcome()
    {
        using var measurements = new SchedulingMeterMeasurements();
        var solver = new GreedyFirstFitSolver();
        var problem = new FiniteCandidateCompiler().Compile(Profile(
            activities: [new("activity-1", 1)],
            timeWindows: [new("activity-1", 0, 0)],
            resources: [new("resource-1", new HashSet<string> { "capability-1" }, new HashSet<int> { 0 })],
            requirements: [new("requirement-1", "activity-1", "capability-1", 1)]));

        var proposal = solver.Solve(problem, deterministicWorkBudget: 10);

        var unassigned = Assert.Single(measurements.ForInstrument("scheduling.plan.unassigned"));
        Assert.Equal(0, unassigned.Value);
        Assert.Equal(solver.SolverId, unassigned.Tags["solver_id"]);
        Assert.Equal(proposal.ReasonCode, unassigned.Tags["reason_code"]);
    }

    [Fact]
    public async Task Stale_draft_save_records_one_commit_refusal()
    {
        using var measurements = new SchedulingMeterMeasurements();
        var directory = Path.Combine(Path.GetTempPath(), "hl-scheduling-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new FileJournalSchedulingStore(new() { JournalPath = Path.Combine(directory, "scheduling.journal") });
            Assert.True((await store.SaveDraftAsync("tenant-a", "definition-1", 0, "{}", "actor")).Saved);
            Assert.False((await store.SaveDraftAsync("tenant-a", "definition-1", 0, "{}", "actor")).Saved);

            var refusal = Assert.Single(measurements.ForInstrument("scheduling.commit.stale_refusals"));
            Assert.Equal(1, refusal.Value);
            Assert.Equal("changed_facts", refusal.Tags["reason"]);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static SchedulingProfile Profile(
        IReadOnlyList<Activity> activities,
        IReadOnlyList<TimeWindow> timeWindows,
        IReadOnlyList<PlanningResource> resources,
        IReadOnlyList<ResourceRequirement> requirements,
        IReadOnlyList<PlanningFactSetPin>? pins = null) =>
        new(
            "profile-1",
            "input-v1",
            activities,
            timeWindows,
            resources,
            requirements,
            [],
            pins ??
            [
                new(PlanningFactSets.Activities, "activities-v1", true),
                new(PlanningFactSets.TimeWindows, "windows-v1", true),
                new(PlanningFactSets.Resources, "resources-v1", true),
                new(PlanningFactSets.ResourceRequirements, "requirements-v1", true),
                new(PlanningFactSets.Precedence, "precedence-v1", true),
            ]);

    private sealed class SchedulingMeterMeasurements : IDisposable
    {
        private static readonly System.Threading.AsyncLocal<SchedulingMeterMeasurements?> Active = new();
        private readonly MeterListener listener = new();
        private readonly ConcurrentQueue<Measurement> measurements = new();

        public SchedulingMeterMeasurements()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Harborline.Scheduling") meterListener.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Capture(instrument, value, tags));
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Capture(instrument, value, tags));
            listener.Start();
            Active.Value = this;
        }

        public Measurement[] ForInstrument(string name) =>
            measurements.Where(measurement => measurement.InstrumentName == name).ToArray();

        public void Dispose()
        {
            if (ReferenceEquals(Active.Value, this)) Active.Value = null;
            listener.Dispose();
        }

        private void Capture(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (!ReferenceEquals(Active.Value, this)) return;
            var tagValues = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in tags) tagValues[tag.Key] = tag.Value?.ToString() ?? string.Empty;
            measurements.Enqueue(new(instrument.Name, value, tagValues));
        }
    }

    private sealed record Measurement(string InstrumentName, double Value, IReadOnlyDictionary<string, string> Tags);
}
