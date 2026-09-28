using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class KernelClockTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2030-01-02T03:04:05Z");

    [Fact]
    public async Task Future_effective_from_is_admitted_without_capability()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));
        var effectiveFrom = Now.AddDays(30);

        Assert.Equal(effectiveFrom, await clock.ResolveEffectiveFromAsync("actor", Now, effectiveFrom));
    }

    [Fact]
    public async Task Past_effective_from_without_capability_refuses_by_name()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));
        var exception = await Assert.ThrowsAsync<KernelClockRefusalException>(async () =>
            await clock.ResolveEffectiveFromAsync("actor", Now, Now.AddDays(-30)));

        Assert.Equal(KernelClockErrors.BackdateCapabilityRequired, exception.Code);
    }

    [Fact]
    public async Task Effective_from_equal_to_admitted_instant_needs_no_capability()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));

        Assert.Equal(Now, await clock.ResolveEffectiveFromAsync("actor", Now, Now));
    }

    [Fact]
    public async Task Omitted_effective_from_is_the_admitted_instant()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));

        Assert.Equal(Now, await clock.ResolveEffectiveFromAsync("actor", Now, null));
    }

    [Fact]
    public async Task Permitted_backdating_uses_the_explicit_effective_from()
    {
        var effectiveFrom = Now.AddDays(-30);
        var clock = new KernelClock(new FixedTimeProvider(Now));

        Assert.Equal(effectiveFrom, await clock.ResolveEffectiveFromAsync("actor", Now, effectiveFrom, new AllowBackdate()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Effective_from_needs_a_named_actor(string actorId)
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(async () => await clock.ResolveEffectiveFromAsync(actorId, Now, null));
    }

    [Fact]
    public void Expiry_is_reached_exactly_at_the_expiry_instant()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));

        Assert.False(clock.IsExpired(Now.AddTicks(1)));
        Assert.True(clock.IsExpired(Now));
        Assert.True(clock.IsExpired(Now.AddTicks(-1)));
    }

    [Fact]
    public void Recorded_at_cannot_be_requested()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));

        Assert.Equal(Now, clock.GetUtcNow());
        Assert.DoesNotContain(typeof(KernelClock).GetMembers(), member => member.Name == "ResolveRecordedAtAsync");
        Assert.DoesNotContain(typeof(KernelClock).Assembly.GetTypes(), type => type.Name == "KernelStampRequest");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AllowBackdate : IKernelBackdateCapability
    {
        public ValueTask<bool> CanBackdateAsync(string actorId, DateTimeOffset requestedRecordedAt, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
