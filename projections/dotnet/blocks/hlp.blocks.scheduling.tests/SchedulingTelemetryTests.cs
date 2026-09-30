using System.Diagnostics.Metrics;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;
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
        Assert.Equal("invalid_fact_set_pin", refusal.Tags["reason"]);
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

        // Both activities compete for the single resource-1 candidate in the same slot, so the
        // greedy solver deterministically assigns activity-1 and dead-ends on activity-2. These are
        // concrete expectations from the fixture shape, not re-derived from the proposal under test,
        // so a wrong solver outcome (and a telemetry value that merely mirrors it) cannot both pass.
        Assert.Equal(SolveStatus.Partial, proposal.Status);
        Assert.Equal("GREEDY_DEAD_END", proposal.ReasonCode);
        Assert.Single(proposal.Assignments);

        var duration = Assert.Single(measurements.ForInstrument("scheduling.plan.duration_ms"));
        Assert.Equal(solver.SolverId, duration.Tags["solver_id"]);
        var unassigned = Assert.Single(measurements.ForInstrument("scheduling.plan.unassigned"));
        Assert.Equal(1, unassigned.Value);
        Assert.Equal(solver.SolverId, unassigned.Tags["solver_id"]);
        Assert.Equal("GREEDY_DEAD_END", unassigned.Tags["reason_code"]);
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
    public async Task Stale_definition_draft_save_records_one_commit_refusal()
    {
        using var measurements = new SchedulingMeterMeasurements();
        var catalogue = new ScheduleDefinitionCatalogue(new InMemoryVersionedDefinitionStore(
            new Dictionary<DefinitionKind, DefinitionAdmission> { [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission }));
        await catalogue.SaveDraftAsync("tenant-a", "definition-1", "1.0.0", "{}", 0, "first");
        var stale = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.SaveDraftAsync("tenant-a", "definition-1", "1.0.1", "{}", 0, "second").AsTask());
        Assert.Equal([new DefinitionRefusal("definition.revision_conflict", "/expectedRevision")], stale.Refusals);

        var refusal = Assert.Single(measurements.ForInstrument("scheduling.commit.stale_refusals"));
        Assert.Equal(1, refusal.Value);
        Assert.Equal("changed_facts", refusal.Tags["reason"]);
    }

    [Fact]
    public async Task Other_definition_refusals_record_no_commit_refusal()
    {
        using var measurements = new SchedulingMeterMeasurements();
        var catalogue = new ScheduleDefinitionCatalogue(new InMemoryVersionedDefinitionStore(
            new Dictionary<DefinitionKind, DefinitionAdmission> { [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission }));
        await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.SaveDraftAsync("tenant-a", "definition-1", "3", "{}", 0, "first").AsTask());
        Assert.Empty(measurements.ForInstrument("scheduling.commit.stale_refusals"));
    }

    [Fact]
    public async Task A_refusal_naming_no_fence_conflict_records_no_commit_refusal()
    {
        using var measurements = new SchedulingMeterMeasurements();
        var catalogue = new ScheduleDefinitionCatalogue(new RefusingStore());
        await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.SaveDraftAsync("tenant-a", "definition-1", "1.0.0", "{}", 0, "first").AsTask());
        Assert.Empty(measurements.ForInstrument("scheduling.commit.stale_refusals"));
    }

    // A store whose draft save refuses with no reasons: nothing in it names a stale fence.
    private sealed class RefusingStore : IVersionedDefinitionStore
    {
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId,
            CancellationToken cancellationToken = default) => throw new DefinitionRefusalException(DefinitionAdmissionPhase.Author, []);
        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
