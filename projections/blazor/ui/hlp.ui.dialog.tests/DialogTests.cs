using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DialogTests : BunitContext
{
    public DialogTests() => JSInterop.Mode = JSRuntimeMode.Loose;
    [Fact] public void ClosedUnmountsAndOpenRelatesTitleDescription() { var closed = Render<HarborlineDialog>(p => p.Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body"))); Assert.Empty(closed.FindAll("[role=dialog]")); var open = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.Description, "Details").Add(x => x.ChildContent, Text("Body"))); var dialog = open.Find("[role=dialog]"); Assert.Equal("true", dialog.GetAttribute("aria-modal")); Assert.Equal("-1", dialog.GetAttribute("tabindex")); Assert.NotNull(dialog.GetAttribute("aria-labelledby")); Assert.NotNull(dialog.GetAttribute("aria-describedby")); }
    [Fact] public void CloseButtonRequestsFalseExactlyOnce() { var requests = new List<bool>(); var cut = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body")).Add(x => x.OpenChanged, value => requests.Add(value))); cut.Find(".hl-dialog__close").Click(); Assert.Equal([false], requests); }
    [Theory, Trait("ModuleConformance", "hlp.ui.dialog")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.dialog", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixture("dialog.");
        });
    }
    // T-1129: a render that lands while the dialog.js import or connect is awaited must not connect a second time,
    // because the first connection (inert siblings, focus trap) would never be disposed and the page would stay inert.
    [Theory, Trait("Holds", "dialog.quality.focus")]
    [InlineData("import")]
    [InlineData("connect")]
    public async Task RenderWhileConnectingConnectsOnceAndCloseDisposesThatConnection(string boundary)
    {
        var (module, connection, import, connect) = PlanConnection(boundary);
        var cut = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body")));
        await cut.InvokeAsync(() => cut.Render());
        if (boundary == "import") await cut.InvokeAsync(() => import.SetResult(module));
        await cut.InvokeAsync(() => connect.SetResult(connection));
        Assert.Single(JSInterop.Invocations["import"]);
        Assert.Single(module.Interop.Invocations["connect"]);
        cut.Render(p => p.Add(x => x.Open, false));
        cut.WaitForAssertion(() => Assert.Equal(1, connection.Disposals));
        Assert.Single(connection.Interop.Invocations["dispose"]);
    }
    // T-1129: a dialog disposed while connecting must dispose the connection it receives afterwards.
    [Theory, Trait("Holds", "dialog.quality.focus")]
    [InlineData("import")]
    [InlineData("connect")]
    public async Task DisposedWhileConnectingDisposesTheConnectionItThenReceives(string boundary)
    {
        var (module, connection, import, connect) = PlanConnection(boundary);
        var cut = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body")));
        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());
        if (boundary == "import") await cut.InvokeAsync(() => import.SetResult(module));
        await cut.InvokeAsync(() => connect.SetResult(connection));
        cut.WaitForAssertion(() => Assert.Equal(1, connection.Disposals));
        Assert.Single(connection.Interop.Invocations["dispose"]);
    }
    // T-1129: a dialog closed while connecting must dispose the connection it receives afterwards, so the page is
    // not left inert behind a dialog that is no longer open.
    [Theory, Trait("Holds", "dialog.quality.focus")]
    [InlineData("import")]
    [InlineData("connect")]
    public async Task ClosedWhileConnectingDisposesTheConnectionItThenReceives(string boundary)
    {
        var (module, connection, import, connect) = PlanConnection(boundary);
        var cut = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body")));
        cut.Render(p => p.Add(x => x.Open, false));
        if (boundary == "import") await cut.InvokeAsync(() => import.SetResult(module));
        await cut.InvokeAsync(() => connect.SetResult(connection));
        cut.WaitForAssertion(() => Assert.Equal(1, connection.Disposals));
        Assert.Single(connection.Interop.Invocations["dispose"]);
    }
    // SetupModule completes imports immediately; bUnit's handler defers the returned reference until the test resolves it.
    private (Reference Module, Reference Connection, JSRuntimeInvocationHandler<IJSObjectReference> Import, JSRuntimeInvocationHandler<IJSObjectReference> Connect) PlanConnection(string boundary)
    {
        var module = new Reference();
        var import = new ReferenceHandler(invocation => invocation.Identifier == "import" && Equals(invocation.Arguments[0], "./_content/Harborline.UIAdapters.Blazor/dialog.js"));
        JSInterop.AddInvocationHandler(import);
        var connect = new ReferenceHandler(invocation => invocation.Identifier == "connect");
        module.Interop.AddInvocationHandler(connect);
        if (boundary != "import") import.SetResult(module);
        return (module, new Reference(), import, connect);
    }
    private sealed class ReferenceHandler(InvocationMatcher matcher) : JSRuntimeInvocationHandler<IJSObjectReference>(matcher, false);
    // Keep bUnit's invocation recording and count the handle disposal it does not record.
    private sealed class Reference : IJSObjectReference
    {
        public BunitJSInterop Interop { get; } = new() { Mode = JSRuntimeMode.Loose };
        public int Disposals { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => Interop.JSRuntime.InvokeAsync<T>(identifier, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => Interop.JSRuntime.InvokeAsync<T>(identifier, cancellationToken, args);
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private static RenderFragment Text(string value) => builder => builder.AddContent(0, value);
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
