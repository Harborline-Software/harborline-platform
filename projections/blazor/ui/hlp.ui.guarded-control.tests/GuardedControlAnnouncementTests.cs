using System.Globalization;
using System.Reflection;
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
/// set after the transition's render stayed stale. The armed instruction reads as one whole message
/// with an amount of time after "within", and every arm states the full window.
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

    [Fact]
    public async Task CountdownReachesASingularSecond()
    {
        var clock = new ManualClock();
        var cut = RenderGuard();
        UseClock(cut.Instance, clock);
        cut.Find("button").Click();

        for (var second = 0; second < 4; second++) await TickAsync(cut, clock);

        cut.WaitForAssertion(() => Assert.Equal("1 second", cut.Find("[role=timer]").TextContent));
        Assert.Equal($"Armed for {Isolated}. Activate again within 5 seconds to commit.", Status(cut));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("escape")]
    public async Task EveryArmStatesTheFullWindow(string recovery)
    {
        var clock = new ManualClock();
        var cut = RenderGuard();
        UseClock(cut.Instance, clock);
        cut.Find("button").Click();
        for (var second = 0; second < (recovery == "timeout" ? 5 : 2); second++) await TickAsync(cut, clock);
        if (recovery == "escape") await cut.Find("button").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        cut.WaitForAssertion(() => Assert.Equal("covered", GuardState(cut)));

        cut.Find("button").Click();

        Assert.Equal($"Armed for {Isolated}. Activate again within 5 seconds to commit.", Status(cut));
        Assert.Equal("5 seconds", cut.Find("[role=timer]").TextContent);
    }

    // The host catalog is resolved first, for the whole message and for the duration form inside it.
    [Fact]
    public async Task HostCatalogLocalizesTheArmedInstructionAndDuration()
    {
        var clock = new ManualClock();
        var cut = RenderInLocale("fr-FR", "Publier", new Dictionary<string, string>
        {
            ["chrome.guard.armed.whole"] = "Armé pour {action}. Activez de nouveau dans un délai de {duration} pour valider.",
            ["chrome.guard.duration.one"] = "{seconds} seconde",
            ["chrome.guard.duration.other"] = "{seconds} secondes",
        });
        UseClock(cut.Instance, clock);
        cut.Find("button").Click();

        Assert.Equal("Armé pour \u2068Publier\u2069. Activez de nouveau dans un délai de 5 secondes pour valider.", Status(cut));
        Assert.Equal("5 secondes", cut.Find("[role=timer]").TextContent);
        for (var second = 0; second < 4; second++) await TickAsync(cut, clock);
        cut.WaitForAssertion(() => Assert.Equal("1 seconde", cut.Find("[role=timer]").TextContent));
    }

    // The locale provider's plural selection picks the catalog form, then other. Its .NET rules cover
    // ar, pl, ru and ja, and one/other elsewhere; full CLDR cardinal rules in the provider, and one
    // duration source for both lanes, are T-1105's deferred duration-parity work.
    [Theory]
    [InlineData(true, 0, "٥ ثوانٍ")]
    [InlineData(true, 3, "ثانيتين")]
    [InlineData(true, 4, "ثانية واحدة")]
    [InlineData(false, 0, "٥ ثانية")]
    public async Task CatalogDurationFormFollowsTheLocalePluralCategory(bool withFew, int ticks, string timer)
    {
        var catalog = new Dictionary<string, string>
        {
            ["chrome.guard.armed.whole"] = "تم تجهيز {action}. فعّل مرة أخرى خلال {duration} للتأكيد.",
            ["chrome.guard.duration.one"] = "ثانية واحدة",
            ["chrome.guard.duration.two"] = "ثانيتين",
            ["chrome.guard.duration.other"] = "{seconds} ثانية",
        };
        if (withFew) catalog["chrome.guard.duration.few"] = "{seconds} ثوانٍ";
        var clock = new ManualClock();
        var cut = RenderInLocale("ar-SA", "تصدير", catalog, HostFormatter("arab"));
        UseClock(cut.Instance, clock);
        cut.Find("button").Click();

        for (var second = 0; second < ticks; second++) await TickAsync(cut, clock);

        cut.WaitForAssertion(() => Assert.Equal(timer, cut.Find("[role=timer]").TextContent));
        Assert.Equal(timer, cut.Find("[role=timer]").GetAttribute("aria-label"));
        Assert.Equal($"تم تجهيز \u2068تصدير\u2069. فعّل مرة أخرى خلال {(withFew ? "٥ ثوانٍ" : "٥ ثانية")} للتأكيد.", Status(cut));
    }

    [Fact]
    public void HostNumberFormatterWritesTheDurationNumber()
    {
        var cut = RenderInLocale("en", "Publish", new Dictionary<string, string>(), HostFormatter("arab"));
        cut.Find("button").Click();

        Assert.Equal("Armed for \u2068Publish\u2069. Activate again within ٥ seconds to commit.", Status(cut));
        Assert.Equal("٥ seconds", cut.Find("[role=timer]").TextContent);
    }

    // Known limitation, recorded rather than silent (T-1105). With no host catalog in a non-English
    // locale, this lane announces the English whole message with the English duration, while the
    // React lane places the locale's unit pattern inside the English sentence ("within 5 secondes").
    // This test pins today's output so any change to it is deliberate. One duration source for both
    // lanes is T-1105's deferred duration-parity work: it depends on round-2 batch b18 (CLDR plural
    // rules in the locale provider) reaching main and on an owner decision.
    [Fact]
    public void NoCatalogFallbackIsEnglishInANonEnglishLocale()
    {
        var cut = RenderInLocale("fr-FR", "Publish", new Dictionary<string, string>());
        cut.Find("button").Click();

        Assert.Equal("Armed for ⁨Publish⁩. Activate again within 5 seconds to commit.", Status(cut));
        Assert.Equal("5 seconds", cut.Find("[role=timer]").TextContent);
    }

    // Known limitation (T-1105, deferred duration parity): a host catalog that localizes the whole armed
    // message but supplies no chrome.guard.duration.* keys leaves Blazor's duration in English, while React
    // writes the CLDR unit pattern ("5 secondes"). Pinned so the divergence is recorded, not silent; it
    // closes with CLDR plural rules in the Blazor locale provider (round-2 b18) and an owner decision.
    [Fact]
    public void HostCatalogWithoutDurationKeysLeavesTheDurationEnglish()
    {
        var cut = RenderInLocale("fr-FR", "Publier", new Dictionary<string, string>
        {
            ["chrome.guard.armed.whole"] = "Armé pour {action}. Activez de nouveau dans un délai de {duration} pour valider.",
        });
        cut.Find("button").Click();

        Assert.Equal("Armé pour ⁨Publier⁩. Activez de nouveau dans un délai de 5 seconds pour valider.", Status(cut));
        Assert.Equal("5 seconds", cut.Find("[role=timer]").TextContent);
    }

    private IRenderedComponent<HarborlineGuardedControl> RenderInLocale(
        string locale, string label, IReadOnlyDictionary<string, string> catalog, HarborlineNumberFormatter? numberFormatter = null) =>
        Render<HarborlineLocaleProvider>(p => p
            .Add(x => x.Locale, locale)
            .Add(x => x.Catalog, catalog)
            .Add(x => x.NumberFormatter, numberFormatter)
            .AddChildContent<HarborlineGuardedControl>(guard => guard
                .Add(x => x.Action, "packages.export")
                .Add(x => x.ClassificationId, "new-record-type")
                .Add(x => x.Label, label)
                .Add(x => x.OnCommit, () => { })))
        .FindComponent<HarborlineGuardedControl>();

    /// <summary>Lets one second of the arm window elapse and waits until the guard has applied it.</summary>
    internal static async Task TickAsync(IRenderedComponent<HarborlineGuardedControl> cut, ManualClock clock)
    {
        await clock.FireNextAsync();
        // The second is applied once the guard waits on its next second, or has recovered.
        await WaitUntilAsync(() => clock.HasScheduled || cut.Instance.State != GuardedControlState.Armed);
    }

    // A host formatter that writes the digits of one CLDR numbering system, whatever the locale.
    internal static HarborlineNumberFormatter HostFormatter(string numberingSystem)
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
    internal static void UseClock(HarborlineGuardedControl guard, TimeProvider clock) =>
        (typeof(HarborlineGuardedControl).GetField("clock", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(HarborlineGuardedControl), "clock")).SetValue(guard, clock);

    /// <summary>
    /// A clock whose timers fire only when the test says so, one scheduled delay at a time. Its monotonic
    /// time (GetTimestamp) stands still between firings and moves to a timer's due time when it fires, so
    /// the guard's deadline arithmetic sees exactly the delays the test lets elapse; Advance moves it
    /// without firing anything (a slow observer, or late callbacks).
    /// </summary>
    internal sealed class ManualClock : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> scheduled = [];
        private long now = TimeSpan.FromHours(1).Ticks;

        public override long GetTimestamp() { lock (gate) return now; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>Moves the monotonic time on without firing any timer.</summary>
        public void Advance(TimeSpan by) { lock (gate) now += by.Ticks; }

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
                        next = scheduled.MinBy(timer => timer.Due);
                        scheduled.Remove(next!);
                        now = Math.Max(now, next!.Due);
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
            public long Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock.gate)
                {
                    clock.scheduled.Remove(this);
                    if (dueTime != Timeout.InfiniteTimeSpan)
                    {
                        Due = clock.now + dueTime.Ticks;
                        clock.scheduled.Add(this);
                    }
                }
                return true;
            }

            public void Fire() => callback(state);
            public void Dispose() { lock (clock.gate) clock.scheduled.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
