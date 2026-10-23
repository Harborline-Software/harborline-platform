using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppShellRev6FrameTests : BunitContext
{
    private static readonly AppShellRev6Owner Alice = new("alice", "tenant-a", "workspace-a");
    private static readonly AppShellRev6Owner Bob = new("bob", "tenant-a", "workspace-a");

    public AppShellRev6FrameTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/app-shell-rev6-frame.mjs");
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/app-shell-rev6-focus.mjs");
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/data-grid-component.js");
    }

    [Fact]
    public void Public_facades_have_exactly_six_and_five_parameters_without_body_slots()
    {
        static string[] Parameters(Type type) => type.GetProperties()
            .Where(property => Attribute.IsDefined(property, typeof(ParameterAttribute)))
            .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Intent", "Owner", "Presentation", "ShellId", "ShellInlineSize", "TabContent" }, Parameters(typeof(HarborlineAppShellRev6Frame)));
        Assert.Equal(new[] { "Intent", "Owner", "Presentation", "ShellId", "TabContent" }, Parameters(typeof(HarborlineAppShellRev6TabHost)));
    }

    // Literal interface80/299 geometry; no expected values come from frame helpers.
    [Theory]
    [InlineData(599, "phone", "0px", "0px", "sheet", "0px")]
    [InlineData(600, "tablet", "56px", "0px", "float", "0px")]
    [InlineData(700, "tablet", "56px", "0px", "float", "0px")]
    [InlineData(1023, "tablet", "56px", "0px", "float", "0px")]
    [InlineData(1024, "laptop", "56px", "240px", "float", "0px")]
    [InlineData(1235, "laptop", "56px", "240px", "float", "0px")]
    [InlineData(1236, "laptop", "56px", "240px", "dock", "380px")]
    [InlineData(1600, "wide", "56px", "240px", "dock", "380px")]
    public void Measured_root_controls_form_and_capacity(double width, string form, string rail, string sidebar, string placement, string panel)
    {
        var cut = Frame(View(), [], width);
        var root = cut.Find("[data-shell-id=frame-test]");
        Assert.Equal(form, root.GetAttribute("data-form"));
        Assert.Equal(placement, root.GetAttribute("data-panel-placement"));
        Assert.Contains("--frame-rail:" + rail + ";", root.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("--frame-sidebar:" + sidebar + ";", root.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.EndsWith("--frame-panel:" + panel, root.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("main"));
        Assert.Empty(cut.FindAll(".hl-rev6-panel [role=dialog]"));
    }

    [Theory]
    [InlineData(1259, "float")]
    [InlineData(1260, "dock")]
    public void Labelled_rail_uses_the_literal_dock_boundary(double width, string placement)
    {
        var cut = Frame(View() with { RailLabels = true }, [], width);
        Assert.Equal(placement, cut.Find("[data-shell-id]").GetAttribute("data-panel-placement"));
        Assert.Contains("--frame-rail:80px;", cut.Find("[data-shell-id]").GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_system_and_focus_conditions_preserve_capacity_and_mounted_panel()
    {
        var view = View();
        var cut = Frame(view with { Sidebar = null }, [], 1024);
        Assert.Equal("dock", cut.Find("[data-shell-id]").GetAttribute("data-panel-placement"));
        Assert.Contains("--frame-sidebar:0px;", cut.Find("[data-shell-id]").GetAttribute("style")!, StringComparison.Ordinal);
        var surface = cut.FindComponent<HarborlineAppShellRev6TabSurface>().Instance;
        cut.Render(p => p.Add(c => c.Presentation, view with { IsSystemPage = true }));
        Assert.Empty(cut.FindAll("aside.hl-rev6-sidebar"));
        cut.Render(p => p.Add(c => c.Presentation, view with { IsSystemPage = true, Mode = AppShellRev6FrameMode.Chat }));
        Assert.Single(cut.FindAll("aside.hl-rev6-sidebar"));
        cut.Render(p => p.Add(c => c.Presentation, view with { FocusMode = true }));
        Assert.Empty(cut.FindAll("nav"));
        Assert.Empty(cut.FindAll("aside.hl-rev6-sidebar"));
        Assert.True(cut.Find("aside.hl-rev6-panel").HasAttribute("hidden"));
        Assert.Same(surface, cut.FindComponent<HarborlineAppShellRev6TabSurface>().Instance);
        Assert.Single(cut.FindAll("main"));
    }

    [Fact]
    public async Task Null_size_waits_for_root_measurement_and_explicit_size_ignores_late_observer()
    {
        var requests = new List<AppShellRev6FrameIntent>();
        var cut = Frame(View(), requests, null);
        Assert.Equal("unmeasured", cut.Find("[data-shell-id]").GetAttribute("data-form"));
        await cut.InvokeAsync(() => cut.Instance.MeasureInlineSize(700));
        Assert.Equal("tablet", cut.Find("[data-shell-id]").GetAttribute("data-form"));
        cut.Render(p => p.Add(c => c.ShellInlineSize, 1236));
        await cut.InvokeAsync(() => cut.Instance.MeasureInlineSize(390));
        Assert.Equal("laptop", cut.Find("[data-shell-id]").GetAttribute("data-form"));
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_explicit_size_is_rejected(double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Frame(View(), [], width));

    [Fact]
    public async Task Supported_table_uses_real_Platform_renderer_and_owner_captured_requests()
    {
        var requests = new List<AppShellRev6FrameIntent>();
        var cut = Frame(View(), requests, 1236);
        Assert.Equal("Record", Assert.Single(cut.FindAll("[role=columnheader]")).TextContent);
        Assert.Contains("Literal sample row", cut.Find("[data-row-id=record-a]").TextContent, StringComparison.Ordinal);
        cut.Find("[data-row-id=record-a]").DoubleClick();
        Assert.Equal(new AppShellRev6FrameRowActivated(Alice, "record-a"), Assert.Single(requests));
        requests.Clear();
        var runtime = cut.FindComponent<HarborlineViewRuntime>();
        var oldAction = runtime.Instance.OnAction;
        cut.Find(".hl-view-runtime__actions button").Click();
        Assert.Equal(new AppShellRev6FrameActionRequested(Alice, "inspect"), Assert.Single(requests));
        requests.Clear();
        var bob = View(Bob);
        cut.Render(p => p.Add(c => c.Owner, Bob).Add(c => c.Presentation, bob));
        await cut.InvokeAsync(() => oldAction.InvokeAsync("inspect"));
        Assert.Empty(requests);
        cut.Render(p => p.Add(c => c.Presentation, bob with { MainFace = bob.MainFace with { ActionsDisabled = true } }));
        Assert.True(cut.Find(".hl-view-runtime__actions button").HasAttribute("disabled"));
    }

    [Fact]
    public void Empty_table_and_unsupported_faces_are_honest_and_owner_mismatch_invokes_no_renderer()
    {
        var view = View();
        var cut = Frame(view with { MainFace = view.MainFace with { Rows = Array.Empty<ViewRuntimeRow>() } }, [], 1236);
        Assert.Contains("No literal rows.", cut.Markup, StringComparison.Ordinal);
        foreach (var face in new[] {
            view.MainFace with { Kind = AppShellRev6FrameFaceKind.Home },
            view.MainFace with { Kind = AppShellRev6FrameFaceKind.Conversation },
            view.MainFace with { Plan = view.MainFace.Plan! with { Bindings = new("layout.board") } },
            view.MainFace with { Plan = null }, view.MainFace with { Rows = null },
            view.MainFace with { Plan = view.MainFace.Plan! with { Bindings = new("layout.table", new(null)) } } })
        {
            cut.Render(p => p.Add(c => c.Presentation, view with { MainFace = face }));
            Assert.Empty(cut.FindComponents<HarborlineViewRuntime>());
            Assert.Equal("No supported view is available.", cut.Find("[data-unsupported-face]").TextContent);
        }
        foreach (var mismatch in new[] { view with { Owner = Bob }, view with { MainFace = view.MainFace with { Owner = Bob } }, view with { Tabs = view.Tabs with { Owner = Bob } } })
        {
            cut.Render(p => p.Add(c => c.Presentation, mismatch));
            Assert.Empty(cut.FindComponents<HarborlineViewRuntime>());
            Assert.Empty(cut.FindAll("[role=tab]"));
            Assert.DoesNotContain("Literal sample row", cut.Markup, StringComparison.Ordinal);
            Assert.Single(cut.FindAll("main"));
        }
    }

    [Fact]
    public void Phone_drawer_has_the_same_mode_rows_counts_home_and_last_account()
    {
        var requests = new List<AppShellRev6FrameIntent>();
        var view = View();
        var cut = Frame(view, requests, 390);
        Assert.Empty(cut.FindAll("nav"));
        cut.Find("[data-navigation-trigger]").Click();
        Assert.Single(cut.FindAll("nav"));
        Assert.Equal(new[] { "home", "my-work", "records", "pages", "conversations", "all", "account" },
            cut.FindAll("nav [data-drawer-row]").Select(row => row.GetAttribute("data-nav-id") ?? "account"));
        Assert.Equal("My work 2", cut.Find("[data-nav-id=my-work]").GetAttribute("aria-label"));
        Assert.Equal("All 3", cut.Find("[data-nav-id=all]").GetAttribute("aria-label"));
        Assert.Equal("true", cut.Find("[data-frame-background]").GetAttribute("aria-hidden"));
        cut.Find("[data-nav-id=records]").Click();
        Assert.Equal(new AppShellRev6FrameNavigate(Alice, "records"), Assert.Single(requests));
        Assert.Empty(cut.FindAll("[data-navigation-dialog]"));
        requests.Clear();
        cut.Find("[data-navigation-trigger]").Click();
        cut.Find("[data-drawer-dismiss]").Click();
        Assert.Empty(cut.FindAll("[data-navigation-dialog]"));
        Assert.Empty(requests);
        cut.Find("[data-navigation-trigger]").Click();
        cut.Render(p => p.Add(c => c.ShellInlineSize, 700));
        Assert.Empty(cut.FindAll("[data-navigation-dialog]"));
        cut.Render(p => p.Add(c => c.ShellInlineSize, 390));
        cut.Find("[data-navigation-trigger]").Click();
        cut.Render(p => p.Add(c => c.Owner, Bob).Add(c => c.Presentation, View(Bob)));
        Assert.Empty(cut.FindAll("[data-navigation-dialog]"));
        Assert.Empty(requests);
    }

    [Fact]
    public void Dirty_close_isolates_all_frame_background_callbacks_and_keeps_editor_until_ack()
    {
        var requests = new List<AppShellRev6FrameIntent>();
        var view = View();
        var cut = Frame(view, requests, 1236);
        var surface = cut.FindComponent<HarborlineAppShellRev6TabSurface>().Instance;
        var dirty = view with { Tabs = view.Tabs with { PendingClose = "details:a" } };
        cut.Render(p => p.Add(c => c.Presentation, dirty));
        Assert.True(cut.Find("[data-frame-background]").HasAttribute("inert"));
        Assert.Equal("true", cut.Find("[data-frame-background]").GetAttribute("aria-hidden"));
        Assert.Empty(cut.FindAll("[data-frame-background] [role=dialog]"));
        Assert.Single(cut.FindAll("[data-dirty-dialog]"));
        cut.Find("[data-nav-id=records]").Click();
        cut.Find(".hl-rev6-search").Click();
        cut.Find("[data-panel-toggle]").Click();
        cut.Find("[data-tab-close]").Click();
        cut.Find("[data-row-id=record-a]").DoubleClick();
        cut.Find(".hl-view-runtime__actions button").Click();
        Assert.Empty(requests);
        cut.FindAll("[data-dirty-dialog] button")[0].Click();
        Assert.Equal(new AppShellRev6FrameTabIntent(Alice, new("resolve-close", Choice: AppShellRev6CloseChoice.Save)), Assert.Single(requests));
        Assert.True(cut.FindAll("[data-dirty-dialog] button")[0].HasAttribute("disabled"));
        cut.FindAll("[data-dirty-dialog] button")[0].Click();
        Assert.Single(requests);
        cut.Render(p => p.Add(c => c.Presentation, dirty with { PanelOpen = false, Tabs = dirty.Tabs with { SaveRequest = new(Alice, "details:a", 7, 42) } }));
        Assert.Single(cut.FindAll("[data-dirty-dialog]"));
        Assert.Contains("waiting for adapter acknowledgment", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Same(surface, cut.FindComponent<HarborlineAppShellRev6TabSurface>().Instance);
        Assert.Contains("Keep literal draft", cut.Markup, StringComparison.Ordinal);
        cut.Render(p => p.Add(c => c.Presentation, view with { Tabs = view.Tabs with { Tabs = [], Active = null } }));
        Assert.Empty(cut.FindAll("[data-dirty-dialog]"));
        Assert.Empty(cut.FindAll("[role=tab]"));
    }

    [Fact]
    public void Phone_dirty_modal_supersedes_drawer_and_instance_ids_are_distinct()
    {
        var view = View();
        var first = Frame(view, [], 390);
        var second = Frame(view, [], 390);
        Assert.NotEqual(first.Find("[data-navigation-trigger]").GetAttribute("aria-controls"), second.Find("[data-navigation-trigger]").GetAttribute("aria-controls"));
        first.Find("[data-navigation-trigger]").Click();
        first.Render(p => p.Add(c => c.Presentation, view with { Tabs = view.Tabs with { PendingClose = "details:a" } }));
        Assert.Empty(first.FindAll("[data-navigation-dialog]"));
        Assert.Single(first.FindAll("[data-dirty-dialog]"));
        first.Render(p => p.Add(c => c.Presentation, view));
        Assert.Empty(first.FindAll("[data-navigation-dialog]"));
        var css = string.Join(Environment.NewLine, first.FindAll("style").Select(style => style.TextContent));
        Assert.Contains("padding-inline-end: 70px", css, StringComparison.Ordinal);
        Assert.Contains("inline-size: 90%", css, StringComparison.Ordinal);
        Assert.Contains("inset-block: 0; inset-inline-start: 0; inline-size: 90%", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_navigation_and_native_href_fallback_keep_supplied_identities()
    {
        var view = View() with { Mode = AppShellRev6FrameMode.Chat };
        var cut = Render<HarborlineAppShellRev6Frame>(p => p.Add(c => c.ShellId, "native-href")
            .Add(c => c.Owner, Alice).Add(c => c.Presentation, view).Add(c => c.ShellInlineSize, 1600));
        Assert.Equal(new[] { "home", "my-work", "conversations", "browse", "account" },
            cut.FindAll("nav [data-drawer-row]").Select(row => row.GetAttribute("data-nav-id") ?? "account"));
        Assert.Equal("/work", cut.Find("[data-nav-id=my-work]").GetAttribute("href"));
        Assert.Equal("My work 2", cut.Find("[data-nav-id=my-work]").GetAttribute("aria-label"));
        cut.Find("[data-nav-id=my-work]").Click();
        Assert.Equal("/work", cut.Find("[data-nav-id=my-work]").GetAttribute("href"));
    }

    [Fact]
    public void Disabled_header_action_exposes_its_supplied_reason()
    {
        var view = View();
        var cut = Frame(view with { Header = view.Header with { Actions = [new("share", "Share", false, "Permission is required.")] } }, [], 1600);
        var action = cut.Find(".hl-rev6-actions button");
        Assert.True(action.HasAttribute("disabled"));
        Assert.Equal("Permission is required.", action.GetAttribute("title"));
        Assert.Equal("Permission is required.", cut.Find("#" + action.GetAttribute("aria-describedby")).TextContent);
    }

    private IRenderedComponent<HarborlineAppShellRev6Frame> Frame(AppShellRev6FramePresentation view, List<AppShellRev6FrameIntent> requests, double? width) =>
        Render<HarborlineAppShellRev6Frame>(p => p.Add(c => c.ShellId, "frame-test").Add(c => c.Owner, view.Owner)
            .Add(c => c.Presentation, view).Add(c => c.ShellInlineSize, width).Add(c => c.Intent, value => requests.Add(value))
            .Add(c => c.TabContent, (RenderFragment<AppShellRev6Tab>)(_ => builder => builder.AddMarkupContent(0, "<label>Draft<input aria-label='Draft' value='Keep literal draft' /></label>"))));

    private static AppShellRev6FramePresentation View(AppShellRev6Owner? owner = null)
    {
        var who = owner ?? Alice;
        var plan = new ViewRenderPlan("literal-hash", "literal-view", "1", "literal-pack", "1", "ViewDefinition", new("layout.table", new([new("record", "Record")]), [new("inspect", "Inspect")]));
        return new(who, AppShellRev6FrameMode.Workspace,
            new("EX", "Example Home", "/", [
                new(AppShellRev6FrameRailGroup.Conversations, new("conversations", "Conversations", "/conversations", "chat", false)),
                new(AppShellRev6FrameRailGroup.Primary, new("records", "Records", "/records", "grid", true)),
                new(AppShellRev6FrameRailGroup.MyWork, new("my-work", "My work", "/work", "work", false, 2)),
                new(AppShellRev6FrameRailGroup.Secondary, new("pages", "Pages", "/pages", "page", false)),
                new(AppShellRev6FrameRailGroup.Browse, new("browse", "Browse", "/browse", "browse", false))], "AR", "Account Alice"),
            new("Records", "Everywhere", [new("views", "Views", [new("all", "All", "/all", "grid", true, 3)])]),
            new("Records", "Home / Records", "Search", "Ask or search", []),
            new(who, AppShellRev6FrameFaceKind.Structured, "records", plan, [new("record-a", new Dictionary<string, object?> { ["record"] = "Literal sample row" })], "Literal records", "No literal rows."),
            new(who, "records", [new("details:a", "details", "a", false, false, true)], "details:a", null, null), true);
    }
}
