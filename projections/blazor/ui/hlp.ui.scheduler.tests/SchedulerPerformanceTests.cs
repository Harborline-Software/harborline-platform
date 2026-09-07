using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Scheduling;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SchedulerPerformanceTests : BunitContext
{
    [Fact, Trait("ModulePerformance", "hlp.ui.scheduler")]
    public void TwoHundredFiftySixEventsAndRepeatedReplacementRemainBounded()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; Services.AddSingleton<IMediaQueryObserver>(new NoMedia());
        var day = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 256).Select(index => new SchedulerEvent(index, $"Event {index}", day.AddMinutes(index * 5), day.AddMinutes(index * 5 + 30))).ToArray();
        var cut = Render<HarborlineScheduler>(p => p.Add(x => x.Data, events).Add(x => x.View, SchedulerViewType.Day).Add(x => x.SelectedDate, day).Add(x => x.Now, day));
        Assert.Equal(256, cut.FindAll("[data-event-chip]").Count);
        for (var index = 0; index < 96; index++) cut.Render();
        Assert.True(cut.FindAll("[data-scheduler-toolbar]").Count <= 1); Assert.True(cut.FindAll("[role=dialog]").Count <= 1);
    }
    private sealed class NoMedia : IMediaQueryObserver { public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> callback, CancellationToken cancellationToken = default) => new(new Subscription(query)); private sealed class Subscription(string query) : IMediaQuerySubscription { public string Query { get; } = query; public bool Matches => false; public ValueTask DisposeAsync() => ValueTask.CompletedTask; } }
}
