using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Harborline.UIAdapters.Blazor.Browser;

/// <summary>Which pointer event the outside-click observer listens for.</summary>
public enum OutsidePointerEventType
{
    /// <summary>Reports an outside click on mouse button down.</summary>
    MouseDown,
    /// <summary>Reports an outside click on pointer down, which also covers touch and pen.</summary>
    PointerDown
}

/// <summary>Settings for outside-pointer detection: whether it is on and which pointer event triggers it.</summary>
public sealed record OutsidePointerOptions(bool Enabled = true, OutsidePointerEventType EventType = OutsidePointerEventType.MouseDown);

/// <summary>A pointer event that happened outside the watched elements: type, button, pointer type and modifier keys.</summary>
public sealed record OutsidePointerEvent(string EventType, int Button, string? PointerType, bool AltKey, bool ControlKey, bool MetaKey, bool ShiftKey);

/// <summary>A live outside-pointer registration that can be turned on or off and given a new callback.</summary>
public interface IOutsidePointerRegistration : IAsyncDisposable
{
/// <summary>Turns outside-pointer detection on or off.</summary>
    ValueTask SetEnabledAsync(bool enabled);
/// <summary>Sets the callback run when a pointer event lands outside the elements.</summary>
    void SetCallback(Func<OutsidePointerEvent, ValueTask> onOutside);
}

/// <summary>Detects pointer events outside a set of elements, such as to close a popover.</summary>
public interface IOutsidePointerObserver
{
/// <summary>Starts watching for pointer events outside the given elements and calls back when one happens.</summary>
    ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<ElementReference> insideElements, Func<OutsidePointerEvent, ValueTask> onOutside, OutsidePointerOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Circuit-scoped document pointer adapter. DOM classification remains in JavaScript.</summary>
public sealed class OutsidePointerObserver(IJSRuntime javascript) : IOutsidePointerObserver, IAsyncDisposable
{
    private IJSObjectReference? module;

/// <summary>Registers a browser listener for events outside the elements and returns the registration.</summary>
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

/// <summary>Removes the browser listener and releases its resources.</summary>
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

/// <summary>Registers the elements with the browser and starts listening.</summary>
        public async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            reference = DotNetObjectReference.Create(this);
            var eventType = options.EventType == OutsidePointerEventType.PointerDown ? "pointerdown" : "mousedown";
            id = await module.InvokeAsync<long>("observe", cancellationToken, elements, reference, options.Enabled, eventType);
        }

        /// <summary>Receives an outside pointer event from the browser and passes it to the current callback.</summary>
        [JSInvokable]
        public Task OnOutside(OutsidePointerEvent pointerEvent) => currentCallback(pointerEvent).AsTask();

/// <summary>Replaces the callback run on an outside pointer event.</summary>
        public void SetCallback(Func<OutsidePointerEvent, ValueTask> onOutside)
        {
            ArgumentNullException.ThrowIfNull(onOutside);
            Interlocked.Exchange(ref currentCallback, onOutside);
        }

/// <summary>Turns the browser listener on or off.</summary>
        public async ValueTask SetEnabledAsync(bool enabled)
        {
            if (id != 0) await module.InvokeVoidAsync("setEnabled", id, enabled);
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
}
