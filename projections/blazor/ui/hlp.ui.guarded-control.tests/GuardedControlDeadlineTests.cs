using Bunit;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;
using Clock = Harborline.UIAdapters.Blazor.Tests.GuardedControlAnnouncementTests;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// The arm window is fixed at five seconds from the arming activation. The countdown runs on timer
/// callbacks, which can arrive late (a busy renderer, or WebAssembly's single thread behind a long
/// task), so the deadline is checked when the commit activation arrives: one that arrives after five
/// seconds recovers with reason timeout instead of committing. The React lane asserts the same
/// (GuardedControl.arm-window.test.tsx, A4).
/// </summary>
public sealed class GuardedControlDeadlineTests : BunitContext
{
    public GuardedControlDeadlineTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void RecoversInsteadOfCommittingWhenTheActivationArrivesAfterTheWindow()
    {
        var commits = 0;
        var events = new List<GuardedControlEvent>();
        var clock = new HeldClock();
        var cut = RenderGuard(() => commits++, events);
        Clock.UseClock(cut.Instance, clock);
        cut.Find("button").Click();
        Assert.Equal(GuardedControlState.Armed, cut.Instance.State);

        clock.Elapsed += TimeSpan.FromSeconds(10);
        cut.Find("button").Click();

        Assert.Equal(0, commits);
        Assert.Equal(GuardedControlState.Covered, cut.Instance.State);
        Assert.Equal(GuardedControlRecoveryReason.Timeout, Assert.Single(events.OfType<GuardedControlRecovered>()).Reason);
        Assert.Equal("Guard restored for ⁨Publish⁩.", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void StillCommitsAnActivationInsideTheWindow()
    {
        var commits = 0;
        var clock = new HeldClock();
        var cut = RenderGuard(() => commits++, []);
        Clock.UseClock(cut.Instance, clock);
        cut.Find("button").Click();

        clock.Elapsed += TimeSpan.FromMilliseconds(4900);
        cut.Find("button").Click();

        Assert.Equal(1, commits);
    }

    // The window runs on the monotonic clock: a wall-clock step (NTP, a manual change) neither extends nor
    // shortens it.
    [Theory]
    [InlineData(60)]
    [InlineData(-60)]
    public void AWallClockStepDoesNotMoveTheWindow(int stepSeconds)
    {
        var commits = 0;
        var clock = new HeldClock();
        var cut = RenderGuard(() => commits++, []);
        Clock.UseClock(cut.Instance, clock);
        cut.Find("button").Click();

        clock.Now += TimeSpan.FromSeconds(stepSeconds);
        clock.Elapsed += TimeSpan.FromSeconds(1);
        cut.Find("button").Click();
        Assert.Equal(1, commits);

        var late = RenderGuard(() => commits++, []);
        Clock.UseClock(late.Instance, clock);
        late.Find("button").Click();
        clock.Now -= TimeSpan.FromSeconds(stepSeconds);
        clock.Elapsed += TimeSpan.FromSeconds(6);
        late.Find("button").Click();
        Assert.Equal(1, commits);
        Assert.Equal(GuardedControlState.Covered, late.Instance.State);
    }

    private IRenderedComponent<HarborlineGuardedControl> RenderGuard(Action commit, List<GuardedControlEvent> events) =>
        Render<HarborlineGuardedControl>(p => p
            .Add(x => x.Action, "packages.export").Add(x => x.ClassificationId, "new-record-type")
            .Add(x => x.Label, "Publish").Add(x => x.OnCommit, commit)
            .Add(x => x.OnEvent, e => events.Add(e)));

    /// <summary>
    /// A clock whose wall time (Now) and monotonic time (Elapsed) the test sets independently, and whose
    /// timers never fire, as when callbacks run late.
    /// </summary>
    private sealed class HeldClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        public TimeSpan Elapsed { get; set; } = TimeSpan.FromHours(1);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new HeldTimer();

        private sealed class HeldTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
