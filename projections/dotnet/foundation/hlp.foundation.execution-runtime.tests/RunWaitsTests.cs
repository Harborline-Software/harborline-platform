using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class RunWaitsTests
{
    [Fact(DisplayName = "execution-runtime-ck-14: parallel waits retain fixed UTC times and closed reasons")]
    public async Task Parallel_waits()
    {
        // Oracle: ADR-0118 decisions 1–3, ADR-0143 decision 2. Mutation: discard Waits in Snapshot.
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var waits = new List<RunWait> { new("approval", RunWaitReason.HumanTask, now), new("delay", RunWaitReason.Timer, now, now.AddHours(1)) };
        var run = new RunRecord { Id = RunId.New(new RunKind("workflow-run")), TenantId = new TenantId("one"), Status = RunStatus.Running, RetryProfile = RetryProfileName.ExternalApiStandard, CreatedUtc = now, UpdatedUtc = now, Waits = waits };
        var store = new InMemoryRunStore();
        await store.CreateAsync(run);
        waits.Clear();
        var saved = (await store.GetAsync(run.TenantId, run.Id))!;
        Assert.Equal(RunStatus.Running, saved.Status);
        Assert.Equal(new[] { "approval", "delay" }, saved.Waits.Select(wait => wait.StepId));
        Assert.Equal(now, saved.Waits[0].BeganUtc);
        Assert.Null(saved.Waits[0].WakeUpUtc);
        Assert.Equal(now.AddHours(1), saved.Waits[1].WakeUpUtc);
        Assert.Equal(new[] { RunWaitReason.HumanTask, RunWaitReason.Timer, RunWaitReason.Dependency }, Enum.GetValues<RunWaitReason>());
        var held = saved.Waits[1].Hold();
        Assert.True(held.IsHeld);
        Assert.False(held.Release().IsHeld);
        Assert.Equal(now.AddHours(1), held.Release().WakeUpUtc);
        if (saved.Waits is IList<RunWait> exported) exported[0] = new("tampered", RunWaitReason.Dependency, now);
        Assert.Equal("approval", (await store.GetAsync(run.TenantId, run.Id))!.Waits[0].StepId);
    }

    [Fact]
    public async Task Add_refuses_duplicate_step_ids_so_a_held_first_wait_cannot_starve_a_later_due_timer()
    {
        // Oracle: M11 ruling on PR #470: one step id identifies one wait per run.
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var run = new RunRecord { Id = RunId.New(new RunKind("workflow-run")), TenantId = new TenantId("one"), Status = RunStatus.Running, RetryProfile = RetryProfileName.ExternalApiStandard, CreatedUtc = now, UpdatedUtc = now,
            Waits = [new RunWait("step", RunWaitReason.HumanTask, now).Hold(), new("step", RunWaitReason.Timer, now, now)] };
        var store = new InMemoryRunStore();

        var refusal = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => store.CreateAsync(run).AsTask());

        Assert.Equal("execution.wait_duplicate", refusal.Code);
        Assert.Null(await store.GetAsync(run.TenantId, run.Id));
    }

    [Fact]
    public async Task Update_refuses_duplicate_step_ids_so_a_held_first_wait_cannot_starve_a_later_due_timer()
    {
        // Oracle: M11 ruling on PR #470; a refused update must preserve the saved wait and version.
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var first = new RunWait("step", RunWaitReason.Timer, now, now).Hold();
        var run = new RunRecord { Id = RunId.New(new RunKind("workflow-run")), TenantId = new TenantId("one"), Status = RunStatus.Running, RetryProfile = RetryProfileName.ExternalApiStandard, CreatedUtc = now, UpdatedUtc = now, Waits = [first] };
        var store = new InMemoryRunStore();
        await store.CreateAsync(run);
        var update = run with { Version = 2, Waits = [first, new("step", RunWaitReason.Timer, now, now)] };

        var refusal = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => store.UpdateAsync(update, 1).AsTask());

        Assert.Equal("execution.wait_duplicate", refusal.Code);
        var saved = (await store.GetAsync(run.TenantId, run.Id))!;
        Assert.Equal(1, saved.Version);
        Assert.Equal(first, Assert.Single(saved.Waits));
    }

    [Fact(DisplayName = "execution-runtime-ck-14: malformed waits are refused at registration")]
    public void Invalid_waits()
    {
        // Oracle: closed reason and absolute UTC requirement. Mutation: remove constructor validation.
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => new RunWait(" ", RunWaitReason.Timer, now, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunWait("step", (RunWaitReason)99, now));
        Assert.Throws<ArgumentException>(() => new RunWait("step", RunWaitReason.Timer, now));
        Assert.Throws<ArgumentException>(() => new RunWait("step", RunWaitReason.Dependency, now, now));
        Assert.Throws<ArgumentException>(() => new RunWait("step", RunWaitReason.Timer, now, now.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => new RunWait("step", RunWaitReason.Dependency, now.ToOffset(TimeSpan.FromHours(1))));
    }
}
