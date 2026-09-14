using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppShellLifecycleTests : BunitContext
{
    private const string ModulePath = "./_content/Harborline.UIAdapters.Blazor/dock-divider.js";
    private readonly Media media = new();
    public AppShellLifecycleTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IMediaQueryObserver>(media);
    }

    [Theory]
    [InlineData("import")]
    [InlineData("measureInlineSize")]
    [InlineData("observeInlineSize")]
    [InlineData("observePanelChord")]
    public async Task RemovedShellDisposesResourcesReturnedByPendingInterop(string boundary)
    {
        var module = new Reference();
        var inline = new Reference();
        var chord = new Reference();
        var import = PlanReference(JSInterop, "import", ModulePath);
        var measure = module.Interop.Setup<double>("measureInlineSize", _ => true);
        var observeInline = PlanReference(module.Interop, "observeInlineSize");
        var observeChord = PlanReference(module.Interop, "observePanelChord");
        if (boundary != "import") import.SetResult(module);
        if (boundary != "measureInlineSize") measure.SetResult(1024);
        if (boundary != "observeInlineSize") observeInline.SetResult(inline);
        if (boundary != "observePanelChord") observeChord.SetResult(chord);
        var emitted = new List<DockStateSnapshot>();
        var host = Shell(emitted);
        var shell = host.FindComponent<ObservedShell>().Instance;
        Assert.False(shell.Setup.IsCompleted);
        var before = module.Interop.Invocations.Count;
        await host.InvokeAsync(() => host.Render(p => p.Add(x => x.Show, false)));
        switch (boundary)
        {
            case "import": import.SetResult(module); break;
            case "measureInlineSize": measure.SetResult(1024); break;
            case "observeInlineSize": observeInline.SetResult(inline); break;
            case "observePanelChord": observeChord.SetResult(chord); break;
        }
        await shell.Setup.WaitAsync(TimeSpan.FromSeconds(5));
        await shell.DisposeAsync();
        Assert.Equal(1, module.Disposals);
        Assert.Equal(before, module.Interop.Invocations.Count);
        Assert.Equal(boundary is "observeInlineSize" or "observePanelChord" ? 1 : 0, inline.Disposals);
        Assert.Equal(boundary == "observePanelChord" ? 1 : 0, chord.Disposals);
        Assert.Equal(inline.Disposals, inline.Interop.Invocations["dispose"].Count);
        Assert.Equal(chord.Disposals, chord.Interop.Invocations["dispose"].Count);
        Assert.Empty(emitted);
        await shell.ShellMeasured(480);
        await media.NotifyAsync();
        Assert.Empty(emitted);
        Assert.Contains("Replacement", host.Markup);
        Assert.All(media.Subscriptions, subscription => Assert.True(subscription.Disposed));
    }

    [Theory]
    [InlineData("import")]
    [InlineData("measure")]
    [InlineData("observe")]
    public async Task RemovedDividerDisposesResourcesReturnedByPendingInterop(string boundary)
    {
        var module = new Reference();
        var connection = new Reference();
        var import = PlanReference(JSInterop, "import", ModulePath);
        var measure = module.Interop.Setup<double>("measure", _ => true);
        var observe = PlanReference(module.Interop, "observe");
        if (boundary != "import") import.SetResult(module);
        if (boundary != "measure") measure.SetResult(900);
        if (boundary != "observe") observe.SetResult(connection);
        var changes = new List<double>();
        var host = Render<LifecycleHost>(p => p.AddChildContent<ObservedDivider>(child => child
            .Add(x => x.Minimum, 100).Add(x => x.SecondMinimum, 100).Add(x => x.Fraction, .5)
            .Add(x => x.Changed, changes.Add)));
        var divider = host.FindComponent<ObservedDivider>().Instance;
        Assert.False(divider.Setup.IsCompleted);
        var before = module.Interop.Invocations.Count;
        await host.InvokeAsync(() => host.Render(p => p.Add(x => x.Show, false)));
        switch (boundary)
        {
            case "import": import.SetResult(module); break;
            case "measure": measure.SetResult(900); break;
            case "observe": observe.SetResult(connection); break;
        }
        await divider.Setup.WaitAsync(TimeSpan.FromSeconds(5));
        await divider.DisposeAsync();
        await divider.Measure(480);
        Assert.Equal(1, module.Disposals);
        Assert.Equal(before, module.Interop.Invocations.Count);
        Assert.Equal(boundary == "observe" ? 1 : 0, connection.Disposals);
        Assert.Equal(connection.Disposals, connection.Interop.Invocations["dispose"].Count);
        Assert.Empty(changes);
        Assert.Contains("Replacement", host.Markup);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RemovedShellDisposesALateMediaSubscription(int boundary)
    {
        media.DeferShellIndex = boundary;
        var emitted = new List<DockStateSnapshot>();
        var host = Shell(emitted);
        var shell = host.FindComponent<ObservedShell>().Instance;
        Assert.False(shell.Setup.IsCompleted);
        var count = media.Subscriptions.Count;
        await host.InvokeAsync(() => host.Render(p => p.Add(x => x.Show, false)));
        media.Pending.SetResult(media.PendingSubscription!);
        await shell.Setup.WaitAsync(TimeSpan.FromSeconds(5));
        await media.NotifyAsync();
        Assert.Equal(count, media.Subscriptions.Count);
        Assert.All(media.Subscriptions, subscription => Assert.True(subscription.Disposed));
        Assert.DoesNotContain(JSInterop.Invocations["import"], invocation => Equals(invocation.Arguments[0], ModulePath));
        Assert.Empty(emitted);
    }

    [Theory]
    [InlineData("collapsed", "true")]
    [InlineData("pins", "[\"late-pin\"]")]
    [InlineData("switcher:workspace:active", "\"late-workspace\"")]
    public async Task RemovedShellDoesNotApplyPendingStorageReads(string key, string value)
    {
        var storage = new Storage(ShellPersistence.Key("lifecycle", key));
        var emitted = new List<DockStateSnapshot>();
        var host = Shell(emitted, storage);
        var shell = host.FindComponent<ObservedShell>().Instance;
        Assert.False(shell.Setup.IsCompleted);
        var reads = storage.Reads;
        await host.InvokeAsync(() => host.Render(p => p.Add(x => x.Show, false)));
        storage.Pending.SetResult($"{{\"v\":1,\"value\":{value}}}");
        await shell.Setup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(reads, storage.Reads);
        Assert.DoesNotContain(JSInterop.Invocations["import"], invocation => Equals(invocation.Arguments[0], ModulePath));
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task KeyedShellReplacementCanFinishBeforeTheOldImportReturns()
    {
        var oldModule = new Reference();
        var oldImport = PlanReference(JSInterop, "import", ModulePath);
        var host = Render<KeyedShellHost>();
        var oldShell = host.FindComponent<ObservedShell>().Instance;
        Assert.False(oldShell.Setup.IsCompleted);
        var replacementModule = new Reference();
        var inline = new Reference();
        var chord = new Reference();
        PlanReference(JSInterop, "import", ModulePath).SetResult(replacementModule);
        replacementModule.Interop.Setup<double>("measureInlineSize", _ => true).SetResult(1024);
        PlanReference(replacementModule.Interop, "observeInlineSize").SetResult(inline);
        PlanReference(replacementModule.Interop, "observePanelChord").SetResult(chord);
        await host.InvokeAsync(() => host.Render(p => p.Add(x => x.ShellKey, "replacement")));
        var replacement = host.FindComponent<ObservedShell>().Instance;
        await replacement.Setup;
        oldImport.SetResult(oldModule);
        await oldShell.Setup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, oldModule.Disposals);
        Assert.Empty(oldModule.Interop.Invocations);
        Assert.Equal(0, replacementModule.Disposals);
        Assert.Equal(0, inline.Disposals);
        Assert.Equal(0, chord.Disposals);
        Assert.Equal("replacement", host.Find("[data-shell-id]").GetAttribute("data-shell-id"));
    }

    [Fact]
    public async Task CompletedShellDisposesConnectionsOnceAndIgnoresLaterNotifications()
    {
        var module = new Reference();
        var inline = new Reference();
        var chord = new Reference();
        PlanReference(JSInterop, "import", ModulePath).SetResult(module);
        module.Interop.Setup<double>("measureInlineSize", _ => true).SetResult(1024);
        PlanReference(module.Interop, "observeInlineSize").SetResult(inline);
        PlanReference(module.Interop, "observePanelChord").SetResult(chord);
        var emitted = new List<DockStateSnapshot>();
        var host = Shell(emitted);
        var cut = host.FindComponent<ObservedShell>();
        var shell = cut.Instance;
        await shell.Setup;
        await cut.InvokeAsync(() => shell.ShellMeasured(720));
        Assert.Equal("medium", cut.Find("[data-shell-breakpoint]").GetAttribute("data-shell-breakpoint"));
        var count = emitted.Count;
        var renders = cut.RenderCount;
        await host.InvokeAsync(() => host.Render(p => p.Add(x => x.Show, false)));
        await shell.DisposeAsync();
        await shell.ShellMeasured(480);
        await media.NotifyAsync();
        Assert.Equal(count, emitted.Count);
        Assert.Equal(renders, cut.RenderCount);
        Assert.Equal(1, module.Disposals);
        Assert.Equal(1, inline.Disposals);
        Assert.Equal(1, chord.Disposals);
        inline.Interop.VerifyInvoke("dispose");
        chord.Interop.VerifyInvoke("dispose");
        module.Interop.VerifyNotInvoke("unobserveInlineSize");
        module.Interop.VerifyNotInvoke("unobservePanelChord");
    }

    private IRenderedComponent<LifecycleHost> Shell(List<DockStateSnapshot> emitted, IShellStorage? storage = null) =>
        Render<LifecycleHost>(p => p.AddChildContent<ObservedShell>(child => child
            .Add(x => x.ShellId, "lifecycle").Add(x => x.Navigation, new PackNavigationDeclaration([new("ops", "Ops")]))
            .Add(x => x.ChildContent, builder => builder.AddContent(0, "Body"))
            .Add(x => x.Storage, storage).Add(x => x.DockStateChanged, emitted.Add)));

    public sealed class LifecycleHost : ComponentBase
    {
        [Parameter] public bool Show { get; set; } = true;
        [Parameter] public RenderFragment? ChildContent { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Show) builder.AddContent(0, ChildContent);
            else builder.AddContent(1, "Replacement");
        }
    }

    public sealed class KeyedShellHost : ComponentBase
    {
        [Parameter] public string ShellKey { get; set; } = "original";
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<ObservedShell>(0);
            builder.SetKey(ShellKey);
            builder.AddAttribute(1, nameof(HarborlineAppShell.ShellId), ShellKey);
            builder.AddAttribute(2, nameof(HarborlineAppShell.Navigation), new PackNavigationDeclaration([new("ops", "Ops")]));
            builder.AddAttribute(3, nameof(HarborlineAppShell.ChildContent), (RenderFragment)(content => content.AddContent(0, "Body")));
            builder.CloseComponent();
        }
    }

    public sealed class ObservedShell : HarborlineAppShell
    {
        public Task Setup { get; private set; } = Task.CompletedTask;
        protected override Task OnAfterRenderAsync(bool first)
        {
            var task = base.OnAfterRenderAsync(first);
            if (first) Setup = task;
            return task;
        }
    }

    public sealed class ObservedDivider : DockDivider
    {
        public Task Setup { get; private set; } = Task.CompletedTask;
        protected override Task OnAfterRenderAsync(bool first)
        {
            var task = base.OnAfterRenderAsync(first);
            if (first) Setup = task;
            return task;
        }
    }

    // SetupModule completes imports immediately; use bUnit's handler to defer the returned reference.
    private static JSRuntimeInvocationHandler<IJSObjectReference> PlanReference(BunitJSInterop interop, string identifier, string? path = null)
    {
        var plan = new ReferenceHandler(invocation => invocation.Identifier == identifier && (path is null || Equals(invocation.Arguments[0], path)));
        interop.AddInvocationHandler(plan);
        return plan;
    }

    private sealed class ReferenceHandler(InvocationMatcher matcher) : JSRuntimeInvocationHandler<IJSObjectReference>(matcher, false);

    // Keep bUnit's invocation matching and deferred results; count the handle disposal it does not record.
    private sealed class Reference : IJSObjectReference
    {
        public BunitJSInterop Interop { get; } = new() { Mode = JSRuntimeMode.Loose };
        public int Disposals { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => Interop.JSRuntime.InvokeAsync<T>(identifier, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => Interop.JSRuntime.InvokeAsync<T>(identifier, cancellationToken, args);
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Media : IMediaQueryObserver
    {
        public int? DeferShellIndex { get; set; }
        private int shellIndex;
        public TaskCompletionSource<IMediaQuerySubscription> Pending { get; } = new();
        public Subscription? PendingSubscription { get; private set; }
        public List<Subscription> Subscriptions { get; } = [];
        public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> callback, CancellationToken cancellationToken = default)
        {
            var subscription = new Subscription(query, callback);
            Subscriptions.Add(subscription);
            var shell = callback.Target is HarborlineAppShell || callback.Target?.GetType().DeclaringType == typeof(HarborlineAppShell);
            if (shell && shellIndex++ == DeferShellIndex) { PendingSubscription = subscription; return new(Pending.Task); }
            return ValueTask.FromResult<IMediaQuerySubscription>(subscription);
        }
        public async Task NotifyAsync() { foreach (var subscription in Subscriptions) await subscription.Callback(new(subscription.Query, false)); }
    }

    private sealed class Subscription(string query, Func<MediaQueryChange, ValueTask> callback) : IMediaQuerySubscription
    {
        public string Query => query;
        public bool Matches => query == "(min-width: 600px)";
        public Func<MediaQueryChange, ValueTask> Callback => callback;
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class Storage(string key) : IShellStorage
    {
        public TaskCompletionSource<string?> Pending { get; } = new();
        public int Reads { get; private set; }
        public Task<string?> GetAsync(string candidate) { Reads++; return candidate == key ? Pending.Task : Task.FromResult<string?>(null); }
        public Task SetAsync(string candidate, string value) => Task.CompletedTask;
    }
}
