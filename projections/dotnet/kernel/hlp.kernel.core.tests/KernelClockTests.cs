using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class KernelClockTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2030-01-02T03:04:05Z");

    [Fact]
    public async Task CapturedTimestampCannotChangeAuthoritativeTimeOrExpiry()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));
        var stamped = await clock.ResolveRecordedAtAsync(new("actor", CapturedAt: Now.AddDays(-30)));

        Assert.Equal(Now, stamped);
        Assert.True(clock.IsExpired(Now));
        Assert.False(clock.IsExpired(Now.AddTicks(1)));
    }

    [Fact]
    public async Task BackdatingWithoutCapabilityRefusesByStableName()
    {
        var clock = new KernelClock(new FixedTimeProvider(Now));
        var exception = await Assert.ThrowsAsync<KernelClockRefusalException>(async () =>
            await clock.ResolveRecordedAtAsync(new("actor", Now.AddDays(-30))));

        Assert.Equal(KernelClockErrors.BackdateCapabilityRequired, exception.Code);
    }

    [Fact]
    public async Task PermittedBackdatingUsesTheExplicitlyAdmittedDate()
    {
        var admitted = Now.AddDays(-30);
        var clock = new KernelClock(new FixedTimeProvider(Now));

        Assert.Equal(admitted, await clock.ResolveRecordedAtAsync(new("actor", admitted), new AllowBackdate()));
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
