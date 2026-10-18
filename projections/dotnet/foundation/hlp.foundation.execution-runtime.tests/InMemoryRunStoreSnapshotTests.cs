using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class InMemoryRunStoreSnapshotTests
{
    private static readonly TenantId Tenant = new() { Value = "snapshot-tenant" };
    private static readonly TenantId OtherTenant = new() { Value = "other-snapshot-tenant" };
    private static readonly RunKind Workflow = new("workflow-run");
    private static readonly RunId FirstId = new(Workflow, Guid.Parse("10101010-1000-4000-8000-000000000001"));
    private static readonly RunId SecondId = new(Workflow, Guid.Parse("10101010-1000-4000-8000-000000000002"));
    private static readonly RunId OtherId = new(Workflow, Guid.Parse("10101010-1000-4000-8000-000000000003"));
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_detaches_the_callers_mutable_attempt_history()
    {
        var store = new InMemoryRunStore();
        var attempts = new List<RunAttempt> { OriginalAttempt() };
        await store.CreateAsync(Record(FirstId, attempts));

        attempts[0] = TamperedAttempt();
        attempts.Add(TamperedAttempt());
        Assert.Equal(2, attempts.Count);
        Assert.Equal("tampered.failure", attempts[0].Failure!.Code);

        var stored = Assert.IsType<RunRecord>(await store.GetAsync(Tenant, FirstId));
        Assert.Equal(1, stored.Version);
        AssertOriginalAttempt(Assert.Single(stored.Attempts));
    }

    [Fact]
    public async Task Update_detaches_new_history_and_still_refuses_a_stale_version()
    {
        var store = new InMemoryRunStore();
        var original = Record(FirstId, new List<RunAttempt> { OriginalAttempt() });
        await store.CreateAsync(original);
        var attempts = new List<RunAttempt>
        {
            OriginalAttempt(),
            new(2, Now.AddSeconds(3), Now.AddSeconds(4), new RunFailure("updated.failure", true)),
        };
        var update = original with
        {
            Attempts = attempts,
            UpdatedUtc = Now.AddSeconds(4),
            NextAttemptDueUtc = Now.AddSeconds(8),
            Version = 2,
        };
        await store.UpdateAsync(update, 1);

        attempts.Clear();
        Assert.Empty(attempts);
        var stored = Assert.IsType<RunRecord>(await store.GetAsync(Tenant, FirstId));
        Assert.Equal(2, stored.Version);
        Assert.Equal(Now.AddSeconds(4), stored.UpdatedUtc);
        Assert.Equal(Now.AddSeconds(8), stored.NextAttemptDueUtc);
        Assert.Equal([1, 2], stored.Attempts.Select(attempt => attempt.Number));
        AssertOriginalAttempt(stored.Attempts[0]);
        Assert.Equal("updated.failure", stored.Attempts[1].Failure!.Code);

        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => store.UpdateAsync(update, 1).AsTask());
        Assert.Equal("execution.run_concurrency_conflict", refused.Code);
        Assert.Equal(2, (await store.GetAsync(Tenant, FirstId))!.Attempts.Count);
        Assert.Equal(2, (await store.GetAsync(Tenant, FirstId))!.Version);
    }

    [Fact]
    public async Task Get_does_not_export_a_mutable_alias_to_retained_history()
    {
        var store = new InMemoryRunStore();
        await store.CreateAsync(Record(FirstId, new List<RunAttempt> { OriginalAttempt() }));

        var exported = Assert.IsType<RunRecord>(await store.GetAsync(Tenant, FirstId));
        TryReplaceExportedAttempt(exported.Attempts);

        var stored = Assert.IsType<RunRecord>(await store.GetAsync(Tenant, FirstId));
        Assert.Equal(1, stored.Version);
        AssertOriginalAttempt(Assert.Single(stored.Attempts));
    }

    [Fact]
    public async Task List_detaches_every_matching_history_and_preserves_tenant_status_and_order()
    {
        var store = new InMemoryRunStore();
        await store.CreateAsync(Record(SecondId, new List<RunAttempt> { OriginalAttempt() }) with
        {
            CreatedUtc = Now.AddMinutes(1),
        });
        await store.CreateAsync(Record(FirstId, new List<RunAttempt> { OriginalAttempt() }));
        await store.CreateAsync(Record(OtherId, new List<RunAttempt> { OriginalAttempt() }) with
        {
            TenantId = OtherTenant,
        });

        var exported = await store.ListByStatusAsync(Tenant, RunStatus.AwaitingRetry);
        Assert.Equal([FirstId, SecondId], exported.Select(record => record.Id));
        foreach (var record in exported) TryReplaceExportedAttempt(record.Attempts);

        var retained = await store.ListByStatusAsync(Tenant, RunStatus.AwaitingRetry);
        Assert.Equal([FirstId, SecondId], retained.Select(record => record.Id));
        foreach (var record in retained)
        {
            Assert.Equal(1, record.Version);
            AssertOriginalAttempt(Assert.Single(record.Attempts));
        }
        Assert.Null(await store.GetAsync(OtherTenant, FirstId));
        Assert.Empty(await store.ListByStatusAsync(Tenant, RunStatus.Succeeded));
        Assert.Equal(OtherId, Assert.Single(await store.ListByStatusAsync(OtherTenant, RunStatus.AwaitingRetry)).Id);
    }

    [Fact]
    public async Task Mutating_real_lifecycle_returns_cannot_reset_the_three_attempt_retry_budget()
    {
        var registry = new RunKindRegistry();
        registry.Register(Workflow, "workflows");
        var store = new InMemoryRunStore();
        var clock = new ManualClock();
        var lifecycle = new RunLifecycle(registry, store, clock);
        var capability = new CapabilityRetryPolicy(
            RetryProfileName.ExternalApiStandard, [RetryProfileName.ExternalApiStandard]);
        var run = await lifecycle.StartAsync(new StartRun(Workflow, Tenant, capability));
        var failure = new RunFailure("upstream.timeout", true);
        Assert.Equal(1, run.Version);

        Assert.Equal(2, (await lifecycle.BeginAttemptAsync(Tenant, run.Id)).Version);
        var first = await lifecycle.FailAttemptAsync(Tenant, run.Id, failure);
        Assert.Equal(3, first.Version);
        Assert.Equal(RunStatus.AwaitingRetry, first.Status);
        Assert.Equal(Now.AddSeconds(2), first.NextAttemptDueUtc);
        ClearLifecycleReturn(first);
        var retainedFirst = Assert.IsType<RunRecord>(await store.GetAsync(Tenant, run.Id));
        Assert.Equal(3, retainedFirst.Version);
        Assert.Equal(1, Assert.Single(retainedFirst.Attempts).Number);

        clock.Advance(TimeSpan.FromSeconds(2));
        var secondStart = await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        Assert.Equal(4, secondStart.Version);
        Assert.Equal([1, 2], secondStart.Attempts.Select(attempt => attempt.Number));
        var second = await lifecycle.FailAttemptAsync(Tenant, run.Id, failure);
        Assert.Equal(5, second.Version);
        Assert.Equal(RunStatus.AwaitingRetry, second.Status);
        Assert.Equal(Now.AddSeconds(6), second.NextAttemptDueUtc);
        ClearLifecycleReturn(second);
        var retainedSecond = Assert.IsType<RunRecord>(await store.GetAsync(Tenant, run.Id));
        Assert.Equal(5, retainedSecond.Version);
        Assert.Equal([1, 2], retainedSecond.Attempts.Select(attempt => attempt.Number));

        clock.Advance(TimeSpan.FromSeconds(4));
        var thirdStart = await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        Assert.Equal(6, thirdStart.Version);
        Assert.Equal([1, 2, 3], thirdStart.Attempts.Select(attempt => attempt.Number));
        var exhausted = await lifecycle.FailAttemptAsync(Tenant, run.Id, failure);
        Assert.Equal(7, exhausted.Version);
        Assert.Equal(RunStatus.DeadLettered, exhausted.Status);
        Assert.Equal([1, 2, 3], exhausted.Attempts.Select(attempt => attempt.Number));
        Assert.All(exhausted.Attempts, attempt => Assert.Equal("upstream.timeout", attempt.Failure!.Code));
        Assert.Null(exhausted.NextAttemptDueUtc);
        Assert.Equal(DeadLetterReason.RetriesExhausted, exhausted.DeadLetter!.Reason);
        ClearLifecycleReturn(exhausted);

        var deadLetter = Assert.Single(await lifecycle.DeadLetteredAsync(Tenant));
        Assert.Equal(7, deadLetter.Version);
        Assert.Equal([1, 2, 3], deadLetter.Attempts.Select(attempt => attempt.Number));
    }

    private static RunRecord Record(RunId id, IReadOnlyList<RunAttempt> attempts) => new()
    {
        Id = id,
        TenantId = Tenant,
        Status = RunStatus.AwaitingRetry,
        RetryProfile = RetryProfileName.ExternalApiStandard,
        Attempts = attempts,
        NextAttemptDueUtc = Now.AddSeconds(3),
        CreatedUtc = Now,
        UpdatedUtc = Now.AddSeconds(1),
    };

    private static RunAttempt OriginalAttempt() =>
        new(1, Now, Now.AddSeconds(1), new RunFailure("original.failure", true));

    private static RunAttempt TamperedAttempt() =>
        new(91, Now, Now.AddSeconds(1), new RunFailure("tampered.failure", false));

    private static void AssertOriginalAttempt(RunAttempt attempt)
    {
        Assert.Equal(1, attempt.Number);
        Assert.Equal(Now, attempt.StartedUtc);
        Assert.Equal(Now.AddSeconds(1), attempt.EndedUtc);
        Assert.Equal("original.failure", attempt.Failure!.Code);
        Assert.True(attempt.Failure!.Retryable);
    }

    private static void TryReplaceExportedAttempt(IReadOnlyList<RunAttempt> attempts)
    {
        if (attempts is not IList<RunAttempt> indexed) return;
        try
        {
            // Arrays allow indexed writes even though their IList.IsReadOnly is true.
            indexed[0] = TamperedAttempt();
        }
        catch (NotSupportedException) { return; }
        Assert.Equal(91, indexed[0].Number);
        Assert.Equal("tampered.failure", indexed[0].Failure!.Code);
    }

    private static void ClearLifecycleReturn(RunRecord record)
    {
        // The protected real EndLastAttempt returns a List. Attack that producer return,
        // without requiring any particular collection representation from the store.
        ((IList<RunAttempt>)record.Attempts).Clear();
        Assert.Empty(record.Attempts);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
