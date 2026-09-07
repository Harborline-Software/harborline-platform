using Microsoft.JSInterop;

namespace Harborline.UIAdapters.Blazor.Browser;

public sealed record MediaQueryChange(string Query, bool Matches);

public interface IMediaQuerySubscription : IAsyncDisposable
{
    string Query { get; }
    bool Matches { get; }
}

public interface IMediaQueryObserver
{
    ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default);
}

/// <summary>Circuit-scoped browser media-query adapter. Register as scoped.</summary>
public sealed class MediaQueryObserver(IJSRuntime javascript) : IMediaQueryObserver, IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(onChanged);
        module ??= await javascript.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./_content/Harborline.UIAdapters.Blazor/media-query.js");
        var subscription = new Subscription(module, query, onChanged);
        await subscription.InitializeAsync(cancellationToken);
        return subscription;
    }

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
        public string Query { get; } = query;
        public bool Matches { get; private set; }

        public async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            reference = DotNetObjectReference.Create(this);
            var result = await module.InvokeAsync<MediaQueryRegistration>("observe", cancellationToken, Query, reference);
            id = result.Id;
            Matches = result.Matches;
        }

        [JSInvokable]
        public async Task OnChanged(bool matches)
        {
            Matches = matches;
            await callback(new(Query, matches));
        }

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
