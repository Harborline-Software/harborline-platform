using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Scheduling;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SchedulerTests : BunitContext
{
    private static readonly DateTimeOffset Anchor = new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly SchedulerEvent[] Events = [new("a", "Review", Anchor, Anchor.AddHours(1)), new("all", "Holiday", Anchor, Anchor.AddDays(1), AllDay: true)];
    public SchedulerTests() { JSInterop.Mode = JSRuntimeMode.Loose; Services.AddSingleton<IMediaQueryObserver>(new FakeMedia()); }
    [Fact] public void FourViewsAndReadonlyEventStructureRender() { var cut = Render<HarborlineScheduler>(p => p.Add(x => x.Data, Events).Add(x => x.DefaultDate, Anchor).Add(x => x.Now, Anchor).Add(x => x.ReadOnly, true)); Assert.Equal(4, cut.FindAll("[data-toolbar-views] button").Count); Assert.Contains("Review", cut.Markup); Assert.Contains("Holiday", cut.Markup); Assert.Empty(cut.FindAll(".hl-scheduler__agenda-actions")); }
    [Fact] public void ControlledViewRequestsWithoutMutatingAndNarrowDefaultsAgenda() { SchedulerViewType? requested = null; var cut = Render<HarborlineScheduler>(p => p.Add(x => x.Data, Events).Add(x => x.View, SchedulerViewType.Day).Add(x => x.DefaultDate, Anchor).Add(x => x.Now, Anchor).Add(x => x.ViewChanged, value => requested = value)); cut.FindAll("[data-toolbar-views] button").Single(button => button.TextContent == "Month").Click(); Assert.Equal(SchedulerViewType.Month, requested); Assert.Equal("day", cut.Find(".hl-scheduler").GetAttribute("data-hl-view")); var narrow = Render<HarborlineScheduler>(p => p.Add(x => x.Data, Events).Add(x => x.DefaultDate, Anchor).Add(x => x.Now, Anchor).Add(x => x.Narrow, true)); Assert.Equal("agenda", narrow.Find(".hl-scheduler").GetAttribute("data-hl-view")); }
    [Fact] public void NavigationAndTodayRequestDates() { var requests = new List<DateTimeOffset>(); var cut = Render<HarborlineScheduler>(p => p.Add(x => x.Data, Events).Add(x => x.DefaultDate, Anchor).Add(x => x.Now, Anchor).Add(x => x.DateChanged, value => requests.Add(value))); cut.Find("[aria-label=Next]").Click(); cut.Find("[aria-label=Today]").Click(); Assert.Equal(2, requests.Count); Assert.Equal(Anchor.AddDays(7), requests[0]); }
    [Fact] public void RecurrenceParsesExpandsAndCaps() { var rule = SchedulerRecurrence.ParseRRule("FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=6"); Assert.NotNull(rule); var master = new SchedulerEvent("r", "Round", new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero), RecurrenceRule: "FREQ=DAILY"); var expanded = SchedulerRecurrence.Expand(master, master.Start, master.Start.AddYears(20)); Assert.Equal(SchedulerRecurrence.OccurrenceCap, expanded.Count); Assert.All(expanded, item => Assert.Equal("r", item.RecurrenceId)); }
    [Fact, Trait("ModuleConformance", "hlp.ui.scheduler")] public void SharedFixtureConforms() { AssertFixture("scheduler."); }
    private void AssertFixture(string prefix)
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var id = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith(prefix, id);
        if (id == "scheduler.anchor-now-action") { AssertNowAction(fixture.RootElement.GetProperty("expected")); return; }
        if (id != "scheduler.lane-classes") return;
        // 282 s8: this lane spelled the scroll box hl-scheduler__body, the month date a
        // hl-scheduler__month-date button, month chips hl-scheduler__month-event, the agenda
        // hl-scheduler__agenda-event/__agenda-main/__delete and the stacked recurrence choices
        // hl-scheduler__recurrence-actions, none of them defined by the authority stylesheet.
        // This asserts the one spelling here; the React case asserts the same row in the other lane.
        var expected = fixture.RootElement.GetProperty("expected");
        var crowded = Enumerable.Range(0, 5).Select(index => new SchedulerEvent($"m{index}", $"Event {index}", Anchor.AddHours(index), Anchor.AddHours(index + 1))).ToArray();
        var month = Render<HarborlineScheduler>(p => p.Add(x => x.Data, crowded).Add(x => x.DefaultDate, Anchor).Add(x => x.Now, Anchor).Add(x => x.View, SchedulerViewType.Month).Add(x => x.EventDeleted, _ => { }));
        foreach (var name in Classes(expected, "scrollClasses").Concat(Classes(expected, "monthClasses"))) Assert.NotEmpty(month.FindAll($".{name}"));
        Assert.NotNull(month.Find(".hl-scheduler__month-day").GetAttribute(expected.GetProperty("outsideMonthAttribute").GetString()!));
        Assert.NotEmpty(month.FindAll(".hl-scheduler__month-day > time"));
        // The authority styles the active view button by [aria-pressed='true']; a bool-valued
        // Blazor attribute never matches that selector, which is why the highlight was missing.
        Assert.NotEmpty(month.FindAll(expected.GetProperty("activeViewSelector").GetString()!));
        AssertRetired(expected, month.Markup);

        var agenda = Render<HarborlineScheduler>(p => p.Add(x => x.Data, crowded).Add(x => x.DefaultDate, Anchor).Add(x => x.Now, Anchor).Add(x => x.View, SchedulerViewType.Agenda).Add(x => x.EventDeleted, _ => { }));
        foreach (var name in Classes(expected, "agendaClasses")) Assert.NotEmpty(agenda.FindAll($".{name}"));
        Assert.Equal("OL", agenda.Find(".hl-scheduler__agenda").TagName);
        agenda.FindAll(".hl-scheduler__agenda-actions button")[0].Click();
        foreach (var name in Classes(expected, "editorFieldClasses")) Assert.NotEmpty(agenda.FindAll($".{name}"));
        AssertRetired(expected, agenda.Markup);
    }
    // 299: the authority styled hl-scheduler__now-action for the React lane only; this lane
    // rendered no Now button at all. scheduler-now.js reports the now indicator leaving the scroll
    // viewport and activating the action asks it to scroll the indicator back into view (its
    // scrollTo is never animated, so reduced motion is honoured in every case).
    private void AssertNowAction(System.Text.Json.JsonElement expected)
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/scheduler-now.js");
        var cut = Render<HarborlineScheduler>(p => p.Add(x => x.Data, Events).Add(x => x.DefaultDate, Anchor).Add(x => x.Now, Anchor).Add(x => x.View, SchedulerViewType.Day).Add(x => x.ReadOnly, true));
        var className = expected.GetProperty("nowActionClass").GetString()!;
        Assert.NotEmpty(cut.FindAll("[data-now-indicator]"));
        Assert.Empty(cut.FindAll($".{className}"));
        Assert.Single(module.Invocations["connect"]);

        // 299 review 3: React's anchor is a layout effect, so it runs on mount as well; the lane
        // must open anchored on the now line with no Now action, not at the top of the grid.
        Assert.True(expected.GetProperty("anchoredAtLoad").GetBoolean());
        Assert.Single(module.Invocations["anchor"]);

        cut.InvokeAsync(() => cut.Instance.OnNowVisibilityAsync(true)).GetAwaiter().GetResult();
        var action = cut.Find($".{className}");
        Assert.Equal(expected.GetProperty("nowActionName").GetString(), action.TextContent.Trim());

        action.Click();
        Assert.Single(module.Invocations["scrollToNow"]);
        Assert.Empty(cut.FindAll($".{className}"));

        // 299 review 1: the condition itself. Next and Today change the rendered grid without
        // scrolling or resizing the host, so a lane that measures on scroll and resize alone keeps
        // a dead action; the React case walks the same two actions in the other lane.
        Assert.True(expected.GetProperty("remeasureOnViewOrDateChange").GetBoolean());
        cut.InvokeAsync(() => cut.Instance.OnNowVisibilityAsync(true)).GetAwaiter().GetResult();
        Assert.NotEmpty(cut.FindAll($".{className}"));

        // 299 review 2: the re-anchor, not only the re-measure. The React lane's date/view layout
        // effect scrolls the host back onto the now line and clears the flag, so Previous back onto
        // today and Today from elsewhere hide the action there whatever the old scroll position was.
        // Every step below observes the rendered DOM; nothing here clears the flag by hand.
        Assert.True(expected.GetProperty("reanchorOnDateChange").GetBoolean());
        cut.Find("[aria-label=Next]").Click();
        Assert.Equal(2, module.Invocations["anchor"].Count);
        Assert.Empty(cut.FindAll("[data-now-indicator]"));
        Assert.Empty(cut.FindAll($".{className}"));

        // scrolled away again while off today, then back onto today
        cut.InvokeAsync(() => cut.Instance.OnNowVisibilityAsync(true)).GetAwaiter().GetResult();
        Assert.NotEmpty(cut.FindAll($".{className}"));
        cut.Find("[aria-label=Previous]").Click();
        Assert.Equal(3, module.Invocations["anchor"].Count);
        Assert.NotEmpty(cut.FindAll("[data-now-indicator]"));
        Assert.Empty(cut.FindAll($".{className}"));

        cut.Find("[aria-label=Next]").Click();
        cut.InvokeAsync(() => cut.Instance.OnNowVisibilityAsync(true)).GetAwaiter().GetResult();
        Assert.NotEmpty(cut.FindAll($".{className}"));
        cut.Find("[aria-label=Today]").Click();
        Assert.Equal(5, module.Invocations["anchor"].Count);
        Assert.NotEmpty(cut.FindAll("[data-now-indicator]"));
        Assert.Empty(cut.FindAll($".{className}"));

        // 299 review 3: React's anchor effect excludes the rendered event count, so a data update
        // re-measures without moving the scroll host and the action a scrolled-away user is looking
        // at stays. A lane that anchors on the event count scroll-jumps and hides it instead.
        Assert.True(expected.GetProperty("dataUpdateKeepsScrollAndAction").GetBoolean());
        cut.InvokeAsync(() => cut.Instance.OnNowVisibilityAsync(true)).GetAwaiter().GetResult();
        Assert.NotEmpty(cut.FindAll($".{className}"));
        var today = DateTimeOffset.Now;
        var measuresBeforeUpdate = module.Invocations["measure"].Count;
        cut.Render(p => p.Add(x => x.Data, new[] { new SchedulerEvent("added", "Added", today, today.AddHours(1)) }));
        Assert.NotEmpty(cut.FindAll($".{className}"));
        Assert.Equal(measuresBeforeUpdate + 1, module.Invocations["measure"].Count);
        Assert.Equal(5, module.Invocations["anchor"].Count);

        // A view change is the other half of React's measure deps: it re-measures and, being an
        // anchor dependency too, re-anchors.
        cut.Render(p => p.Add(x => x.View, SchedulerViewType.Week));
        Assert.Equal(measuresBeforeUpdate + 2, module.Invocations["measure"].Count);
        Assert.Equal(6, module.Invocations["anchor"].Count);
    }

    private static void AssertRetired(System.Text.Json.JsonElement expected, string markup) { foreach (var name in Classes(expected, "retiredClasses")) Assert.DoesNotContain(name, markup, StringComparison.Ordinal); }
    private static string[] Classes(System.Text.Json.JsonElement expected, string property) => expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
    private sealed class FakeMedia : IMediaQueryObserver { public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> callback, CancellationToken cancellationToken = default) => new(new Subscription(query)); private sealed class Subscription(string query) : IMediaQuerySubscription { public string Query { get; } = query; public bool Matches => false; public ValueTask DisposeAsync() => ValueTask.CompletedTask; } }
}
