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
        if (boundary == "import")
        {
            cut.WaitForAssertion(() => Assert.Equal(1, module.Disposals));
            Assert.Empty(module.Interop.Invocations["connect"]);
            Assert.Equal(0, connection.Disposals);
            return;
        }
        cut.WaitForAssertion(() => Assert.Equal(1, connection.Disposals));
        Assert.Single(connection.Interop.Invocations["dispose"]);
        Assert.Equal(1, module.Disposals);
        var callback = Assert.IsType<DotNetObjectReference<HarborlineDialog>>(module.Interop.Invocations["connect"][0].Arguments[3]);
        Assert.Throws<ObjectDisposedException>(() => callback.Value);
    }
    [Theory, Trait("Holds", "dialog.quality.focus")]
    [InlineData("import")]
    [InlineData("connect")]
    public async Task ReopenedWhileConnectingReleasesOldElementsAndConnectsNewOnes(string boundary)
    {
        var (module, connection, import, connect) = PlanConnection(boundary);
        var cut = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body")));
        cut.Render(p => p.Add(x => x.Open, false));
        cut.Render(p => p.Add(x => x.Open, true));
        if (boundary == "import") await cut.InvokeAsync(() => import.SetResult(module));
        await cut.InvokeAsync(() => connect.SetResult(connection));
        if (boundary == "import")
        {
            cut.WaitForAssertion(() => Assert.Single(module.Interop.Invocations["connect"]));
            Assert.Equal(0, connection.Disposals);
        }
        else
        {
            cut.WaitForAssertion(() => Assert.Equal(2, module.Interop.Invocations["connect"].Count));
            Assert.Equal(1, connection.Disposals);
            Assert.NotEqual(module.Interop.Invocations["connect"][0].Arguments[0], module.Interop.Invocations["connect"][1].Arguments[0]);
        }
        Assert.Single(JSInterop.Invocations["import"]);
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
        if (boundary == "import")
        {
            Assert.Empty(module.Interop.Invocations["connect"]);
            Assert.Equal(0, connection.Disposals);
            return;
        }
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
    // T-1122: the translate is physical, so the start edge must be too, or right-to-left content
    // moves the surface off the left edge. This deliberately pins the chosen implementation and the
    // byte identity of the copies; the behaviour itself is proved by gallery/tests/tests/dialog-rtl.spec.ts.
    [Fact]
    public void LaneStylesheetCentresTheSurfaceWithAPhysicalLeftEdge()
    {
        var lane = File.ReadAllText(Repo("projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.css"));
        Assert.Equal(File.ReadAllText(Repo("specs/modules/ui/hlp.ui.dialog/style.css")), lane);
        var surface = System.Text.RegularExpressions.Regex.Match(lane, @"\n\.hl-dialog__surface \{([^}]*)\}").Groups[1].Value;
        Assert.Contains("\n  left: 50%;", surface, StringComparison.Ordinal);
        Assert.Contains("transform: translate(-50%, -50%)", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  inset-inline-start:", surface, StringComparison.Ordinal);
    }
    private static string Repo(string relative) => Path.Combine(
        Environment.GetEnvironmentVariable("HARBORLINE_REPO_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../")),
        relative);
    private static RenderFragment Text(string value) => builder => builder.AddContent(0, value);
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
