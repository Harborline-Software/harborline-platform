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

    [Theory]
    [InlineData(true, 10, SolveStatus.Feasible, "ALL_HARD_CONSTRAINTS_SATISFIED", 0)]
    [InlineData(false, 10, SolveStatus.Indeterminate, "PINNED_FACTS_INCOMPLETE", 1)]
    [InlineData(true, 0, SolveStatus.Indeterminate, "WORK_BUDGET_EXHAUSTED", 1)]
    public void Exhaustive_plan_records_duration_and_literal_outcome(
        bool resourcesComplete, int workBudget, SolveStatus expectedStatus, string expectedReason, int expectedUnassigned)
    {
        using var measurements = new SchedulingMeterMeasurements();
        var problem = new FiniteCandidateCompiler().Compile(Profile(
            activities: [new("activity-1", 1)],
            timeWindows: [new("activity-1", 0, 0)],
            resources: [new("resource-1", new HashSet<string> { "capability-1" }, new HashSet<int> { 0 })],
            requirements: [new("requirement-1", "activity-1", "capability-1", 1)],
            pins:
            [
                new(PlanningFactSets.Activities, "activities-v1", true),
                new(PlanningFactSets.TimeWindows, "windows-v1", true),
                new(PlanningFactSets.Resources, "resources-v1", resourcesComplete),
                new(PlanningFactSets.ResourceRequirements, "requirements-v1", true),
                new(PlanningFactSets.Precedence, "precedence-v1", true),
            ]));

        var proposal = new DeterministicExhaustiveProofSolver().Solve(problem, workBudget);

        Assert.Equal(expectedStatus, proposal.Status);
        Assert.Equal(expectedReason, proposal.ReasonCode);
        if (expectedStatus == SolveStatus.Feasible)
        {
            var assignment = Assert.Single(proposal.Assignments);
            Assert.Equal("activity-1", assignment.ActivityId);
            Assert.Equal(0, assignment.StartSlot);
            Assert.Equal(1, assignment.EndSlotExclusive);
            Assert.Equal(["resource-1"], assignment.ResourceIds);
        }
        else
            Assert.Empty(proposal.Assignments);
        if (resourcesComplete)
            Assert.Empty(proposal.IncompleteFactSets);
        else
            Assert.Equal(["resources"], proposal.IncompleteFactSets);

        var duration = Assert.Single(measurements.ForInstrument("scheduling.plan.duration_ms"));
        Assert.True(double.IsFinite(duration.Value));
        Assert.True(duration.Value >= 0);
        Assert.Equal("prototype.exhaustive-proof.v1", duration.Tags["solver_id"]);
        var unassigned = Assert.Single(measurements.ForInstrument("scheduling.plan.unassigned"));
        Assert.Equal(expectedUnassigned, unassigned.Value);
        Assert.Equal("prototype.exhaustive-proof.v1", unassigned.Tags["solver_id"]);
        Assert.Equal(expectedReason, unassigned.Tags["reason_code"]);
    }

    [Theory]
    [InlineData(false, "prototype.greedy-first-fit.v1", SolveStatus.Partial, "GREEDY_DEAD_END", 1)]
    [InlineData(true, "prototype.exhaustive-proof.v1", SolveStatus.Feasible, "ALL_HARD_CONSTRAINTS_SATISFIED", 0)]
    public void Real_solver_duration_contains_observed_enumeration_interval_and_literal_module(
        bool exhaustive, string expectedModule, SolveStatus expectedStatus, string expectedReason, int expectedUnassigned)
    {
        // These lawful fixtures have one literal assignment. The greedy fixture has a second
        // competing activity, so it also supplies an independent unassigned-one control.
        var profile = Profile(
            activities: exhaustive ? [new("activity-1", 1)] : [new("activity-1", 1), new("activity-2", 1)],
            timeWindows: exhaustive ? [new("activity-1", 0, 0)] : [new("activity-1", 0, 0), new("activity-2", 0, 0)],
            resources: [new("resource-1", new HashSet<string> { "capability-1" }, new HashSet<int> { 0 })],
            requirements: exhaustive
                ? [new("requirement-1", "activity-1", "capability-1", 1)]
                : [new("requirement-1", "activity-1", "capability-1", 1), new("requirement-2", "activity-2", "capability-1", 1)]);
        var problem = new FiniteCandidateCompiler().Compile(profile);
        var tolerance = ClockToleranceMilliseconds;

        var first = ObserveRealSolve(problem, exhaustive, minimumHoldMilliseconds: 8 * tolerance);
        // The second target depends on an independent outer-call observation, never the metric.
        // Two disjoint valid intervals cannot both contain one fabricated finite constant.
        var second = ObserveRealSolve(problem, exhaustive, first.OuterMilliseconds + 8 * tolerance);
        Assert.True(first.OuterMilliseconds + tolerance < second.HoldMilliseconds - tolerance,
            "INCONCLUSIVE: the two observed duration intervals overlap; no timing or fault credit.");

        foreach (var observation in new[] { first, second })
        {
            Assert.Equal(expectedStatus, observation.Proposal.Status);
            Assert.Equal(expectedReason, observation.Proposal.ReasonCode);
            Assert.Empty(observation.Proposal.IncompleteFactSets);
            var assignment = Assert.Single(observation.Proposal.Assignments);
            Assert.Equal("activity-1", assignment.ActivityId);
            Assert.Equal(0, assignment.StartSlot);
            Assert.Equal(1, assignment.EndSlotExclusive);
            Assert.Equal(["resource-1"], assignment.ResourceIds);

            Assert.True(double.IsFinite(observation.Duration.Value));
            Assert.True(observation.Duration.Value >= observation.HoldMilliseconds - tolerance,
                "The real solve duration must include the independently observed enumeration hold.");
            Assert.True(observation.Duration.Value <= observation.OuterMilliseconds + tolerance,
                "The real solve duration must fit inside its independently observed public-call bracket.");
            Assert.Equal(expectedModule, observation.Duration.Tags["solver_id"]);
            Assert.Equal(expectedUnassigned, observation.Unassigned.Value);
            Assert.Equal(expectedModule, observation.Unassigned.Tags["solver_id"]);
            Assert.Equal(expectedReason, observation.Unassigned.Tags["reason_code"]);
        }
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

    // A resource bound for test coordination, never a product latency requirement.
    private static readonly TimeSpan SolveCoordinationLimit = TimeSpan.FromSeconds(10);

    // Stopwatch timestamps and its TimeSpan result have different quantization. This allowance
    // covers two timestamp ticks and one TimeSpan tick; no scheduler precision is assumed.
    private static double ClockToleranceMilliseconds =>
        2.0 * 1000 / System.Diagnostics.Stopwatch.Frequency + 1000.0 / TimeSpan.TicksPerSecond;

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static SolveObservation ObserveRealSolve(
        CompiledPlanningProblem problem, bool exhaustive, double minimumHoldMilliseconds)
    {
        Assert.True(double.IsFinite(minimumHoldMilliseconds) && minimumHoldMilliseconds > 0
            && minimumHoldMilliseconds < SolveCoordinationLimit.TotalMilliseconds,
            "INCONCLUSIVE: the independently chosen hold does not fit the finite coordination bound.");

        var activities = new ObservedActivities(problem.Activities);
        SolveObservation? observation = null;
        Exception? failure = null;
        var worker = new System.Threading.Thread(() =>
        {
            try
            {
                // Listener ownership and AsyncLocal observation remain in the actual solve context.
                using var measurements = new SchedulingMeterMeasurements();
                IPlanningSolver solver = exhaustive
                    ? new DeterministicExhaustiveProofSolver()
                    : new GreedyFirstFitSolver();
                var input = problem with { Activities = activities };
                var before = System.Diagnostics.Stopwatch.GetTimestamp();
                var proposal = solver.Solve(input, deterministicWorkBudget: 10);
                var after = System.Diagnostics.Stopwatch.GetTimestamp();
                observation = new(
                    proposal,
                    Assert.Single(measurements.ForInstrument("scheduling.plan.duration_ms")),
                    Assert.Single(measurements.ForInstrument("scheduling.plan.unassigned")),
                    ElapsedMilliseconds(activities.EnteredAt, activities.LeftAt),
                    ElapsedMilliseconds(before, after),
                    activities.EnumerationCount);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };

        var started = false;
        var joined = false;
        try
        {
            worker.Start();
            started = true;
            Assert.True(activities.Entered.Wait(SolveCoordinationLimit),
                "INCONCLUSIVE: the real solver did not reach the enumeration seam within the coordination bound.");
            Assert.True(System.Threading.SpinWait.SpinUntil(
                    () => ElapsedMilliseconds(activities.EnteredAt, System.Diagnostics.Stopwatch.GetTimestamp())
                        >= minimumHoldMilliseconds,
                    SolveCoordinationLimit),
                "INCONCLUSIVE: an independently observed positive hold could not be established.");
        }
        finally
        {
            activities.Release.Set();
            try
            {
                joined = !started || worker.Join(SolveCoordinationLimit);
            }
            finally
            {
                // Never dispose a wait handle still in use by an unterminated worker.
                if (joined) activities.Dispose();
            }
            Assert.True(joined,
                "STOP/INCONCLUSIVE: solve worker did not terminate after release; no native or fault credit.");
        }

        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        var result = observation ?? throw new InvalidOperationException("INCONCLUSIVE: the real solver produced no observation.");
        Assert.Equal(1, result.EnumerationCount);
        Assert.True(double.IsFinite(result.HoldMilliseconds)
            && result.HoldMilliseconds > 2 * ClockToleranceMilliseconds,
            "INCONCLUSIVE: the actual enumeration hold is not distinguishable from clock quantization.");
        Assert.True(double.IsFinite(result.OuterMilliseconds)
            && result.OuterMilliseconds >= result.HoldMilliseconds,
            "INCONCLUSIVE: the inner hold is not contained by the observed outer call.");
        return result;
    }

    // Deliberately implements no ICollection/IList shortcut. Only first enumeration is gated;
    // Count and the indexer delegate directly, including the producer's Count read after Stop.
    private sealed class ObservedActivities(IReadOnlyList<Activity> values) : IReadOnlyList<Activity>, IDisposable
    {
        private int enumerationCount;
        public System.Threading.ManualResetEventSlim Entered { get; } = new(false);
        public System.Threading.ManualResetEventSlim Release { get; } = new(false);
        public long EnteredAt { get; private set; }
        public long LeftAt { get; private set; }
        public int EnumerationCount => enumerationCount;
        public int Count => values.Count;
        public Activity this[int index] => values[index];

        public IEnumerator<Activity> GetEnumerator()
        {
            if (System.Threading.Interlocked.Increment(ref enumerationCount) == 1)
            {
                EnteredAt = System.Diagnostics.Stopwatch.GetTimestamp();
                Entered.Set();
                try
                {
                    if (!Release.Wait(SolveCoordinationLimit))
                        throw new InvalidOperationException("INCONCLUSIVE: enumeration gate was not released within its bound.");
                }
                finally
                {
                    LeftAt = System.Diagnostics.Stopwatch.GetTimestamp();
                }
            }
            return values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private sealed record SolveObservation(
        SchedulingProposal Proposal, Measurement Duration, Measurement Unassigned,
        double HoldMilliseconds, double OuterMilliseconds, int EnumerationCount);

    private sealed record Measurement(string InstrumentName, double Value, IReadOnlyDictionary<string, string> Tags);
}
