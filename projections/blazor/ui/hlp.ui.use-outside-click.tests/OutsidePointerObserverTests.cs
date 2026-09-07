using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Harborline.UIAdapters.Blazor.Browser;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class OutsidePointerObserverNativeTests
{
    [Fact]
    public async Task RegistrationPreservesClosedEventVocabularyAndDisposes()
    {
        var module = new FakeModule();
        await using var observer = new OutsidePointerObserver(new FakeRuntime(module));
        await using var registration = await observer.ObserveAsync([default(ElementReference)], _ => ValueTask.CompletedTask, new(EventType: OutsidePointerEventType.PointerDown));
        registration.SetCallback(_ => ValueTask.CompletedTask);
        await registration.SetEnabledAsync(false);
        await registration.DisposeAsync();
        Assert.Equal(["observe", "setEnabled", "dispose"], module.Calls.Take(3));
    }

    [Fact]
    [Trait("ModuleConformance", "hlp.ui.use-outside-click")]
    public async Task SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("outside-click.", fixture.RootElement.GetProperty("id").GetString());
        await using var observer = new OutsidePointerObserver(new FakeRuntime(new FakeModule()));
        await using var registration = await observer.ObserveAsync([], _ => ValueTask.CompletedTask);
        Assert.NotNull(registration);
    }

    private sealed class FakeRuntime(IJSObjectReference module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => new((TValue)module);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => new((TValue)module);
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public ValueTask DisposeAsync() { Calls.Add("module-dispose"); return ValueTask.CompletedTask; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (typeof(TValue).Name == "IJSVoidResult") return new(default(TValue)!);
            object value = identifier == "observe" ? 9L : 0L;
            return new((TValue)value);
        }
    }
}
