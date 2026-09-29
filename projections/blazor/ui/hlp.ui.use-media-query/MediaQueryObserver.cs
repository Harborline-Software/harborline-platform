using Microsoft.JSInterop;

namespace Harborline.UIAdapters.Blazor.Browser;

/// <summary>A change to a media query: the query and whether it now matches.</summary>
public sealed record MediaQueryChange(string Query, bool Matches);

/// <summary>A live subscription to one media query.</summary>
public interface IMediaQuerySubscription : IAsyncDisposable
{
/// <summary>The media query being watched.</summary>
    string Query { get; }
/// <summary>Whether the query currently matches.</summary>
    bool Matches { get; }
}

/// <summary>Watches media queries in the browser and reports changes.</summary>
public interface IMediaQueryObserver
{
/// <summary>Starts watching a media query and calls back whenever whether it matches changes.</summary>
    ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default);
}

/// <summary>Circuit-scoped browser media-query adapter. Register as scoped.</summary>
public sealed class MediaQueryObserver(IJSRuntime javascript) : IMediaQueryObserver, IAsyncDisposable
{
    private IJSObjectReference? module;

/// <summary>Registers a browser listener for the query and returns the subscription.</summary>
    public async ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(onChanged);
        module ??= await javascript.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./_content/Harborline.UIAdapters.Blazor/media-query.js");
        var subscription = new Subscription(module, query, onChanged);
        await subscription.InitializeAsync(cancellationToken);
        return subscription;
    }

/// <summary>Removes the browser listener and releases its resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try { await module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        module = null;
    }

    private sealed class Subscription(IJSObjectReference module, string query, Func<MediaQueryChange, ValueTask> callback) : IMediaQuerySubscription
    {
        private DotNetObjectReference<Subscription>? reference;
        private long id;
/// <summary>The media query being watched.</summary>
        public string Query { get; } = query;
/// <summary>Whether the query currently matches.</summary>
        public bool Matches { get; private set; }

/// <summary>Registers the query with the browser and reads its first match state.</summary>
        public async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            reference = DotNetObjectReference.Create(this);
            var result = await module.InvokeAsync<MediaQueryRegistration>("observe", cancellationToken, Query, reference);
            id = result.Id;
            Matches = result.Matches;
        }

        /// <summary>Receives a change from the browser, stores the new match state and reports it.</summary>
        [JSInvokable]
        public async Task OnChanged(bool matches)
        {
            Matches = matches;
            await callback(new(Query, matches));
        }

/// <summary>Removes the browser listener and releases its interop reference.</summary>
        public async ValueTask DisposeAsync()
        {
            if (id != 0)
            {
                try { await module.InvokeVoidAsync("dispose", id); }
                catch (JSDisconnectedException) { }
                id = 0;
            }
            reference?.Dispose();
            reference = null;
        }
    }

    private sealed record MediaQueryRegistration(long Id, bool Matches);
}
