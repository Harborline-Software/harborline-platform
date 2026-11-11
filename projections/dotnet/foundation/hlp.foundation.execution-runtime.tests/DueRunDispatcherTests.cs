using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class DueRunDispatcherTests
{
    private static readonly TenantId Tenant = new("one");
    private static readonly RunKind Kind = new("workflow-run");
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "execution-runtime-eng-3: concurrent retry claims start one attempt, never early, and do not recover a dead holder")]
    public async Task Retry_claim()
    {
        // Oracle: ADR-0118 decision 6 and ADR-0147 decision 1. Mutation: omit BeginAttemptAsync.
        var store = new YieldingStore();
        var clock = new Clock();
        var registry = new RunKindRegistry();
        var calls = 0;
        registry.Register(Kind, "workflows", (_, step, _) => { Assert.Null(step); Interlocked.Increment(ref calls); return ValueTask.CompletedTask; });
        var lifecycle = new RunLifecycle(registry, store, clock);
        var run = await lifecycle.StartAsync(new StartRun(Kind, Tenant, new CapabilityRetryPolicy(RetryProfileName.ExternalApiStandard, [RetryProfileName.ExternalApiStandard])));
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        run = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("transient", true));
        var a = new DueRunDispatcher(registry, store, clock, "a");
        var b = new DueRunDispatcher(registry, store, clock, "b");
        store.Reads = 0;
        clock.Now = Now.AddSeconds(2).AddTicks(-1);
        await a.DispatchAsync(Tenant);
        Assert.Equal(0, calls);
        Assert.Equal(0, store.Reads);
        clock.Now = Now.AddSeconds(2);
        await Task.WhenAll(a.DispatchAsync(Tenant).AsTask(), b.DispatchAsync(Tenant).AsTask());
        Assert.Equal(1, calls);
        var claimed = (await store.GetAsync(Tenant, run.Id))!;
        Assert.Equal(RunStatus.Running, claimed.Status);
        Assert.Equal(2, claimed.Attempts.Count);
        Assert.Equal(2, claimed.Attempts[1].Number);
        Assert.Equal(clock.Now, claimed.Attempts[1].StartedUtc);
        Assert.Null(claimed.NextAttemptDueUtc);
        clock.Now = Now.AddDays(1);
        await b.DispatchAsync(Tenant);
        Assert.Equal(1, calls);
    }

    [Theory(DisplayName = "execution-runtime-eng-3: missing handlers report due retries and timers without claiming")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_handler(bool registered)
    {
        // Oracle: slice acceptance 1. Mutation: skip missing-handler report.
        var store = new InMemoryRunStore();
        var registry = new RunKindRegistry();
        if (registered) registry.Register(Kind, "workflows");
        var retry = Record() with { Status = RunStatus.AwaitingRetry, NextAttemptDueUtc = Now };
        var timer = Record();
        await store.CreateAsync(retry);
        await store.CreateAsync(timer);
        var reports = await new DueRunDispatcher(registry, store, new Clock(), "a").DispatchAsync(Tenant);
        Assert.Equal(new[] { retry.Id, timer.Id }, reports);
        Assert.Equal(1, (await store.GetAsync(Tenant, retry.Id))!.Version);
        Assert.Equal(1, (await store.GetAsync(Tenant, timer.Id))!.Version);
    }

    [Fact(DisplayName = "execution-runtime-eng-3 execution-runtime-ck-14: timer boundary and competing dispatchers")]
    public async Task Timer_boundary_and_concurrency()
    {
        // Oracle: ADR-0143 decisions 1–3. Mutation: change WakeUpUtc <= now to < now; remove lease eligibility.
        var store = new YieldingStore();
        var registry = new RunKindRegistry();
        var calls = 0;
        registry.Register(Kind, "workflows", (run, step, _) => { Assert.Equal("timer", step); Assert.Equal(RunStatus.Running, run.Status); Interlocked.Increment(ref calls); return ValueTask.CompletedTask; });
        var run = Record();
        await store.CreateAsync(run);
        var clock = new Clock { Now = Now.AddTicks(-1) };
        var a = new DueRunDispatcher(registry, store, clock, "a");
        var b = new DueRunDispatcher(registry, store, clock, "b");
        await a.DispatchAsync(Tenant);
        await b.DispatchAsync(Tenant);
        Assert.Equal(0, calls);
        clock.Now = Now;
        await Task.WhenAll(a.DispatchAsync(Tenant).AsTask(), b.DispatchAsync(Tenant).AsTask());
        Assert.Equal(1, calls);
        var wait = Assert.Single((await store.GetAsync(Tenant, run.Id))!.Waits);
        Assert.Contains(wait.ClaimDispatcherId, new[] { "a", "b" });
        Assert.Equal(Now.AddMinutes(5), wait.ClaimExpiresUtc);
        Assert.Equal(Now, wait.WakeUpUtc);
    }

    [Fact(DisplayName = "execution-runtime-eng-3 execution-runtime-ck-14: lease expiry fences late resolution and repeats are no-ops")]
    public async Task Lease_and_fence()
    {
        // Oracle: ADR-0147 decisions 2–3. Mutation: accept stale expectedVersion in UpdateAsync.
        var store = new InMemoryRunStore();
        var clock = new Clock();
        var registry = new RunKindRegistry();
        var deliveries = new List<RunRecord>();
        registry.Register(Kind, "workflows", (run, _, _) => { deliveries.Add(run); return ValueTask.CompletedTask; });
        var run = Record();
        await store.CreateAsync(run);
        var a = new DueRunDispatcher(registry, store, clock, "a", TimeSpan.FromSeconds(10));
        var b = new DueRunDispatcher(registry, store, clock, "b");
        await a.DispatchAsync(Tenant);
        var first = Assert.Single(deliveries);
        Assert.Equal("a", first.Waits[0].ClaimDispatcherId);
        Assert.Equal(Now.AddSeconds(10), first.Waits[0].ClaimExpiresUtc);
        clock.Now = Now.AddSeconds(10).AddTicks(-1);
        await b.DispatchAsync(Tenant);
        Assert.Single(deliveries);
        clock.Now = Now.AddSeconds(10);
        await b.DispatchAsync(Tenant);
        Assert.Equal(2, deliveries.Count);
        var refusal = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => Resolve(store, first, "timer"));
        Assert.Equal("execution.run_concurrency_conflict", refusal.Code);
        Assert.Equal(3, (await store.GetAsync(Tenant, run.Id))!.Version);
        Assert.Equal("b", (await store.GetAsync(Tenant, run.Id))!.Waits[0].ClaimDispatcherId);
        await Resolve(store, deliveries[1], "timer");
        var resolved = (await store.GetAsync(Tenant, run.Id))!;
        await Resolve(store, deliveries[1], "timer");
        Assert.Equal(resolved.Version, (await store.GetAsync(Tenant, run.Id))!.Version);
        await b.DispatchAsync(Tenant);
        Assert.Equal(2, deliveries.Count);
        Assert.Empty((await store.GetAsync(Tenant, run.Id))!.Waits);
    }

    [Fact(DisplayName = "execution-runtime-eng-3 execution-runtime-ck-14: restart clears only own claims before first overdue pass")]
    public async Task Restart_recovers_own_claims()
    {
        // Oracle: ADR-0147 decision 4. Mutation: clear all owners instead of matching dispatcher id.
        var store = new InMemoryRunStore();
        var clock = new Clock();
        var registry = new RunKindRegistry();
        var deliveries = new List<RunRecord>();
        registry.Register(Kind, "workflows", (run, _, _) => { deliveries.Add(run); return ValueTask.CompletedTask; });
        var own = Record();
        await store.CreateAsync(own);
        await new DueRunDispatcher(registry, store, clock, "a").DispatchAsync(Tenant);
        var other = Record();
        await store.CreateAsync(other);
        await new DueRunDispatcher(registry, store, clock, "b").DispatchAsync(Tenant);
        Assert.Equal(2, deliveries.Count);
        var otherBefore = (await store.GetAsync(Tenant, other.Id))!;
        clock.Now = Now.AddSeconds(1);
        await new DueRunDispatcher(registry, store, clock, "a").DispatchAsync(Tenant);
        Assert.Equal(3, deliveries.Count);
        Assert.Equal(own.Id, deliveries[2].Id);
        Assert.Equal(otherBefore.Version, (await store.GetAsync(Tenant, other.Id))!.Version);
        Assert.Equal("b", (await store.GetAsync(Tenant, other.Id))!.Waits[0].ClaimDispatcherId);
    }

    [Fact(DisplayName = "execution-runtime-ck-14 execution-runtime-eng-3: held overdue wait is never claimed and release preserves wake-up")]
    public async Task Hold_release()
    {
        // Oracle: ADR-0132 and ADR-0143 decision 2. Mutation: remove IsHeld eligibility check.
        var store = new InMemoryRunStore();
        var clock = new Clock { Now = Now.AddHours(1) };
        var registry = new RunKindRegistry();
        var calls = 0;
        registry.Register(Kind, "workflows", (_, _, _) => { calls++; return ValueTask.CompletedTask; });
        var run = Record();
        run = run with { Waits = [run.Waits[0].Hold()] };
        await store.CreateAsync(run);
        var dispatcher = new DueRunDispatcher(registry, store, clock, "a");
        for (var pass = 0; pass < 3; pass++) await dispatcher.DispatchAsync(Tenant);
        Assert.Equal(0, calls);
        Assert.Null((await store.GetAsync(Tenant, run.Id))!.Waits[0].ClaimDispatcherId);
        await store.UpdateAsync(run with { Waits = [run.Waits[0].Release()], Version = 2 }, 1);
        await dispatcher.DispatchAsync(Tenant);
        await dispatcher.DispatchAsync(Tenant);
        Assert.Equal(1, calls);
        Assert.Equal(Now, (await store.GetAsync(Tenant, run.Id))!.Waits[0].WakeUpUtc);
    }

    [Fact(DisplayName = "execution-runtime-eng-3: invalid settings and cancellation are refused")]
    public async Task Invalid_settings()
    {
        // Oracle: positive lease and nonempty stable node id. Mutation: remove lease/id validation.
        var registry = new RunKindRegistry();
        var store = new InMemoryRunStore();
        var clock = new Clock();
        Assert.Throws<ArgumentNullException>(() => new DueRunDispatcher(null!, store, clock, "a"));
        Assert.Throws<ArgumentNullException>(() => new DueRunDispatcher(registry, null!, clock, "a"));
        Assert.Throws<ArgumentNullException>(() => new DueRunDispatcher(registry, store, null!, "a"));
        Assert.Throws<ArgumentException>(() => new DueRunDispatcher(registry, store, clock, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DueRunDispatcher(registry, store, clock, "a", TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DueRunDispatcher(registry, store, clock, "a", TimeSpan.FromTicks(-1)));
        var dispatcher = new DueRunDispatcher(registry, store, clock, "a");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => dispatcher.DispatchAsync(default).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(Tenant, new CancellationToken(true)).AsTask());
        Assert.Empty(await dispatcher.DispatchAsync(Tenant));
    }

    [Fact(DisplayName = "execution-runtime-eng-3 execution-runtime-ck-14: startup retries conflicts and preserves other claims and non-timers")]
    public async Task Startup_conflict()
    {
        // Oracle: ADR-0147 decision 4. Mutation: clear every claim; remove cleanup conflict retry.
        var store = new YieldingStore();
        var registry = new RunKindRegistry();
        var clock = new Clock();
        var own = Record();
        await store.CreateAsync(own);
        registry.Register(Kind, "workflows", (_, _, _) => ValueTask.CompletedTask);
        await new DueRunDispatcher(registry, store, clock, "a").DispatchAsync(Tenant);
        var claimed = (await store.GetAsync(Tenant, own.Id))!;
        // Put an independently claimed timer beside the recovering timer on the same run.
        var other = Record() with { Waits = [new RunWait("other", RunWaitReason.Timer, Now, Now)] };
        await store.CreateAsync(other);
        await new DueRunDispatcher(registry, store, clock, "b").DispatchAsync(Tenant);
        var otherWait = (await store.GetAsync(Tenant, other.Id))!.Waits[0];
        await store.UpdateAsync(claimed with { Version = claimed.Version + 1, Waits = [claimed.Waits[0].Hold(), otherWait, new RunWait("human", RunWaitReason.HumanTask, Now)] }, claimed.Version);
        store.Conflicts = 1;
        await new DueRunDispatcher(registry, store, clock, "a").DispatchAsync(Tenant);
        var saved = (await store.GetAsync(Tenant, own.Id))!;
        Assert.Null(saved.Waits[0].ClaimDispatcherId);
        Assert.Null(saved.Waits[0].ClaimExpiresUtc);
        Assert.True(saved.Waits[0].IsHeld);
        Assert.Equal("b", saved.Waits[1].ClaimDispatcherId);
        Assert.Equal(Now.AddMinutes(5), saved.Waits[1].ClaimExpiresUtc);
        Assert.Equal("human", saved.Waits[2].StepId);
        Assert.Equal(0, store.Conflicts);
    }

    [Theory(DisplayName = "execution-runtime-eng-3: dispatcher awaits never capture the host context")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task No_context_capture(int pauseAt)
    {
        // Oracle: existing T-1039 library awaits contract. Mutation: ConfigureAwait(false) -> true.
        var store = new YieldingStore();
        var registry = new RunKindRegistry();
        var clock = new Clock();
        var run = Record();
        await store.CreateAsync(run);
        registry.Register(Kind, "workflows", (_, _, _) => store.Pause());
        await new DueRunDispatcher(registry, store, clock, "a").DispatchAsync(Tenant);
        var retry = Record() with { Status = RunStatus.AwaitingRetry, NextAttemptDueUtc = Now };
        await store.CreateAsync(retry);
        store.ResetPause(pauseAt);
        var context = new RecordingContext();
        var prior = SynchronizationContext.Current;
        Task<IReadOnlyList<RunId>> operation;
        SynchronizationContext.SetSynchronizationContext(context);
        try { operation = new DueRunDispatcher(registry, store, clock, "a").DispatchAsync(Tenant).AsTask(); }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        store.Completion.TrySetResult();
        await operation;
        Assert.Equal(0, context.PostCount);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref PostCount);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    [Theory(DisplayName = "execution-runtime-eng-3 execution-runtime-ck-14: a listed timer is rechecked before claiming")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Listed_timer_changed(int change)
    {
        // Oracle: claim only a still-running run with the same pending wait. Mutation: remove recheck guard.
        var store = new YieldingStore();
        var registry = new RunKindRegistry();
        var calls = 0;
        registry.Register(Kind, "workflows", (_, _, _) => { calls++; return ValueTask.CompletedTask; });
        var dispatcher = new DueRunDispatcher(registry, store, new Clock(), "a");
        await dispatcher.DispatchAsync(Tenant);
        await store.CreateAsync(Record());
        store.ReadOverride = run => change switch { 0 => null, 1 => run! with { Status = RunStatus.Succeeded }, _ => run! with { Waits = [] } };
        await dispatcher.DispatchAsync(Tenant);
        Assert.Equal(0, calls);
    }

    [Fact(DisplayName = "execution-runtime-eng-3 execution-runtime-ck-14: claim conflict skips delivery and handler errors propagate")]
    public async Task Claim_conflict_and_handler_error()
    {
        // Oracle: version-checked claims precede every handoff; handler errors are not claim refusals.
        // Mutation: invert conflict filter or catch handler refusal as a claim conflict.
        var store = new YieldingStore();
        var registry = new RunKindRegistry();
        var calls = 0;
        registry.Register(Kind, "workflows", (_, _, _) => { calls++; throw new ExecutionRuntimeRefusedException("execution.run_concurrency_conflict", "handler error"); });
        var dispatcher = new DueRunDispatcher(registry, store, new Clock(), "a");
        await dispatcher.DispatchAsync(Tenant);
        await store.CreateAsync(Record());
        store.Conflicts = 1;
        await dispatcher.DispatchAsync(Tenant);
        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => dispatcher.DispatchAsync(Tenant).AsTask());
        Assert.Equal(1, calls);
        await dispatcher.DispatchAsync(Tenant);
        Assert.Equal(1, calls);
    }

    [Fact(DisplayName = "execution-runtime-eng-3 execution-runtime-ck-14: parallel timers claim only their own wait and skip resolved siblings")]
    public async Task Parallel_timer_resolution()
    {
        // Oracle: ADR-0118 parallel waits and ADR-0143 resolved repeat. Mutation: claim all waits in Select.
        var store = new InMemoryRunStore();
        var registry = new RunKindRegistry();
        var calls = 0;
        registry.Register(Kind, "workflows", async (run, step, ct) =>
        {
            calls++;
            Assert.Equal("timer", step);
            Assert.Equal("a", run.Waits[0].ClaimDispatcherId);
            Assert.Null(run.Waits[1].ClaimDispatcherId);
            await store.UpdateAsync(run with { Waits = [], Version = run.Version + 1 }, run.Version, ct);
        });
        var original = Record();
        await store.CreateAsync(original with { Waits = [original.Waits[0], new RunWait("sibling", RunWaitReason.Timer, Now, Now)] });
        await new DueRunDispatcher(registry, store, new Clock(), "a").DispatchAsync(Tenant);
        Assert.Equal(1, calls);
    }

    [Fact(DisplayName = "execution-runtime-eng-3: queued passes do not capture the host context")]
    public async Task Queued_pass()
    {
        // Oracle: T-1039 library await contract. Mutation: capture context at semaphore WaitAsync.
        var registry = new RunKindRegistry();
        var store = new InMemoryRunStore();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register(Kind, "workflows", (_, _, _) => { entered.SetResult(); return new ValueTask(release.Task); });
        await store.CreateAsync(Record());
        var dispatcher = new DueRunDispatcher(registry, store, new Clock(), "a");
        var first = dispatcher.DispatchAsync(Tenant).AsTask();
        await entered.Task;
        var context = new RecordingContext();
        var prior = SynchronizationContext.Current;
        Task<IReadOnlyList<RunId>> second;
        SynchronizationContext.SetSynchronizationContext(context);
        try { second = dispatcher.DispatchAsync(Tenant).AsTask(); }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        Assert.False(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(0, context.PostCount);
    }

    [Fact(DisplayName = "execution-runtime-eng-3: losing a retry claim never hands off the unclaimed run")]
    public async Task Retry_conflict()
    {
        // Oracle: ADR-0147 decision 1. Mutation: swallow conflict and hand off unclaimed/null run.
        var store = new YieldingStore();
        var registry = new RunKindRegistry();
        var calls = 0;
        registry.Register(Kind, "workflows", (_, _, _) => { calls++; return ValueTask.CompletedTask; });
        var dispatcher = new DueRunDispatcher(registry, store, new Clock(), "a");
        await dispatcher.DispatchAsync(Tenant);
        await store.CreateAsync(Record() with { Waits = [], Status = RunStatus.AwaitingRetry, NextAttemptDueUtc = Now });
        store.Conflicts = 1;
        await dispatcher.DispatchAsync(Tenant);
        Assert.Equal(0, calls);
        await dispatcher.DispatchAsync(Tenant);
        Assert.Equal(1, calls);
    }

    private static async Task Resolve(IRunStore store, RunRecord delivery, string step)
    {
        // Minimal engine stand-in: resolved repeats do nothing; writes use the delivered fencing version.
        var current = (await store.GetAsync(delivery.TenantId, delivery.Id))!;
        if (!current.Waits.Any(wait => wait.StepId == step)) return;
        await store.UpdateAsync(delivery with { Waits = delivery.Waits.Where(wait => wait.StepId != step).ToArray(), Version = delivery.Version + 1 }, delivery.Version);
    }

    private static RunRecord Record() => new()
    {
        Id = RunId.New(Kind), TenantId = Tenant, Status = RunStatus.Running,
        RetryProfile = RetryProfileName.ExternalApiStandard, CreatedUtc = Now, UpdatedUtc = Now,
        Waits = [new RunWait("timer", RunWaitReason.Timer, Now.AddHours(-1), Now)],
    };

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DueRunDispatcherTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class YieldingStore : IRunStore
    {
        private readonly InMemoryRunStore _inner = new();
        public Func<RunRecord?, RunRecord?>? ReadOverride { get; set; }
        public int Reads { get; set; }
        public int PauseAt { get; set; } = -1;
        private int _operations;
        public void ResetPause(int pauseAt) { _operations = 0; PauseAt = pauseAt; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask Pause() => ++_operations == PauseAt ? new ValueTask(Completion.Task) : ValueTask.CompletedTask;
        public ValueTask CreateAsync(RunRecord record, CancellationToken cancellationToken = default) => _inner.CreateAsync(record, cancellationToken);
        public async ValueTask<RunRecord?> GetAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken = default)
        {
            Reads++;
            var result = await _inner.GetAsync(tenantId, id, cancellationToken);
            if (PauseAt < 0) await Task.Yield();
            else await Pause().ConfigureAwait(false);
            return ReadOverride is null ? result : ReadOverride(result);
        }
        public int Conflicts { get; set; }
        public async ValueTask UpdateAsync(RunRecord record, long expectedVersion, CancellationToken cancellationToken = default)
        {
            if (Conflicts > 0)
            {
                Conflicts--;
                throw new ExecutionRuntimeRefusedException("execution.run_concurrency_conflict", "injected competing writer");
            }
            await Pause().ConfigureAwait(false);
            await _inner.UpdateAsync(record, expectedVersion, cancellationToken);
        }
        public async ValueTask<IReadOnlyList<RunRecord>> ListByStatusAsync(TenantId tenantId, RunStatus status, CancellationToken cancellationToken = default)
        {
            var result = await _inner.ListByStatusAsync(tenantId, status, cancellationToken);
            if (PauseAt < 0) await Task.Yield();
            else await Pause().ConfigureAwait(false);
            return result;
        }
    }
}
