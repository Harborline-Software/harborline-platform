using System.Reflection;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// T-1105: the live region carries the whole message of the state the control is in. Recovery is
/// ordered transition-armed-to-covered, restore-focus-when-safe, announce-whole-message,
/// report-one-recovery, so the recovered (or settled) message and the focus move belong to the
/// transition itself, not to whatever render happens to come after it. The timeout, host and
/// rejected-commit paths run outside an event handler's automatic render, which is where a message
/// set after the transition's render stayed stale.
/// </summary>
public sealed class GuardedControlAnnouncementTests : BunitContext
{
    private const string ModulePath = "./_content/Harborline.UIAdapters.Blazor/guarded-control.js";
    private const string Isolated = "\u2068Sign and save\u2069";
    private readonly BunitJSModuleInterop module;

    public GuardedControlAnnouncementTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<HarborlineGuardedControl> RenderGuard(Func<Task>? commit = null, List<GuardedControlEvent>? events = null) =>
        Render<HarborlineGuardedControl>(p => p
            .Add(x => x.Action, "packages.export")
            .Add(x => x.ClassificationId, "new-record-type")
            .Add(x => x.Label, "Sign and save")
            .Add(x => x.OnCommit, () => commit?.Invoke() ?? Task.CompletedTask)
            .Add(x => x.OnEvent, e => events?.Add(e)));

    private static string Status(IRenderedComponent<HarborlineGuardedControl> cut) => cut.Find("[role=status]").TextContent;
    private static string GuardState(IRenderedComponent<HarborlineGuardedControl> cut) => cut.Find("button").GetAttribute("data-guard-state")!;
    private int FocusCalls => module.Invocations["focus"].Count;

    [Fact]
    public async Task TimeoutAnnouncesTheRecoveredMessageWhileCovered()
    {
        var clock = new ManualClock();
        var events = new List<GuardedControlEvent>();
        var cut = RenderGuard(events: events);
        UseClock(cut.Instance, clock);
        cut.Find("button").Click();
        Assert.Equal($"Armed for {Isolated}. Activate again within 5 seconds to commit.", Status(cut));

        for (var second = 0; second < 5; second++) await clock.FireNextAsync();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("covered", GuardState(cut));
            Assert.Equal($"Guard restored for {Isolated}.", Status(cut));
        });
        Assert.Equal(GuardedControlRecoveryReason.Timeout, Assert.Single(events.OfType<GuardedControlRecovered>()).Reason);
    }

    [Fact]
    public async Task TimeoutRestoresFocusToTheCoveredAction()
    {
        var clock = new ManualClock();
        var cut = RenderGuard();
        UseClock(cut.Instance, clock);
        cut.Find("button").Click();
        Assert.Equal(1, FocusCalls);

        for (var second = 0; second < 5; second++) await clock.FireNextAsync();

        cut.WaitForAssertion(() => Assert.Equal("covered", GuardState(cut)));
        cut.WaitForAssertion(() => Assert.Equal(2, FocusCalls));
    }

    [Fact]
    public async Task HostRecoveryAnnouncesTheRecoveredMessageWhileCovered()
    {
        var cut = RenderGuard();
        cut.Find("button").Click();

        await cut.InvokeAsync(() => cut.Instance.RecoverFromHostAsync("navigation"));

        Assert.Equal("covered", GuardState(cut));
        Assert.Equal($"Guard restored for {Isolated}.", Status(cut));
    }

    // An asynchronous rejection: the click handler's own render ran when the operation started, and a
    // faulted handler gets no render afterwards, so only the settlement's transition can carry the
    // message. The caller's failure then reaches the renderer, the native exception route.
    [Fact]
    public async Task AsynchronousRejectionAnnouncesTheRejectedMessageWhileCovered()
    {
        var operation = new TaskCompletionSource();
        var cut = RenderGuard(() => operation.Task);
        cut.Find("button").Click();
        var commit = cut.Find("button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Equal("committing", GuardState(cut));

        operation.SetException(new InvalidOperationException("caller failure"));
        var routed = await Renderer.UnhandledException.WaitAsync(TimeSpan.FromSeconds(5));
        await commit;

        Assert.Equal("caller failure", Assert.IsType<InvalidOperationException>(routed).Message);
        Assert.Equal("covered", GuardState(cut));
        Assert.Equal($"Could not complete {Isolated}. Guard restored.", Status(cut));
        Assert.DoesNotContain("caller failure", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ArmedInstructionIsOneGrammaticalWholeMessage()
    {
        var cut = RenderGuard();
        cut.Find("button").Click();

        Assert.Equal($"Armed for {Isolated}. Activate again within 5 seconds to commit.", Status(cut));
        Assert.Equal("5 seconds", cut.Find("[role=timer]").TextContent);
        Assert.DoesNotContain("within in", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HostCatalogLocalizesTheArmedInstructionAndDuration()
    {
        var provider = Render<HarborlineLocaleProvider>(p => p
            .Add(x => x.Locale, "fr-FR")
            .Add(x => x.Catalog, new Dictionary<string, string>
            {
                ["chrome.guard.armed.whole"] = "Armé pour {action}. Activez de nouveau dans un délai de {duration} pour valider.",
                ["chrome.guard.duration.other"] = "{seconds} secondes",
            })
            .AddChildContent<HarborlineGuardedControl>(guard => guard
                .Add(x => x.Action, "packages.export")
                .Add(x => x.ClassificationId, "new-record-type")
                .Add(x => x.Label, "Publier")
                .Add(x => x.OnCommit, () => { })));
        var cut = provider.FindComponent<HarborlineGuardedControl>();
        cut.Find("button").Click();

        Assert.Equal("Armé pour \u2068Publier\u2069. Activez de nouveau dans un délai de 5 secondes pour valider.", Status(cut));
        Assert.Equal("5 secondes", cut.Find("[role=timer]").TextContent);
    }

    // The arm window runs on the component's private clock, which is the system clock outside tests.
    private static void UseClock(HarborlineGuardedControl guard, TimeProvider clock) =>
        (typeof(HarborlineGuardedControl).GetField("clock", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(HarborlineGuardedControl), "clock")).SetValue(guard, clock);

    /// <summary>A clock whose timers fire only when the test says so, one scheduled delay at a time.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> scheduled = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>Waits for the component to schedule its next delay, then lets that delay elapse.</summary>
        public async Task FireNextAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                ManualTimer? next = null;
                lock (gate)
                {
                    if (scheduled.Count > 0)
                    {
                        next = scheduled[0];
                        scheduled.RemoveAt(0);
                    }
                }
                if (next is not null)
                {
                    next.Fire();
                    return;
                }
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The guard scheduled no delay to fire.");
                await Task.Delay(10);
            }
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock.gate)
                {
                    clock.scheduled.Remove(this);
                    if (dueTime != Timeout.InfiniteTimeSpan) clock.scheduled.Add(this);
                }
                return true;
            }

            public void Fire() => callback(state);
            public void Dispose() { lock (clock.gate) clock.scheduled.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
