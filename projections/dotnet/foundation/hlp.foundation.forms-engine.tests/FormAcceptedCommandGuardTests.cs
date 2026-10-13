using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Xunit;
using static Harborline.Foundation.Forms.Engine.Tests.AcceptedCommandFixtures;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// Review M5 (decision 1 reading): an accepted command's submit-gate decider runs while the store's exclusion scope is held.
/// Deadlock is structurally impossible: a call back into a built-in submission store from that flow is refused at once with
/// <c>form.engine.store-reentrant</c>, and a decision past <see cref="FormEngineOptions.AcceptanceDecisionTimeout"/> is
/// abandoned with <c>form.engine.resource-bound</c>, and so is a mapped-body digest past the same bound (review R2-L3); either
/// way the scope is released and nothing persists. An ordinary
/// submit's decider runs outside the scope and may read the store. Every wait is bounded so a regression fails instead of
/// hanging. Oracles: literal codes and store counts.
/// </summary>
public sealed class FormAcceptedCommandGuardTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact(DisplayName = "M5: a decider that calls back into the store during an acceptance is refused with form.engine.store-reentrant, not deadlocked")]
    public async Task ReentrantDeciderIsRefusedWithANamedCode()
    {
        var decider = new Decider();
        var harness = await AcceptingAsync(decider: decider);
        decider.Before = async _ => await harness.Store.GetAsync(new TenantId("tenant-engine"), new EntityId("harborline", "forms", "any"));
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var refused = await Assert.ThrowsAsync<FormEngineStoreReentrancyException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")).AsTask().WaitAsync(Bound));

        Assert.Equal("form.engine.store-reentrant", refused.Code);
        // The scope was released: the store answers at once, and nothing was written.
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync().AsTask().WaitAsync(Bound));
    }

    [Fact(DisplayName = "M5: an ordinary submit's decider runs outside the store scope and may read the store")]
    public async Task OrdinaryDeciderMayReadTheStore()
    {
        var decider = new Decider();
        var harness = await Harness.CreateAsync(definitionFactory: Gated, submitGates: Port(decider));
        decider.Before = async _ => await harness.Store.GetAsync(new TenantId("tenant-engine"), new EntityId("harborline", "forms", "any"));
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")).AsTask().WaitAsync(Bound);

        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "M5: a decision that exceeds the acceptance bound is abandoned with form.engine.resource-bound and the scope released")]
    public async Task SlowDeciderIsBounded()
    {
        var decider = new Decider { Before = _ => Task.Delay(TimeSpan.FromSeconds(30)) };
        var harness = await AcceptingAsync(decider: decider,
            options: new FormEngineOptions { AcceptanceDecisionTimeout = TimeSpan.FromMilliseconds(100) });
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var refused = await Assert.ThrowsAsync<FormEngineResourceBoundException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")).AsTask().WaitAsync(Bound));

        Assert.Equal("form.engine.resource-bound", refused.Code);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync().AsTask().WaitAsync(Bound));
    }

    [Fact(DisplayName = "R2-L3: a mapped-body digest that exceeds the acceptance bound is abandoned with form.engine.resource-bound, its token cancelled and the scope released")]
    public async Task SlowBodyDigestIsBounded()
    {
        var slow = new SlowBodiesSink();
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(new Decider()),
            recordTypePolicies: new FormOnlyReader(), projectionSink: slow,
            options: new FormEngineOptions { AcceptanceDecisionTimeout = TimeSpan.FromMilliseconds(100) });
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var refused = await Assert.ThrowsAsync<FormEngineResourceBoundException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")).AsTask().WaitAsync(Bound));

        Assert.Equal("form.engine.resource-bound", refused.Code);
        Assert.True(slow.Token.IsCancellationRequested);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync().AsTask().WaitAsync(Bound));
    }

    public static TheoryData<string> BoundedCalls => ["gate", "digest"];

    [Theory(DisplayName = "F2: when the acceptance bound fires and the bounded call then completes before the bound's continuation runs, the submit is still refused form.engine.resource-bound, for the gate and the digest alike")]
    [MemberData(nameof(BoundedCalls))]
    public async Task BoundRace_DeadlineFiresBeforeTheCallCompletes_IsResourceBound(string bounded)
    {
        var clock = new RacingClock();
        var gate = bounded == "gate" ? (IFormSubmitGateAccess)new RacingGate(clock) : Port(new Decider());
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: gate, recordTypePolicies: new FormOnlyReader(),
            projectionSink: new RacingBodiesSink(bounded == "digest" ? clock : null), clock: clock);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var refused = await Assert.ThrowsAsync<FormEngineResourceBoundException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")).AsTask().WaitAsync(Bound));

        Assert.Equal("form.engine.resource-bound", refused.Code);
        // The race took place: the bound's timer fired once, and the call had completed successfully before the refusal.
        Assert.Equal(1, clock.Fired);
        Assert.True(clock.CompletedCall is { IsCompletedSuccessfully: true });
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync().AsTask().WaitAsync(Bound));
    }

    public static TheoryData<string> InvalidOptions => ["zero-timeout", "zero-retry-limit"];

    [Theory(DisplayName = "M2/M5: the acceptance bound and the binding retry limit must be positive")]
    [MemberData(nameof(InvalidOptions))]
    public async Task InvalidGuardOptionsAreRefused(string shape)
    {
        var options = shape == "zero-timeout"
            ? new FormEngineOptions { AcceptanceDecisionTimeout = TimeSpan.Zero }
            : new FormEngineOptions { AcceptedCommandBindingRetryLimit = 0 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await AcceptingAsync(options: options));
    }

    // A sink whose mapped-body digest outlasts any bound unless its token is cancelled; it records the token it was given.
    private sealed class SlowBodiesSink : Projection.IFormProjectionSink, IFormAcceptedRecordBodies
    {
        public CancellationToken Token { get; private set; }

        public async ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new("records-write-intent/1", "never");
        }

        public ValueTask<Projection.FormProjectionDeliveryResult> DeliverAsync(Persistence.FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Projection.FormProjectionDeliveryResult([]));
    }

    // Review F2: a fake clock that stages the bound's race deterministically, on one thread. Its instant is the fixture's. A
    // timer created while a call has armed it is that call's acceptance bound: it fires as it is created, so the bound has
    // expired, and only then is the call completed, so the call has completed before the bound's continuation can run. Any
    // other timer never fires.
    private sealed class RacingClock : TimeProvider
    {
        private Func<Task>? _armed;

        public int Fired { get; private set; }
        public Task? CompletedCall { get; private set; }

        public void Arm(Func<Task> completeCall) => _armed = completeCall;

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Exchange(ref _armed, null) is { } completeCall)
            {
                callback(state);
                Fired++;
                CompletedCall = completeCall();
            }
            return new InertTimer();
        }

        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // A submit-gate port whose decision is pending until the racing clock completes it, with a refusing decision, so a
    // submit that used the late decision would be denied rather than resource-bound.
    private sealed class RacingGate(RacingClock clock) : IFormSubmitGateAccess
    {
        public ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, Harborline.Foundation.Forms.Models.FormDefinition definition,
            Harborline.Contracts.Authorization.SubmitGate gate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("the engine asks DecideAsync");

        public ValueTask<FormSubmitGateDecision> DecideAsync(FormSubmitGateRequest request, CancellationToken cancellationToken = default)
        {
            var decision = new TaskCompletionSource<FormSubmitGateDecision>();
            clock.Arm(() =>
            {
                decision.SetResult(new FormSubmitGateDecision(request.Intent, false, null));
                return decision.Task;
            });
            return new(decision.Task);
        }
    }

    // A sink whose mapped-body digest is pending until the racing clock completes it; without a clock it answers at once.
    private sealed class RacingBodiesSink(RacingClock? clock) : Projection.IFormProjectionSink, IFormAcceptedRecordBodies
    {
        public ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default)
        {
            var digest = new FormAcceptedRecordBodyDigest("records-write-intent/1", "body-digest");
            if (clock is null) return ValueTask.FromResult(digest);
            var body = new TaskCompletionSource<FormAcceptedRecordBodyDigest>();
            clock.Arm(() =>
            {
                body.SetResult(digest);
                return body.Task;
            });
            return new(body.Task);
        }

        public ValueTask<Projection.FormProjectionDeliveryResult> DeliverAsync(Persistence.FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Projection.FormProjectionDeliveryResult([]));
    }
}
