using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppShellRev6TabHostTests : BunitContext
{
    private static readonly AppShellRev6Owner Alice = new("alice", "tenant-a", "workspace-a");
    private static readonly AppShellRev6Owner Bob = new("bob", "tenant-a", "workspace-a");

    public AppShellRev6TabHostTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/app-shell-rev6-focus.mjs");
    }

    // DES-0008 P8 and app-shell.tab-overflow: pinned first, icon-only, full names,
    // and unpin before close. Input order and host-owned state remain unchanged.
    [Fact]
    public void Pinned_tabs_are_icons_with_full_names_and_require_host_unpin_before_close()
    {
        var tabs = new[]
        {
            new AppShellRev6Tab("details:wo-1042", "details", "WO-1042", true, false, false),
            new AppShellRev6Tab("assistant:c-agent", "assistant", "c-agent", false, true, false),
            new AppShellRev6Tab("files:manual", "files", "manual", false, true, true),
        };
        var view = View(tabs, "details:wo-1042");
        var intents = new List<AppShellRev6Intent>();
        var cut = Host(view, intents);
        var rendered = cut.FindAll("[role=tab]");
        Assert.Equal(new string?[] { "assistant: c-agent Pinned", "files: manual Pinned Unsaved", "details: WO-1042 Preview" },
            rendered.Select(tab => tab.GetAttribute("aria-label")));
        Assert.Equal(new[] { "details:wo-1042", "assistant:c-agent", "files:manual" }, tabs.Select(tab => tab.Id));
        Assert.Empty(rendered[0].TextContent.Trim());
        Assert.Empty(rendered[1].TextContent.Trim());
        Assert.NotNull(rendered[0].QuerySelector("svg[aria-hidden=true]"));
        Assert.NotNull(rendered[1].QuerySelector("svg[aria-hidden=true]"));
        Assert.Single(cut.FindAll("[data-tab-label]"));
        Assert.Equal("true", rendered[2].GetAttribute("aria-selected"));
        Assert.Equal("0", rendered[2].GetAttribute("tabindex"));
        Assert.Equal("-1", rendered[0].GetAttribute("tabindex"));
        Assert.Equal(rendered[2].Id, cut.FindAll("[role=tabpanel]")[0].GetAttribute("aria-labelledby"));
        Assert.Equal(cut.FindAll("[role=tabpanel]")[0].Id, rendered[2].GetAttribute("aria-controls"));

        var actions = cut.FindAll("[role=tabpanel]")[1].QuerySelectorAll("button");
        Assert.Equal("Unpin", actions[1].TextContent);
        Assert.True(actions[2].HasAttribute("disabled"));
        actions[1].Click();
        Assert.Equal(new AppShellRev6Intent("unpin", "assistant:c-agent"), Assert.Single(intents));
        Assert.True(tabs[1].Pinned);
        Assert.True(cut.FindAll("[role=tabpanel]")[1].QuerySelectorAll("button")[2].HasAttribute("disabled"));

        intents.Clear();
        cut.Render(parameters => parameters.Add(component => component.Presentation,
            view with { Tabs = [tabs[0], tabs[1] with { Pinned = false }, tabs[2]] }));
        var close = cut.FindAll("[role=tabpanel]")[1].QuerySelectorAll("button")[2];
        Assert.False(close.HasAttribute("disabled"));
        close.Click();
        Assert.Equal(new AppShellRev6Intent("close", "assistant:c-agent"), Assert.Single(intents));
        Assert.Equal(3, cut.FindAll("[role=tab]").Count);
    }

    // The neutral many-tabs scenario specifies 200, the long-content scenario
    // specifies 120-character labels; CSS widths are declarations, not layout proof.
    [Fact]
    public void All_two_hundred_tabs_remain_searchable_and_open_intents_do_not_change_presentation()
    {
        var tabs = Enumerable.Range(0, 200).Select(index => new AppShellRev6Tab(
            $"details:{index}", "details", index == 0 ? new string('R', 120) : $"Record {index}", false, false, false)).ToArray();
        var view = View(tabs, "details:0");
        var intents = new List<AppShellRev6Intent>();
        var cut = Host(view, intents);
        Assert.Equal(200, cut.FindAll("[role=tab]").Count);
        Assert.Equal(200, cut.FindAll("[data-tab-search-target]").Count);
        Assert.Equal("Search tabs (200 open)", cut.Find("[data-tab-search-trigger]").GetAttribute("aria-label"));
        var search = cut.Find("[data-tab-search]");
        var invoker = cut.Find("[data-tab-search-trigger]");
        Assert.Equal("auto", search.GetAttribute("popover"));
        Assert.Equal(search.Id, invoker.GetAttribute("popovertarget"));
        Assert.Equal("toggle", invoker.GetAttribute("popovertargetaction"));
        Assert.Equal("region", search.GetAttribute("role"));
        Assert.Contains("position:fixed", search.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("100vw - 32px", search.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("100dvh - 32px", search.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("overflow:auto", search.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.DoesNotContain("position:absolute", search.GetAttribute("style")!, StringComparison.Ordinal);
        Assert.True(cut.Find("input[type=search]").HasAttribute("autofocus"));
        Assert.Equal("details: " + new string('R', 120), cut.FindAll("[role=tab]")[0].GetAttribute("aria-label"));
        Assert.Equal("details: " + new string('R', 120), cut.FindAll("[role=tab]")[0].GetAttribute("title"));
        Assert.Contains("overflow-x:auto", cut.Find("[data-tab-strip]").GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("min-inline-size:112px", cut.FindAll("[role=tab]")[0].GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("max-inline-size:168px", cut.FindAll("[role=tab]")[0].GetAttribute("style")!, StringComparison.Ordinal);
        cut.Find("input[type=search]").Input("Record 199");
        var result = Assert.Single(cut.FindAll("[data-tab-search-target]"));
        Assert.Equal("details: Record 199", result.TextContent);
        result.Click();
        Assert.Equal(new AppShellRev6Intent("open", "details:199", Kind: "details", Target: "Record 199"), Assert.Single(intents));
        Assert.Equal("true", cut.FindAll("[role=tab]")[0].GetAttribute("aria-selected"));
        Assert.Equal("false", cut.FindAll("[role=tab]")[199].GetAttribute("aria-selected"));
        cut.Render(parameters => parameters.Add(component => component.Presentation, view with { Active = "details:199" }));
        Assert.Equal("true", cut.FindAll("[role=tab]")[199].GetAttribute("aria-selected"));
        cut.Find("input[type=search]").Input("does not exist");
        Assert.Empty(cut.FindAll("[data-tab-search-target]"));
        Assert.Equal("No matching tabs.", cut.Find("[role=status]").TextContent);
        cut.Find("input[type=search]").Input("");
        Assert.Equal(200, cut.FindAll("[data-tab-search-target]").Count);
        Assert.Equal(200, view.Tabs.Length);
    }

    [Fact]
    public void Owner_change_clears_search_and_mismatched_presentation_is_never_rendered()
    {
        var view = View([new("details:alice", "details", "Alice private draft", false, false, true)], "details:alice");
        var intents = new List<AppShellRev6Intent>();
        var cut = Host(view, intents);
        cut.Find("input[type=search]").Input("Alice private draft");
        cut.Render(parameters => parameters.Add(component => component.Owner, Bob));
        Assert.Empty(cut.FindAll("[role=tab]"));
        Assert.Empty(cut.FindAll("input[type=search]"));
        Assert.Contains("No current workspace presentation.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice private draft", cut.Markup, StringComparison.Ordinal);
        var bob = View([new("details:bob", "details", "Bob record", false, false, false)], "details:bob", Bob);
        cut.Render(parameters => parameters.Add(component => component.Presentation, bob));
        Assert.Equal("", cut.Find("input[type=search]").GetAttribute("value"));
        Assert.Equal("details: Bob record", Assert.Single(cut.FindAll("[data-tab-search-target]")).TextContent);
        Assert.DoesNotContain("Alice private draft", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(intents);
    }

    [Fact]
    public void Dirty_close_and_save_acknowledgment_remain_host_owned_and_exclude_search()
    {
        var view = View([new("details:dirty", "details", "Draft", false, false, true)], "details:dirty");
        var intents = new List<AppShellRev6Intent>();
        var cut = Host(view, intents);
        Assert.Equal("auto", cut.Find("[data-tab-search]").GetAttribute("popover"));
        Assert.Single(cut.FindAll("[data-tab-search-trigger]"));
        view = view with { PendingClose = "details:dirty" };
        cut.Render(parameters => parameters.Add(component => component.Presentation, view));
        Assert.Single(cut.FindAll("[role=dialog][aria-modal=true]"));
        Assert.Empty(cut.FindAll("[data-tab-search]"));
        Assert.Empty(cut.FindAll("[data-tab-search-trigger]"));
        cut.FindAll("[role=dialog] button").Single(button => button.TextContent == "Save").Click();
        Assert.Equal(new AppShellRev6Intent("resolve-close", Choice: AppShellRev6CloseChoice.Save), Assert.Single(intents));
        Assert.Single(cut.FindAll("[role=tab]"));
        Assert.Single(cut.FindAll("[role=dialog]"));
        var request = new AppShellRev6SaveRequest(Alice, "details:dirty", 7, 42);
        cut.Render(parameters => parameters.Add(component => component.Presentation, view with { SaveRequest = request }));
        Assert.True(cut.FindAll("[role=dialog] button").Single(button => button.TextContent == "Save").HasAttribute("disabled"));
        Assert.Equal("Save requested; waiting for adapter acknowledgment.", cut.Find("[role=status]").TextContent);
        intents.Clear();
        cut.Find("[role=dialog]").KeyDown("Escape");
        Assert.Equal(new AppShellRev6Intent("resolve-close", Choice: AppShellRev6CloseChoice.Cancel), Assert.Single(intents));
        Assert.Single(cut.FindAll("[role=dialog]"));
        Assert.True(view.Tabs[0].Dirty);
    }

    [Fact]
    public void Two_instances_keep_tab_panel_and_search_identity_separate()
    {
        var view = View([new("details:one", "details", "One", false, false, false)], "details:one");
        var first = Host(view, []);
        var second = Host(view, []);
        Assert.NotEqual(first.Find("[role=tab]").Id, second.Find("[role=tab]").Id);
        Assert.NotEqual(first.Find("[data-tab-search-trigger]").GetAttribute("aria-controls"),
            second.Find("[data-tab-search-trigger]").GetAttribute("aria-controls"));
        Assert.Equal(first.Find("[role=tabpanel]").Id, first.Find("[role=tab]").GetAttribute("aria-controls"));
        Assert.Equal(second.Find("[role=tabpanel]").Id, second.Find("[role=tab]").GetAttribute("aria-controls"));
    }

    private IRenderedComponent<HarborlineAppShellRev6TabHost> Host(AppShellRev6Presentation view, List<AppShellRev6Intent> intents) =>
        Render<HarborlineAppShellRev6TabHost>(parameters => parameters
            .Add(component => component.ShellId, "native-p8")
            .Add(component => component.Owner, view.Owner)
            .Add(component => component.Presentation, view)
            .Add(component => component.Intent, intent => intents.Add(intent)));

    private static AppShellRev6Presentation View(AppShellRev6Tab[] tabs, string active, AppShellRev6Owner? owner = null) =>
        new(owner ?? Alice, "Workspace", tabs, active, null, null);
}
