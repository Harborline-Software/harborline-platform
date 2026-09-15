using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DataGridLifecycleTests : BunitContext
{
    [Fact]
    public async Task RemovalDuringImportNeverConnectsAndDisposesTheLateModule()
    {
        var runtime = new DelayedRuntime();
        runtime.Module.Connect.SetResult(runtime.Connection);
        Services.AddSingleton<IJSRuntime>(runtime);
        var host = Render<GridHost>();
        var grid = host.FindComponent<LifecycleGrid>().Instance;
        Assert.False(grid.RenderCompletion.IsCompleted);

        host.Render(parameters => parameters.Add(component => component.Show, false));
        Assert.Empty(host.FindAll(".hl-data-grid__viewport"));
        runtime.Import.SetResult(runtime.Module);
        await grid.RenderCompletion;

        Assert.Equal(0, runtime.Module.ConnectCount);
        Assert.Equal(1, runtime.Module.DisposeCount);
    }

    [Fact]
    public async Task RemovalDuringConnectDisposesTheLateConnectionAndCallback()
    {
        var runtime = new DelayedRuntime();
        runtime.Import.SetResult(runtime.Module);
        Services.AddSingleton<IJSRuntime>(runtime);
        var host = Render<GridHost>();
        var grid = host.FindComponent<LifecycleGrid>().Instance;
        Assert.Equal(1, runtime.Module.ConnectCount);

        host.Render(parameters => parameters.Add(component => component.Show, false));
        runtime.Module.Connect.SetResult(runtime.Connection);
        await grid.RenderCompletion;

        Assert.Equal(1, runtime.Connection.DisconnectCount);
        Assert.Equal(1, runtime.Connection.DisposeCount);
        Assert.Equal(1, runtime.Module.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.Callback!.Value);
    }

    [Fact]
    public async Task MissingBrowserElementsDisposeTheUnusedModuleAndCallback()
    {
        var runtime = new DelayedRuntime();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.Connect.SetResult(null);
        Services.AddSingleton<IJSRuntime>(runtime);
        var host = Render<GridHost>();
        await host.FindComponent<LifecycleGrid>().Instance.RenderCompletion;

        Assert.Equal(1, runtime.Module.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.Callback!.Value);
        host.Render(parameters => parameters.Add(component => component.Show, false));
        Assert.Equal(1, runtime.Module.DisposeCount);
    }

    [Fact]
    public void UnrelatedConnectErrorsStillPropagateAndReleaseResources()
    {
        var runtime = new DelayedRuntime();
        var failure = new JSException("Unexpected grid initialization failure");
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.Connect.SetException(failure);
        Services.AddSingleton<IJSRuntime>(runtime);

        Assert.Same(failure, Assert.Throws<JSException>(() => Render<GridHost>()));
        Assert.Equal(1, runtime.Module.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.Callback!.Value);
    }

    public sealed class LifecycleGrid : HarborlineDataGrid<string>
    {
        public Task RenderCompletion { get; private set; } = Task.CompletedTask;
        protected override Task OnAfterRenderAsync(bool firstRender) =>
            RenderCompletion = base.OnAfterRenderAsync(firstRender);
    }

    public sealed class GridHost : ComponentBase
    {
        [Parameter] public bool Show { get; set; } = true;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (!Show) return;
            builder.OpenComponent<LifecycleGrid>(0);
            builder.CloseComponent();
        }
    }

    private sealed class DelayedRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DelayedModule Module { get; } = new();
        public GridConnection Connection { get; } = new();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("import", identifier);
            return (TValue)(object)await Import.Task;
        }
    }

    private sealed class DelayedModule : IJSObjectReference
    {
        public TaskCompletionSource<IJSObjectReference?> Connect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ConnectCount { get; private set; }
        public int DisposeCount { get; private set; }
        public DotNetObjectReference<HarborlineDataGrid<string>>? Callback { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("connect", identifier);
            ConnectCount++;
            Callback = Assert.IsType<DotNetObjectReference<HarborlineDataGrid<string>>>(args![2]);
            return (TValue)(object)(await Connect.Task)!;
        }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    private sealed class GridConnection : IJSObjectReference
    {
        public int DisconnectCount { get; private set; }
        public int DisposeCount { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("dispose", identifier);
            DisconnectCount++;
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
