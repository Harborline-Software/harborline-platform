using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Harborline.UIAdapters.Blazor.Browser;

/// <summary>The scroll axis the scroll-affordance observer watches.</summary>
public enum ScrollAffordanceOrientation
{
    /// <summary>Watches horizontal scrolling to show start and end overflow cues.</summary>
    Horizontal,
    /// <summary>Watches vertical scrolling to show top and bottom overflow cues.</summary>
    Vertical
}

/// <summary>Settings for scroll fades: direction, fade size, item count and the delay before announcing.</summary>
public sealed record ScrollAffordanceOptions(
    ScrollAffordanceOrientation Orientation = ScrollAffordanceOrientation.Horizontal,
    double FadeSize = 24,
    int? ItemCount = null,
    int AnnounceDebounceMilliseconds = 300);

/// <summary>A scroll measurement: raw position, scroll size, client size and whether the text runs right to left.</summary>
public sealed record ScrollAffordanceMeasurement(double RawPosition, double ScrollSize, double ClientSize, bool RightToLeft = false);

/// <summary>What a scroll area should show: whether it scrolls, its start and end, the fade mask and the announcement.</summary>
public sealed record ScrollAffordanceState(
    bool CanScroll,
    bool AtStart,
    bool AtEnd,
    string? MaskImage,
    string? AnnouncementKey,
    IReadOnlyDictionary<string, int> AnnouncementArguments);

/// <summary>The result of a key press on a scroll area: whether it was handled and the position to scroll to.</summary>
public sealed record ScrollAffordanceKeyResult(bool Handled, double LogicalTarget, double RawTarget);

/// <summary>Rules that turn a scroll measurement into fade and announcement state.</summary>
public static class ScrollAffordancePolicy
{
    /// <summary>Works out the scroll state, fade mask and announcement from a measurement and the options.</summary>
    public static ScrollAffordanceState Resolve(ScrollAffordanceMeasurement measurement, ScrollAffordanceOptions? options = null)
    {
        options ??= new();
        var horizontalRtl = options.Orientation == ScrollAffordanceOrientation.Horizontal && measurement.RightToLeft;
        var position = Math.Max(0, horizontalRtl ? -measurement.RawPosition : measurement.RawPosition);
        var maximum = Math.Max(0, measurement.ScrollSize - measurement.ClientSize);
        var canScroll = measurement.ScrollSize > measurement.ClientSize + 1;
        var atStart = position <= 1;
        var atEnd = position >= maximum - 1;
        var towardEnd = options.Orientation == ScrollAffordanceOrientation.Vertical ? "bottom" : horizontalRtl ? "left" : "right";
        var towardStart = options.Orientation == ScrollAffordanceOrientation.Vertical ? "top" : horizontalRtl ? "right" : "left";
        string? mask = null;
        if (canScroll && !atStart && !atEnd)
            mask = $"linear-gradient(to {towardEnd}, transparent, black {options.FadeSize}px, black calc(100% - {options.FadeSize}px), transparent)";
        else if (canScroll && !atStart)
            mask = $"linear-gradient(to {towardEnd}, transparent, black {options.FadeSize}px)";
        else if (canScroll && !atEnd)
            mask = $"linear-gradient(to {towardStart}, transparent, black {options.FadeSize}px)";

        string? key = null;
        var arguments = new Dictionary<string, int>();
        if (canScroll && options.ItemCount is > 0)
        {
            var unit = measurement.ScrollSize / options.ItemCount.Value;
            var start = Math.Min(options.ItemCount.Value, Math.Max(1, (int)Math.Round(position / unit, MidpointRounding.AwayFromZero) + 1));
            var visible = Math.Max(1, (int)Math.Round(measurement.ClientSize / unit, MidpointRounding.AwayFromZero));
            var end = Math.Min(options.ItemCount.Value, start + visible - 1);
            key = start >= end ? "scrollAffordance.showingOne" : "scrollAffordance.showingRange";
            arguments["index"] = start;
            arguments["start"] = start;
            arguments["end"] = end;
            arguments["total"] = options.ItemCount.Value;
        }
        else if (canScroll)
        {
            key = atEnd ? "scrollAffordance.endReached" : atStart ? "scrollAffordance.moreAvailable" : "scrollAffordance.percentScrolled";
            if (!atStart && !atEnd) arguments["percent"] = maximum <= 0 ? 0 : (int)Math.Round(position / maximum * 100);
        }
        return new(canScroll, atStart, atEnd, mask, key, arguments);
    }

