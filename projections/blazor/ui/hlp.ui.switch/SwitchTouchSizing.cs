using System.Runtime.CompilerServices;
using Harborline.Foundation.Responsive;
using Harborline.UIAdapters.Blazor.Browser;

namespace Harborline.UIAdapters.Blazor.Components.Forms.Inputs;

/// <summary>
/// Touch sizing for every switch that shares one media-query observer (one per circuit or WebAssembly app):
/// the four form-factor queries are observed once, not once per switch, and resolved by the shared
/// form-factor policy as React's useTouchSizing does: phone mode or any coarse pointer. The subscriptions
/// live as long as the observer.
/// </summary>
internal sealed class SwitchTouchSizing
{
    private static readonly ConditionalWeakTable<IMediaQueryObserver, SwitchTouchSizing> Shared = new();
    private static readonly string[] Queries = [FormFactorQueries.Phone, FormFactorQueries.Landscape, FormFactorQueries.ShortHeight, FormFactorQueries.AnyCoarsePointer];
    private readonly IMediaQueryObserver observer;
    private readonly Dictionary<string, bool> matches = new(StringComparer.Ordinal);
    private readonly List<IMediaQuerySubscription> subscriptions = [];
    private Task? started;

    private SwitchTouchSizing(IMediaQueryObserver observer) => this.observer = observer;

    /// <summary>The state shared by every switch using this observer.</summary>
    public static SwitchTouchSizing For(IMediaQueryObserver observer) => Shared.GetValue(observer, key => new SwitchTouchSizing(key));

    /// <summary>Whether the 44px touch target applies now.</summary>
    public bool TouchSized { get; private set; }

    /// <summary>Raised after <see cref="TouchSized"/> changes.</summary>
    public event Func<Task>? Changed;

    /// <summary>Starts observing once; later callers await the same start.</summary>
    public Task StartAsync() => started ??= StartCoreAsync();

    private async Task StartCoreAsync()
    {
        foreach (var query in Queries)
        {
            var subscription = await observer.ObserveAsync(query, change => { matches[change.Query] = change.Matches; return new ValueTask(UpdateAsync()); });
            subscriptions.Add(subscription);
            matches[query] = subscription.Matches;
        }
        await UpdateAsync();
    }

    private async Task UpdateAsync()
    {
        bool Match(string query) => matches.GetValueOrDefault(query);
        var next = FormFactorPolicy.Resolve(new FormFactorSignals(PhoneWidth: Match(FormFactorQueries.Phone), Landscape: Match(FormFactorQueries.Landscape), ShortHeight: Match(FormFactorQueries.ShortHeight), AnyCoarsePointer: Match(FormFactorQueries.AnyCoarsePointer))).TouchSizing;
        if (next == TouchSized) return;
        TouchSized = next;
        if (Changed is { } handlers) foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>()) await handler();
    }
}
