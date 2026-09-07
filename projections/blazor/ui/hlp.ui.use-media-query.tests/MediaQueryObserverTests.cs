using Microsoft.JSInterop;
using Harborline.UIAdapters.Blazor.Browser;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class MediaQueryObserverNativeTests
{
    [Fact]
    public async Task BrowserSnapshotAndDisposalUseTheOwnedModule()
    {
        var module = new FakeModule();
        await using var observer = new MediaQueryObserver(new FakeRuntime(module));
        await using var subscription = await observer.ObserveAsync("(min-width: 768px)", _ => ValueTask.CompletedTask);
        Assert.True(subscription.Matches);
        Assert.Equal("(min-width: 768px)", subscription.Query);
        await subscription.DisposeAsync();
        Assert.Contains("dispose", module.Calls);
    }

    [Fact]
    [Trait("ModuleConformance", "hlp.ui.use-media-query")]
    public async Task SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("media-query.", fixture.RootElement.GetProperty("id").GetString());
        await using var observer = new MediaQueryObserver(new FakeRuntime(new FakeModule()));
        await using var subscription = await observer.ObserveAsync("(prefers-reduced-motion: reduce)", _ => ValueTask.CompletedTask);
        Assert.True(subscription.Matches);
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
            object? value = identifier == "observe" ? new { Id = 7L, Matches = true } : default(TValue);
            if (identifier == "observe")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(value);
                return new(System.Text.Json.JsonSerializer.Deserialize<TValue>(json)!);
            }
            return new((TValue)value!);
        }
    }
}
