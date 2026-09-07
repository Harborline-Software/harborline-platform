using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Harborline.UIAdapters.Blazor.Browser;

public enum OutsidePointerEventType { MouseDown, PointerDown }

public sealed record OutsidePointerOptions(bool Enabled = true, OutsidePointerEventType EventType = OutsidePointerEventType.MouseDown);

public sealed record OutsidePointerEvent(string EventType, int Button, string? PointerType, bool AltKey, bool ControlKey, bool MetaKey, bool ShiftKey);

public interface IOutsidePointerRegistration : IAsyncDisposable
{
    ValueTask SetEnabledAsync(bool enabled);
    void SetCallback(Func<OutsidePointerEvent, ValueTask> onOutside);
}

public interface IOutsidePointerObserver
{
    ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<ElementReference> insideElements, Func<OutsidePointerEvent, ValueTask> onOutside, OutsidePointerOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Circuit-scoped document pointer adapter. DOM classification remains in JavaScript.</summary>
public sealed class OutsidePointerObserver(IJSRuntime javascript) : IOutsidePointerObserver, IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<ElementReference> insideElements, Func<OutsidePointerEvent, ValueTask> onOutside, OutsidePointerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(insideElements);
        ArgumentNullException.ThrowIfNull(onOutside);
        options ??= new();
        module ??= await javascript.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./_content/Harborline.UIAdapters.Blazor/outside-pointer.js");
        var registration = new Registration(module, insideElements, onOutside, options);
        await registration.InitializeAsync(cancellationToken);
        return registration;
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try { await module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        module = null;
    }

    private sealed class Registration(IJSObjectReference module, IReadOnlyList<ElementReference> elements, Func<OutsidePointerEvent, ValueTask> callback, OutsidePointerOptions options) : IOutsidePointerRegistration
    {
        private DotNetObjectReference<Registration>? reference;
        private Func<OutsidePointerEvent, ValueTask> currentCallback = callback;
        private long id;

        public async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            reference = DotNetObjectReference.Create(this);
            var eventType = options.EventType == OutsidePointerEventType.PointerDown ? "pointerdown" : "mousedown";
            id = await module.InvokeAsync<long>("observe", cancellationToken, elements, reference, options.Enabled, eventType);
        }

        [JSInvokable]
        public Task OnOutside(OutsidePointerEvent pointerEvent) => currentCallback(pointerEvent).AsTask();

        public void SetCallback(Func<OutsidePointerEvent, ValueTask> onOutside)
        {
            ArgumentNullException.ThrowIfNull(onOutside);
            Interlocked.Exchange(ref currentCallback, onOutside);
        }

        public async ValueTask SetEnabledAsync(bool enabled)
        {
            if (id != 0) await module.InvokeVoidAsync("setEnabled", id, enabled);
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
}
