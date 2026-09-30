using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ExecutionRuntime;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class ExecutionRuntimeTests
{
    private static readonly TenantId Tenant = new("tenant-a");
    private static readonly TenantId OtherTenant = new("tenant-b");
    private static readonly RunKind WorkflowRun = new("workflow-run");
    private static readonly RunKind PlanRun = new("plan-run");

    private static readonly CapabilityRetryPolicy ExternalApi = new(
        RetryProfileName.ExternalApiStandard,
        [RetryProfileName.ExternalApiStandard, RetryProfileName.ImmediateOrFail]);

    // ---- red first: the four refusals T-528 slice 1 exists for ----

    [Fact(DisplayName = "T-528 S1 ck-1: an illegal status transition is refused and the stored run does not move")]
    public async Task Illegal_status_transition_is_refused()
    {
        var (lifecycle, store, _) = Build();
        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));

        // pending -> succeeded skips the attempt.
        var skipped = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => lifecycle.SucceedAsync(Tenant, run.Id).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.TransitionIllegal, skipped.Code);
        Assert.Equal(RunStatus.Pending, (await store.GetAsync(Tenant, run.Id))!.Status);

        // pending -> dead-lettered skips the attempt too.
        var failedUnstarted = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("boom", Retryable: false)).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.TransitionIllegal, failedUnstarted.Code);

        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        await lifecycle.SucceedAsync(Tenant, run.Id);

        // A terminal run never leaves its terminal status.
        foreach (var act in new Func<Task>[]
        {
            () => lifecycle.BeginAttemptAsync(Tenant, run.Id).AsTask(),
            () => lifecycle.SucceedAsync(Tenant, run.Id).AsTask(),
            () => lifecycle.CancelAsync(Tenant, run.Id).AsTask(),
            () => lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("late", Retryable: true)).AsTask(),
        })
        {
            var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(act);
            Assert.Equal(ExecutionRuntimeRefusals.TransitionIllegal, refused.Code);
        }

        var stored = (await store.GetAsync(Tenant, run.Id))!;
        Assert.Equal(RunStatus.Succeeded, stored.Status);
        Assert.Single(stored.Attempts);
    }

    [Fact(DisplayName = "T-528 S1 ck-8: a retry profile outside the capability's allowed set is refused, and a raw retry parameter is not a profile")]
    public async Task Retry_outside_allowed_profiles_is_refused()
    {
        var (lifecycle, store, _) = Build();

        var notAllowed = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => lifecycle.StartAsync(
            new StartRun(WorkflowRun, Tenant, ExternalApi, RetryProfileName.DurableDelivery)).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.RetryProfileNotAllowed, notAllowed.Code);
        Assert.Empty(await store.ListByStatusAsync(Tenant, RunStatus.Pending));

        foreach (var raw in new[] { "max-attempts=5", "backoff:2s", "3", "", "External-Api-Standard" })
        {
            var unknown = Assert.Throws<ExecutionRuntimeRefusedException>(() => RetryProfileName.Parse(raw));
            Assert.Equal(ExecutionRuntimeRefusals.RetryProfileUnknown, unknown.Code);
        }

        var unknownDefault = Assert.Throws<ExecutionRuntimeRefusedException>(() => RetryProfiles.Get(default));
        Assert.Equal(ExecutionRuntimeRefusals.RetryProfileUnknown, unknownDefault.Code);

        var defaultOutsideAllowed = Assert.Throws<ExecutionRuntimeRefusedException>(() => new CapabilityRetryPolicy(
            RetryProfileName.DurableDelivery, [RetryProfileName.ImmediateOrFail]));
        Assert.Equal(ExecutionRuntimeRefusals.RetryProfileNotAllowed, defaultOutsideAllowed.Code);

        // The allowed counterparts start: an explicit allowed selection, and the capability default.
        var selected = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi, RetryProfileName.ImmediateOrFail));
        Assert.Equal(RetryProfileName.ImmediateOrFail, selected.RetryProfile);
        var defaulted = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        Assert.Equal(RetryProfileName.ExternalApiStandard, defaulted.RetryProfile);
        Assert.Equal(RetryProfileName.DurableDelivery, RetryProfileName.Parse("durable-delivery"));
    }

    [Fact(DisplayName = "T-528 S1: exhausting the retry profile dead-letters the run into the one dead-letter path")]
    public async Task Exhaustion_dead_letters()
    {
        var (lifecycle, _, clock) = Build();
        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        var failure = new RunFailure("upstream.timeout", Retryable: true);

        // external-api-standard: three attempts, waiting 2s then 4s between them.
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        var first = await lifecycle.FailAttemptAsync(Tenant, run.Id, failure);
        Assert.Equal(RunStatus.AwaitingRetry, first.Status);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(2), first.NextAttemptDueUtc);

        var early = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => lifecycle.BeginAttemptAsync(Tenant, run.Id).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.RetryNotDue, early.Code);

        clock.Advance(TimeSpan.FromSeconds(2));
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        var second = await lifecycle.FailAttemptAsync(Tenant, run.Id, failure);
        Assert.Equal(RunStatus.AwaitingRetry, second.Status);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(4), second.NextAttemptDueUtc);
        Assert.Empty(await lifecycle.DeadLetteredAsync(Tenant));

        clock.Advance(TimeSpan.FromSeconds(4));
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        var exhausted = await lifecycle.FailAttemptAsync(Tenant, run.Id, failure);

        Assert.Equal(RunStatus.DeadLettered, exhausted.Status);
        Assert.Equal(3, exhausted.Attempts.Count);
        Assert.Equal([1, 2, 3], exhausted.Attempts.Select(attempt => attempt.Number));
        Assert.All(exhausted.Attempts, attempt => Assert.Equal(failure, attempt.Failure));
        Assert.Null(exhausted.NextAttemptDueUtc);
        Assert.Equal(new DeadLetter(DeadLetterReason.RetriesExhausted, failure, clock.GetUtcNow()), exhausted.DeadLetter);
        Assert.Equal([run.Id], (await lifecycle.DeadLetteredAsync(Tenant)).Select(dead => dead.Id));
        Assert.Empty(await lifecycle.DeadLetteredAsync(OtherTenant));
    }

    [Fact(DisplayName = "T-528 S1 cc-5: two run kinds never share an identity; caused_by correlates without merging")]
    public async Task Two_run_kinds_never_share_an_identity()
    {
        var (lifecycle, store, _) = Build();
        var workflow = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        var plan = await lifecycle.StartAsync(new StartRun(PlanRun, Tenant, ExternalApi, CausedBy: workflow.Id));

        Assert.Equal(WorkflowRun, workflow.Id.Kind);
        Assert.Equal(PlanRun, plan.Id.Kind);
        Assert.NotEqual(workflow.Id, plan.Id);
        Assert.Equal(workflow.Id, plan.CausedBy);
        Assert.Null((await store.GetAsync(Tenant, workflow.Id))!.CausedBy);

        // The same opaque value under another kind is another identity, and reaches no run.
        var sameValueOtherKind = new RunId(PlanRun, workflow.Id.Value);
        Assert.NotEqual(workflow.Id, sameValueOtherKind);
        Assert.Null(await store.GetAsync(Tenant, sameValueOtherKind));
        var unknown = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => lifecycle.BeginAttemptAsync(Tenant, sameValueOtherKind).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.RunUnknown, unknown.Code);

        // Changing one run leaves the other untouched.
        await lifecycle.BeginAttemptAsync(Tenant, plan.Id);
        Assert.Equal(RunStatus.Pending, (await store.GetAsync(Tenant, workflow.Id))!.Status);
        Assert.Equal("workflow-run:" + workflow.Id.Value.ToString("D"), workflow.Id.ToString());
    }

    // ---- the rest of the contract ----

    [Fact(DisplayName = "T-528 S1: the transition table is exactly the closed vocabulary's legal moves")]
    public void Transition_table_is_exact()
    {
        var legal = new HashSet<(RunStatus, RunStatus)>
        {
            (RunStatus.Pending, RunStatus.Running),
            (RunStatus.Pending, RunStatus.Cancelled),
            (RunStatus.Running, RunStatus.Succeeded),
            (RunStatus.Running, RunStatus.AwaitingRetry),
            (RunStatus.Running, RunStatus.DeadLettered),
            (RunStatus.Running, RunStatus.Cancelled),
            (RunStatus.AwaitingRetry, RunStatus.Running),
            (RunStatus.AwaitingRetry, RunStatus.Cancelled),
        };
        var statuses = Enum.GetValues<RunStatus>();
        Assert.Equal(6, statuses.Length);
        foreach (var from in statuses)
        {
            foreach (var to in statuses)
            {
                Assert.Equal(legal.Contains((from, to)), RunStatusTransitions.IsLegal(from, to));
            }
        }

        Assert.Equal(
            [RunStatus.Succeeded, RunStatus.DeadLettered, RunStatus.Cancelled],
            statuses.Where(RunStatusTransitions.IsTerminal));
        Assert.False(RunStatusTransitions.IsLegal((RunStatus)99, RunStatus.Running));
        Assert.False(RunStatusTransitions.IsTerminal((RunStatus)99));
        var outside = Assert.Throws<ExecutionRuntimeRefusedException>(() => RunStatusTransitions.Require(RunStatus.Pending, (RunStatus)99));
        Assert.Contains("'pending' to '99'", outside.Message, StringComparison.Ordinal);
        Assert.Equal(
            ["pending", "running", "awaiting-retry", "succeeded", "dead-lettered", "cancelled"],
            statuses.Select(RunStatusTransitions.WireName));
    }

    [Fact(DisplayName = "T-528 S1: a non-retryable failure dead-letters on its first attempt")]
    public async Task Not_retryable_failure_dead_letters_at_once()
    {
        var (lifecycle, _, clock) = Build();
        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        var failure = new RunFailure("mapping.invalid", Retryable: false);

        var dead = await lifecycle.FailAttemptAsync(Tenant, run.Id, failure);

        Assert.Equal(RunStatus.DeadLettered, dead.Status);
        Assert.Single(dead.Attempts);
        Assert.Equal(new DeadLetter(DeadLetterReason.NotRetryable, failure, clock.GetUtcNow()), dead.DeadLetter);
    }

    [Fact(DisplayName = "T-528 S1: immediate-or-fail dead-letters a retryable failure after one attempt")]
    public async Task Single_attempt_profile_exhausts_at_once()
    {
        var (lifecycle, _, _) = Build();
        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi, RetryProfileName.ImmediateOrFail));
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);

        var dead = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("upstream.timeout", Retryable: true));

        Assert.Equal(DeadLetterReason.RetriesExhausted, dead.DeadLetter!.Reason);
    }

    [Fact(DisplayName = "T-528 S1: the substrate's profiles are fixed, and backoff doubles to its cap")]
    public void Profiles_are_substrate_owned()
    {
        Assert.Equal(
            ["immediate-or-fail", "external-api-standard", "durable-delivery", "manual-reconciliation"],
            RetryProfiles.All.Select(profile => profile.Name.Value));
        Assert.Equal([1, 3, 10, 1], RetryProfiles.All.Select(profile => profile.MaxAttempts));
        Assert.DoesNotContain(typeof(RetryProfile).GetConstructors(), constructor => constructor.IsPublic);

        var durable = RetryProfiles.Get(RetryProfileName.DurableDelivery);
        Assert.Equal(
            new[] { 30, 60, 120, 240, 480, 960, 1920, 3600, 3600 }.Select(seconds => TimeSpan.FromSeconds(seconds)),
            Enumerable.Range(1, 9).Select(durable.DelayAfter));
        Assert.True(durable.AllowsAnotherAttempt(9));
        Assert.False(durable.AllowsAnotherAttempt(10));
        Assert.Equal(TimeSpan.Zero, RetryProfiles.Get(RetryProfileName.ManualReconciliation).DelayAfter(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => durable.DelayAfter(0));
        Assert.Equal("durable-delivery", RetryProfileName.DurableDelivery.ToString());
        Assert.Equal(string.Empty, default(RetryProfileName).ToString());
    }

    [Fact(DisplayName = "T-528 S1 cc-5: a run kind is registered once, and an unregistered kind cannot start a run")]
    public async Task Run_kind_registration()
    {
        var registry = new RunKindRegistry();
        registry.Register(PlanRun, "scheduling");
        var registration = registry.Register(WorkflowRun, "workflows");
        Assert.Equal(new RunKindRegistration(WorkflowRun, "workflows"), registration);
        Assert.Equal([PlanRun, WorkflowRun], registry.Registrations.Select(r => r.Kind));

        var duplicate = Assert.Throws<ExecutionRuntimeRefusedException>(() => registry.Register(WorkflowRun, "someone-else"));
        Assert.Equal(ExecutionRuntimeRefusals.RunKindDuplicate, duplicate.Code);
        Assert.Contains("'workflows'", duplicate.Message, StringComparison.Ordinal);
        Assert.Equal("workflows", registry.Require(WorkflowRun).OwningEngine);
        Assert.Throws<ArgumentException>(() => registry.Register(new RunKind("report-run"), " "));
        Assert.Equal(
            ExecutionRuntimeRefusals.RunKindInvalid,
            Assert.Throws<ExecutionRuntimeRefusedException>(() => registry.Register(default, "workflows")).Code);
        Assert.Equal(
            ExecutionRuntimeRefusals.RunKindUnregistered,
            Assert.Throws<ExecutionRuntimeRefusedException>(() => registry.Require(default)).Code);

        var lifecycle = new RunLifecycle(registry, new InMemoryRunStore(), new ManualClock());
        var unregistered = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => lifecycle.StartAsync(new StartRun(new RunKind("report-run"), Tenant, ExternalApi)).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.RunKindUnregistered, unregistered.Code);
    }

    [Theory(DisplayName = "T-528 S1: a run kind is a lower-case kebab token")]
    [InlineData("")]
    [InlineData("Workflow-run")]
    [InlineData("workflow_run")]
    [InlineData("-run")]
    [InlineData("run-")]
    [InlineData("workflow--run")]
    [InlineData("9-run")]
    [InlineData("workflow run")]
    public void Run_kind_refuses_non_kebab(string value)
    {
        Assert.Equal(
            ExecutionRuntimeRefusals.RunKindInvalid,
            Assert.Throws<ExecutionRuntimeRefusedException>(() => new RunKind(value)).Code);
    }

    [Fact(DisplayName = "T-528 S1: a run kind is at most 64 characters")]
    public void Run_kind_length_bound()
    {
        Assert.Equal(64, new RunKind(new string('a', 64)).Value.Length);
        Assert.Throws<ExecutionRuntimeRefusedException>(() => new RunKind(new string('a', 65)));
        Assert.Throws<ExecutionRuntimeRefusedException>(() => new RunKind(null!));
        Assert.Equal("delivery-attempt", new RunKind("delivery-attempt").ToString());
        Assert.Equal(string.Empty, default(RunKind).ToString());
    }

    [Fact(DisplayName = "T-528 S1: a run identity needs a kind and a value")]
    public void Run_id_requires_kind_and_value()
    {
        Assert.Equal(
            ExecutionRuntimeRefusals.RunKindInvalid,
            Assert.Throws<ExecutionRuntimeRefusedException>(() => new RunId(default, Guid.NewGuid())).Code);
        Assert.Throws<ArgumentException>(() => new RunId(WorkflowRun, Guid.Empty));
        Assert.NotEqual(RunId.New(WorkflowRun), RunId.New(WorkflowRun));
    }

    [Fact(DisplayName = "T-528 S1 cc-3: caused_by must name a run the same tenant holds")]
    public async Task Caused_by_must_resolve_within_the_tenant()
    {
        var (lifecycle, _, _) = Build();
        var foreign = await lifecycle.StartAsync(new StartRun(WorkflowRun, OtherTenant, ExternalApi));

        foreach (var cause in new[] { foreign.Id, RunId.New(WorkflowRun) })
        {
            var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
                () => lifecycle.StartAsync(new StartRun(PlanRun, Tenant, ExternalApi, CausedBy: cause)).AsTask());
            Assert.Equal(ExecutionRuntimeRefusals.CausedByInvalid, refused.Code);
        }
    }

    [Fact(DisplayName = "T-528 S1: runs are tenant-scoped")]
    public async Task Runs_are_tenant_scoped()
    {
        var (lifecycle, store, _) = Build();
        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));

        Assert.Null(await store.GetAsync(OtherTenant, run.Id));
        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => lifecycle.BeginAttemptAsync(OtherTenant, run.Id).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.RunUnknown, refused.Code);
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.StartAsync(new StartRun(WorkflowRun, default, ExternalApi)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.DeadLetteredAsync(default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.SucceedAsync(default, run.Id).AsTask());
    }

    [Fact(DisplayName = "T-528 S1: cancel ends a running attempt and clears a pending retry; a terminal run cannot cancel")]
    public async Task Cancel_paths()
    {
        var (lifecycle, _, clock) = Build();

        var pending = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        var cancelledPending = await lifecycle.CancelAsync(Tenant, pending.Id);
        Assert.Equal(RunStatus.Cancelled, cancelledPending.Status);
        Assert.Empty(cancelledPending.Attempts);

        var running = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        await lifecycle.BeginAttemptAsync(Tenant, running.Id);
        clock.Advance(TimeSpan.FromSeconds(1));
        var cancelledRunning = await lifecycle.CancelAsync(Tenant, running.Id);
        Assert.Equal(clock.GetUtcNow(), Assert.Single(cancelledRunning.Attempts).EndedUtc);
        Assert.Null(cancelledRunning.Attempts[0].Failure);

        var waiting = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        await lifecycle.BeginAttemptAsync(Tenant, waiting.Id);
        await lifecycle.FailAttemptAsync(Tenant, waiting.Id, new RunFailure("upstream.timeout", Retryable: true));
        var cancelledWaiting = await lifecycle.CancelAsync(Tenant, waiting.Id);
        Assert.Null(cancelledWaiting.NextAttemptDueUtc);
        Assert.Equal("upstream.timeout", Assert.Single(cancelledWaiting.Attempts).Failure!.Code);
    }

    [Fact(DisplayName = "T-528 S1: a success records the attempt's end, the record's update time and version")]
    public async Task Success_records_attempt_and_version()
    {
        var (lifecycle, store, clock) = Build();
        var started = clock.GetUtcNow();
        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        Assert.Equal(1, run.Version);
        Assert.Equal(started, run.CreatedUtc);
        Assert.Equal(started, run.UpdatedUtc);

        clock.Advance(TimeSpan.FromSeconds(1));
        var running = await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        Assert.Equal(new RunAttempt(1, clock.GetUtcNow(), null, null), Assert.Single(running.Attempts));
        clock.Advance(TimeSpan.FromSeconds(1));
        var done = await lifecycle.SucceedAsync(Tenant, run.Id);

        Assert.Equal(3, done.Version);
        Assert.Equal(started, done.CreatedUtc);
        Assert.Equal(clock.GetUtcNow(), done.UpdatedUtc);
        Assert.Equal(new RunAttempt(1, started + TimeSpan.FromSeconds(1), clock.GetUtcNow(), null), Assert.Single(done.Attempts));
        Assert.Equal(done, await store.GetAsync(Tenant, run.Id));
    }

    [Fact(DisplayName = "T-528 S1: the store refuses a duplicate, an unknown run and a stale or skipped version")]
    public async Task Store_port_contract()
    {
        var store = new InMemoryRunStore();
        var now = DateTimeOffset.UnixEpoch;
        var record = new RunRecord
        {
            Id = RunId.New(WorkflowRun),
            TenantId = Tenant,
            Status = RunStatus.Pending,
            RetryProfile = RetryProfileName.ImmediateOrFail,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        await store.CreateAsync(record);

        Assert.Equal(ExecutionRuntimeRefusals.RunDuplicate,
            (await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => store.CreateAsync(record).AsTask())).Code);
        Assert.Equal(ExecutionRuntimeRefusals.RunUnknown,
            (await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
                () => store.UpdateAsync(record with { Id = RunId.New(WorkflowRun), Version = 2 }, 1).AsTask())).Code);
        Assert.Equal(ExecutionRuntimeRefusals.RunConcurrencyConflict,
            (await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
                () => store.UpdateAsync(record with { Version = 3 }, 2).AsTask())).Code);
        Assert.Equal(ExecutionRuntimeRefusals.RunConcurrencyConflict,
            (await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
                () => store.UpdateAsync(record with { Version = 3 }, 1).AsTask())).Code);

        await store.UpdateAsync(record with { Status = RunStatus.Running, Version = 2 }, 1);
        Assert.Equal(RunStatus.Running, (await store.GetAsync(Tenant, record.Id))!.Status);
        Assert.Equal(ExecutionRuntimeRefusals.RunConcurrencyConflict,
            (await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
                () => store.UpdateAsync(record with { Status = RunStatus.Cancelled, Version = 2 }, 1).AsTask())).Code);

        // Same tenant and status, created in reverse order: listed oldest first.
        var older = record with { Id = RunId.New(WorkflowRun), CreatedUtc = now - TimeSpan.FromMinutes(1) };
        await store.CreateAsync(older);
        await store.CreateAsync(older with { Id = RunId.New(PlanRun), TenantId = OtherTenant });
        Assert.Equal([older.Id], (await store.ListByStatusAsync(Tenant, RunStatus.Pending)).Select(run => run.Id));
        Assert.Equal([record.Id], (await store.ListByStatusAsync(Tenant, RunStatus.Running)).Select(run => run.Id));
    }

    [Fact(DisplayName = "T-528 S1: the store lists same-instant runs by identity")]
    public async Task Store_orders_ties_by_identity()
    {
        var store = new InMemoryRunStore();
        var now = DateTimeOffset.UnixEpoch;
        var ids = new[] { new RunId(WorkflowRun, Guid.Parse("00000000-0000-0000-0000-000000000002")), new RunId(PlanRun, Guid.Parse("00000000-0000-0000-0000-000000000001")) };
        foreach (var id in ids)
        {
            await store.CreateAsync(new RunRecord
            {
                Id = id, TenantId = Tenant, Status = RunStatus.Pending, RetryProfile = RetryProfileName.ImmediateOrFail,
                CreatedUtc = now, UpdatedUtc = now,
            });
        }

        Assert.Equal([ids[1], ids[0]], (await store.ListByStatusAsync(Tenant, RunStatus.Pending)).Select(run => run.Id));
    }

    [Fact(DisplayName = "T-528 S1: the store lists a status oldest first")]
    public async Task Store_orders_by_creation()
    {
        var store = new InMemoryRunStore();
        var now = DateTimeOffset.UnixEpoch;
        var newer = new RunRecord
        {
            Id = RunId.New(WorkflowRun), TenantId = Tenant, Status = RunStatus.Pending, RetryProfile = RetryProfileName.ImmediateOrFail,
            CreatedUtc = now, UpdatedUtc = now,
        };
        var older = newer with { Id = RunId.New(WorkflowRun), CreatedUtc = now - TimeSpan.FromMinutes(1) };
        await store.CreateAsync(newer);
        await store.CreateAsync(older);

        Assert.Equal([older.Id, newer.Id], (await store.ListByStatusAsync(Tenant, RunStatus.Pending)).Select(run => run.Id));
    }

    [Fact(DisplayName = "T-528 S1 ck-8: a capability cannot allow a profile the substrate does not define")]
    public void Capability_allows_only_defined_profiles()
    {
        var refused = Assert.Throws<ExecutionRuntimeRefusedException>(() => new CapabilityRetryPolicy(
            RetryProfileName.ImmediateOrFail, [RetryProfileName.ImmediateOrFail, default]));
        Assert.Equal(ExecutionRuntimeRefusals.RetryProfileUnknown, refused.Code);
    }

    [Fact(DisplayName = "T-528 S1: the store refuses a null record and honours cancellation")]
    public async Task Store_guards()
    {
        var store = new InMemoryRunStore();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var record = new RunRecord
        {
            Id = RunId.New(WorkflowRun), TenantId = Tenant, Status = RunStatus.Pending, RetryProfile = RetryProfileName.ImmediateOrFail,
            CreatedUtc = DateTimeOffset.UnixEpoch, UpdatedUtc = DateTimeOffset.UnixEpoch,
        };

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.CreateAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.UpdateAsync(null!, 1).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.CreateAsync(record, cancelled.Token).AsTask());
        Assert.Null(await store.GetAsync(Tenant, record.Id));
        await store.CreateAsync(record);
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.GetAsync(Tenant, record.Id, cancelled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.UpdateAsync(record with { Version = 2 }, 1, cancelled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.ListByStatusAsync(Tenant, RunStatus.Pending, cancelled.Token).AsTask());
        Assert.Equal(1, (await store.GetAsync(Tenant, record.Id))!.Version);
    }

    [Fact(DisplayName = "T-528 S1: a failure needs a code, and the lifecycle needs its collaborators")]
    public async Task Guards()
    {
        var (lifecycle, _, _) = Build();
        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, ExternalApi));
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure(" ", true)).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => lifecycle.FailAttemptAsync(Tenant, run.Id, null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => lifecycle.StartAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => lifecycle.StartAsync(new StartRun(WorkflowRun, Tenant, null!)).AsTask());
        Assert.Throws<ArgumentNullException>(() => new RunLifecycle(null!, new InMemoryRunStore(), TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new RunLifecycle(new RunKindRegistry(), null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new RunLifecycle(new RunKindRegistry(), new InMemoryRunStore(), null!));
        Assert.Throws<ArgumentNullException>(() => new CapabilityRetryPolicy(RetryProfileName.ImmediateOrFail, null!));
    }

    private static (RunLifecycle Lifecycle, InMemoryRunStore Store, ManualClock Clock) Build()
    {
        var registry = new RunKindRegistry();
        registry.Register(WorkflowRun, "workflows");
        registry.Register(PlanRun, "scheduling");
        var store = new InMemoryRunStore();
        var clock = new ManualClock();
        return (new RunLifecycle(registry, store, clock), store, clock);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