    /// <summary>Returns where a scroll key such as Home, End or an arrow should scroll to, allowing for right-to-left text.</summary>
    public static ScrollAffordanceKeyResult ResolveKey(
        string key,
        ScrollAffordanceMeasurement measurement,
        ScrollAffordanceOrientation orientation = ScrollAffordanceOrientation.Horizontal)
    {
        var rtl = orientation == ScrollAffordanceOrientation.Horizontal && measurement.RightToLeft;
        var logical = Math.Max(0, rtl ? -measurement.RawPosition : measurement.RawPosition);
        var maximum = Math.Max(0, measurement.ScrollSize - measurement.ClientSize);
        var step = Math.Max(40, measurement.ClientSize * .8);
        double? target = key switch
        {
            "Home" => 0,
            "End" => maximum,
            "ArrowDown" when orientation == ScrollAffordanceOrientation.Vertical => logical + step,
            "ArrowUp" when orientation == ScrollAffordanceOrientation.Vertical => logical - step,
            "ArrowRight" when orientation == ScrollAffordanceOrientation.Horizontal => logical + (rtl ? -step : step),
            "ArrowLeft" when orientation == ScrollAffordanceOrientation.Horizontal => logical + (rtl ? step : -step),
            _ => null,
        };
        if (!target.HasValue) return new(false, logical, measurement.RawPosition);
        var clamped = Math.Clamp(target.Value, 0, maximum);
        return new(true, clamped, rtl ? -clamped : clamped);
    }
}

/// <summary>A live scroll-affordance registration, released by disposing it.</summary>
public interface IScrollAffordanceRegistration : IAsyncDisposable { }

/// <summary>Watches a scrollable element and reports its fade and announcement state as it scrolls.</summary>
public interface IScrollAffordanceObserver
{
    /// <summary>Starts watching an element and calls back with its scroll state whenever it changes.</summary>
    ValueTask<IScrollAffordanceRegistration> ObserveAsync(ElementReference element, ScrollAffordanceOptions options, Func<ScrollAffordanceState, ValueTask> onChanged, CancellationToken cancellationToken = default);
}

/// <summary>Browser lifecycle adapter for a single scroll host.</summary>
public sealed class ScrollAffordanceObserver(IJSRuntime javascript) : IScrollAffordanceObserver, IAsyncDisposable
{
    private IJSObjectReference? module;

    /// <summary>Registers browser scroll listeners on the element and returns the registration.</summary>
    public async ValueTask<IScrollAffordanceRegistration> ObserveAsync(ElementReference element, ScrollAffordanceOptions options, Func<ScrollAffordanceState, ValueTask> onChanged, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onChanged);
        module ??= await javascript.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./_content/Harborline.UIAdapters.Blazor/scroll-affordance.js");
        var registration = new Registration(module, element, options, onChanged);
        await registration.InitializeAsync(cancellationToken);
        return registration;
    }

    /// <summary>Removes the browser listeners and releases their resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try { await module.DisposeAsync(); } catch (JSDisconnectedException) { }
        module = null;
    }

    private sealed class Registration(IJSObjectReference module, ElementReference element, ScrollAffordanceOptions options, Func<ScrollAffordanceState, ValueTask> callback) : IScrollAffordanceRegistration
    {
        private DotNetObjectReference<Registration>? reference;
        private long id;
        /// <summary>Registers the element with the browser and reads the first scroll state.</summary>
        public async ValueTask InitializeAsync(CancellationToken token)
        {
            reference = DotNetObjectReference.Create(this);
            id = await module.InvokeAsync<long>("observe", token, element, reference, options);
        }
        /// <summary>Removes the browser listeners and releases their interop reference.</summary>
        [JSInvokable] public Task OnMeasured(ScrollAffordanceMeasurement measurement) => callback(ScrollAffordancePolicy.Resolve(measurement, options)).AsTask();
        /// <summary>Releases the registration when it is disposed.</summary>
        public async ValueTask DisposeAsync()
        {
            if (id != 0) { try { await module.InvokeVoidAsync("dispose", id); } catch (JSDisconnectedException) { } id = 0; }
            reference?.Dispose(); reference = null;
        }
    }
}
