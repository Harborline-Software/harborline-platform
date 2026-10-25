using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ContextMenuConformanceTests : BunitContext
{
    public ContextMenuConformanceTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private static IReadOnlyList<ContextMenuGroup> Groups =>
    [
        new([new("blocked", "Blocked", Disabled: true), new("copy", "Copy")]),
        new([new("delete", "Delete", Danger: true, Icon: builder => builder.AddContent(0, "!"))]),
    ];

    private IRenderedComponent<HarborlineContextMenu> RenderMenu(Action<ContextMenuSelection>? selected = null) => Render<HarborlineContextMenu>(parameters => parameters
        .Add(component => component.Groups, Groups)
        .Add(component => component.AccessibleLabel, "Actions")
        .Add(component => component.Locale, "ar-SA")
        .Add(component => component.Direction, "rtl")
        .Add(component => component.OnSelect, selection => selected?.Invoke(selection))
        .AddChildContent("Target"));

    [Fact, Trait("ConformanceCase", "context-menu.closed")]
    public void Closed() => Assert.Empty(RenderMenu().FindAll("[role=menu]"));

    [Fact, Trait("ConformanceCase", "context-menu.pointer-open")]
    public async Task PointerOpen() { var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/context-menu.js"); module.SetupVoid("focusActive",_=>true); var cut=RenderMenu(); await cut.Find(".hl-context-menu-trigger").TriggerEventAsync("oncontextmenu",new MouseEventArgs{ClientX=40,ClientY=60}); Assert.Contains("left:40px;top:60px",cut.Find("[role=menu]").GetAttribute("style")); module.VerifyInvoke("focusActive"); }

    // T-962: a culture's negative sign need not be U+002D. ar, he and fa write -1 with a direction mark
    // before the minus; sv, nb, fi, lt, sl, hr and eu write U+2212. HTML integer parsing refuses both,
    // so every menu item became a tab stop. The roving tabindex is invariant.
    [Theory, InlineData("ar-SA"), InlineData("he-IL"), InlineData("fa-IR"), InlineData("sv-SE"), InlineData("nb-NO")]
    public async Task RovingTabIndexIsInvariantUnderEveryCulture(string culture) { var previous=System.Globalization.CultureInfo.CurrentCulture; try { System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo(culture); var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); var tabIndexes=cut.FindAll("[role=menuitem]").Select(item=>item.GetAttribute("tabindex")).ToArray(); Assert.All(tabIndexes, value=>Assert.Contains(value, new[]{"0","-1"})); Assert.Single(tabIndexes, value=>value=="0"); } finally { System.Globalization.CultureInfo.CurrentCulture=previous; } }
    // T-962 review: the fixed position is CSS, which takes a dot decimal and an ASCII minus whatever
    // the culture. de-DE writes 40.5 as "40,5" and ar-SA as "40٫5", and CSS drops the declaration, so
    // the menu fell back to its static position. Fractional coordinates come from getBoundingClientRect
    // after a keyboard open, a zoomed pointer open, or a caller's ShowAsync.
    [Theory, InlineData("de-DE"), InlineData("ar-SA"), InlineData("fa-IR"), InlineData("sv-SE")]
    public async Task PositionIsInvariantUnderEveryCulture(string culture) { var previous=System.Globalization.CultureInfo.CurrentCulture; try { System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo(culture); var cut=RenderMenu(); await cut.Instance.ShowAsync(40.5,60.25); Assert.Contains("left:40.5px;top:60.25px",cut.Find("[role=menu]").GetAttribute("style")); } finally { System.Globalization.CultureInfo.CurrentCulture=previous; } }
    [Fact, Trait("ConformanceCase", "context-menu.groups")]
    public async Task GroupsRender() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); Assert.Equal(3,cut.FindAll("[role=menuitem]").Count); Assert.Single(cut.FindAll("[role=separator]")); }

    [Fact, Trait("ConformanceCase", "context-menu.pointer-selection")]
    public async Task PointerSelection() { var count=0; var cut=RenderMenu(_=>count++); await cut.Instance.ShowAsync(8,8); cut.FindAll("[role=menuitem]")[1].Click(); Assert.Equal(1,count); Assert.False(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.disabled-selection")]
    public async Task DisabledSelection() { var count=0; var cut=RenderMenu(_=>count++); await cut.Instance.ShowAsync(8,8); cut.FindAll("[role=menuitem]")[0].Click(); Assert.Equal(0,count); Assert.True(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.dismissal")]
    public async Task Dismissal() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="Escape"}); Assert.False(cut.Instance.IsOpen); await cut.Instance.ShowAsync(8,8); await cut.Find(".hl-context-menu-overlay").MouseDownAsync(new MouseEventArgs{Button=0}); Assert.False(cut.Instance.IsOpen); await cut.Instance.ShowAsync(8,8); await cut.Find(".hl-context-menu-overlay").MouseDownAsync(new MouseEventArgs{Button=2}); Assert.False(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.keyboard-open")]
    public async Task KeyboardOpen() { var cut=RenderMenu(); await cut.Find(".hl-context-menu-trigger").KeyDownAsync(new KeyboardEventArgs{Key="F10",ShiftKey=true}); Assert.True(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.focus-entry")]
    public async Task FocusEntry() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); Assert.Equal("copy",cut.Find("[role=menu]").GetAttribute("data-active-id")); }

    [Fact, Trait("ConformanceCase", "context-menu.keyboard-navigation")]
    public async Task NavigationWraps() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); var menu=cut.Find("[role=menu]"); await menu.KeyDownAsync(new KeyboardEventArgs{Key="End"}); Assert.Equal("delete",cut.Find("[role=menu]").GetAttribute("data-active-id")); await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="ArrowDown"}); Assert.Equal("copy",cut.Find("[role=menu]").GetAttribute("data-active-id")); }

    [Fact, Trait("ConformanceCase", "context-menu.keyboard-activation")]
    public async Task KeyboardActivation() { string? id=null; var cut=RenderMenu(value=>id=value.ItemId); await cut.Instance.ShowAsync(8,8); await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="Enter"}); Assert.Equal("copy",id); Assert.False(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.focus-return")]
    public async Task FocusReturnState() { var cut=RenderMenu(); await cut.Find(".hl-context-menu-trigger").KeyDownAsync(new KeyboardEventArgs{Key="ContextMenu"}); await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="Escape"}); Assert.False(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.accessible-context")]
    public async Task AccessibleContext() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); var menu=cut.Find("[role=menu]"); Assert.Equal("Actions",menu.GetAttribute("aria-label")); Assert.Equal("ar-SA",menu.GetAttribute("lang")); Assert.Equal("rtl",menu.GetAttribute("dir")); var overlay=cut.Find(".hl-context-menu-overlay"); Assert.Equal("DIV",overlay.TagName); Assert.Equal("true",overlay.GetAttribute("aria-hidden")); Assert.Null(overlay.GetAttribute("tabindex")); Assert.Null(overlay.GetAttribute("role")); Assert.Null(overlay.GetAttribute("aria-label")); }

    [Fact, Trait("ConformanceCase", "context-menu.viewport-position")]
    public void ViewportPosition() => Assert.Equal((812d,552d),HarborlineContextMenu.ClampPosition(995,795,180,240,1000,800));

    [Fact, Trait("ConformanceCase", "context-menu.danger-icon")]
    public async Task DangerIcon() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); var item=cut.FindAll("[role=menuitem]")[2]; Assert.Equal("true",item.GetAttribute("data-danger")); Assert.NotNull(item.QuerySelector("[aria-hidden=true]")); }

    // T-943 review 2: every close returns focus to the element that had it when the menu opened, after a
    // pointer open as well as a keyboard one (APG menu pattern, as React). No ConformanceCase trait, so the
    // per-case runner's filter still selects exactly one test per case.
    [Theory]
    [InlineData("pointer", "escape")]
    [InlineData("pointer", "select")]
    [InlineData("pointer", "outside")]
    [InlineData("keyboard", "escape")]
    [InlineData("keyboard", "select")]
    [InlineData("keyboard", "outside")]
    public async Task EveryCloseReturnsFocusToTheOpener(string opening, string closing)
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/context-menu.js");
        module.SetupVoid("rememberOpener", _ => true).SetVoidResult();
        module.SetupVoid("focusActive", _ => true).SetVoidResult();
        module.SetupVoid("focusTrigger", _ => true).SetVoidResult();
        var cut = RenderMenu();
        if (opening == "pointer")
        {
            await cut.Find(".hl-context-menu-trigger").TriggerEventAsync("oncontextmenu", new MouseEventArgs { ClientX = 40, ClientY = 60 });
            module.VerifyInvoke("rememberOpener");
        }
        else await cut.Find(".hl-context-menu-trigger").KeyDownAsync(new KeyboardEventArgs { Key = "F10", ShiftKey = true });
        Assert.True(cut.Instance.IsOpen);
        if (closing == "escape") await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        else if (closing == "select") cut.FindAll("[role=menuitem]")[1].Click();
        else await cut.Find(".hl-context-menu-overlay").MouseDownAsync(new MouseEventArgs { Button = 0 });
        Assert.False(cut.Instance.IsOpen);
        module.VerifyInvoke("focusTrigger");
    }

    // T-943 review 3: a re-open while the menu is open keeps the opener recorded at the closed-to-open
    // transition, instead of recording one of the menu's own items.
    [Fact]
    public async Task ReopenWhileOpenKeepsTheOpener()
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/context-menu.js");
        module.SetupVoid("rememberOpener", _ => true).SetVoidResult();
        var cut = RenderMenu();
        await cut.Find(".hl-context-menu-trigger").KeyDownAsync(new KeyboardEventArgs { Key = "F10", ShiftKey = true });
        await cut.InvokeAsync(() => cut.Instance.ShowAsync(20, 20));
        Assert.True(cut.Instance.IsOpen);
        module.VerifyNotInvoke("rememberOpener");
        await cut.Instance.HideAsync();
        await cut.InvokeAsync(() => cut.Instance.ShowAsync(20, 20));
        module.VerifyInvoke("rememberOpener");
    }

    [Fact, Trait("ConformanceCase", "context-menu.reopen-reset")]
    public async Task ReopenResets() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="End"}); await cut.Instance.HideAsync(); await cut.Instance.ShowAsync(8,8); Assert.Equal("copy",cut.Find("[role=menu]").GetAttribute("data-active-id")); }
}
