using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Harborline.Foundation.Localization;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Harborline.UIAdapters.Blazor.Localization;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// T-1105: the live region carries the whole message of the state the control is in. Recovery is
/// ordered transition-armed-to-covered, restore-focus-when-safe, announce-whole-message,
/// report-one-recovery, so the recovered (or settled) message and the focus move belong to the
/// transition itself, not to whatever render happens to come after it. The timeout, host and
/// rejected-commit paths run outside an event handler's automatic render, which is where a message
/// set after the transition's render stayed stale. The armed instruction and its countdown are
/// replayed from the fixture the React lane replays too (duration-v1.json).
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
        // Navigation restores focus now: one move from arming, one from the recovery's own render.
        cut.WaitForAssertion(() => Assert.Equal(2, FocusCalls));
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
        // Focus returns with the settlement's own render: one move from arming, one from settling. A
        // wait would rethrow the routed failure, and the render has already happened.
        Assert.Equal(2, FocusCalls);
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

    // T-1105 review: both lanes replay conformance/hlp.ui.guarded-control/duration-v1.json (the React
    // half is GuardedControl.native.test.tsx), so one catalog, locale and host formatter give one armed
    // instruction and one countdown in both. A case the fixture does not declare is an error.
    private static string Repo(string relative) => Path.Combine(
        Environment.GetEnvironmentVariable("HARBORLINE_REPO_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../")),
        relative);

    private static readonly JsonElement DurationFixture =
        JsonElement.Parse(File.ReadAllText(Repo("conformance/hlp.ui.guarded-control/duration-v1.json")));

    public static TheoryData<string> DurationCaseIds()
    {
        var ids = new TheoryData<string>();
        foreach (var fixture in DurationFixture.GetProperty("cases").EnumerateArray()) ids.Add(fixture.GetProperty("id").GetString()!);
        return ids;
    }

    [Theory]
    [MemberData(nameof(DurationCaseIds))]
    public async Task DurationFixtureReplays(string caseId)
    {
        var fixture = DurationFixture.GetProperty("cases").EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == caseId);
        var clock = new ManualClock();
        var provider = Render<HarborlineLocaleProvider>(p => p
            .Add(x => x.Locale, fixture.GetProperty("locale").GetString()!)
            .Add(x => x.Catalog, fixture.GetProperty("catalog").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!))
            .Add(x => x.NumberFormatter, fixture.TryGetProperty("hostNumberingSystem", out var system) ? HostFormatter(system.GetString()!) : null)
            .AddChildContent<HarborlineGuardedControl>(guard => guard
                .Add(x => x.Action, "packages.export")
                .Add(x => x.ClassificationId, "new-record-type")
                .Add(x => x.Label, fixture.GetProperty("label").GetString()!)
                .Add(x => x.OnCommit, () => { })));
        var cut = provider.FindComponent<HarborlineGuardedControl>();
        UseClock(cut.Instance, clock);

        foreach (var step in fixture.GetProperty("steps").EnumerateArray().Select(value => value.GetString()))
        {
            switch (step)
            {
                case "arm":
                    cut.WaitForAssertion(() => Assert.Equal("covered", GuardState(cut)));
                    cut.Find("button").Click();
                    break;
                case "tick":
                    await clock.FireNextAsync();
                    // The second is applied once the guard waits on its next second, or has recovered.
                    await WaitUntilAsync(() => clock.HasScheduled || cut.Instance.State != GuardedControlState.Armed);
                    break;
                case "escape":
                    await cut.Find("button").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
                    break;
                default:
                    throw new InvalidOperationException($"unknown-step: {step}");
            }
        }

        var expected = fixture.TryGetProperty("divergence", out var divergence) && divergence.GetProperty("lane").GetString() == "blazor"
            ? divergence.GetProperty("expected")
            : fixture.GetProperty("expected");
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(expected.GetProperty("status").GetString(), Status(cut));
            Assert.Equal(expected.GetProperty("timer").GetString(), cut.Find("[role=timer]").TextContent);
            Assert.Equal(expected.GetProperty("timer").GetString(), cut.Find("[role=timer]").GetAttribute("aria-label"));
        });
    }

    // The fixture's host formatter: the digits of one CLDR numbering system, whatever the locale.
    private static HarborlineNumberFormatter HostFormatter(string numberingSystem)
    {
        var zero = numberingSystem switch
        {
            "latn" => '0',
            "arab" => '\u0660',
            _ => throw new InvalidOperationException($"unknown-numbering-system: {numberingSystem}"),
        };
        return request => string.Concat(request.Value.ToString(CultureInfo.InvariantCulture)
            .Select(digit => char.IsAsciiDigit(digit) ? (char)(zero + (digit - '0')) : digit));
    }

    /// <summary>Waits, on real time, until the condition holds.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The guard did not settle after the delay fired.");
            await Task.Delay(10);
        }
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

        /// <summary>Whether the component is waiting on a delay that has not fired yet.</summary>
        public bool HasScheduled { get { lock (gate) return scheduled.Count > 0; } }

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
