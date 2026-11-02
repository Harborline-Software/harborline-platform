using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
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
    public async Task PointerOpen() { var cut=RenderMenu(); await cut.Find(".hl-context-menu-trigger").TriggerEventAsync("oncontextmenu",new MouseEventArgs{ClientX=40,ClientY=60}); Assert.Contains("left:40px;top:60px",cut.Find("[role=menu]").GetAttribute("style")); }

    [Fact, Trait("ConformanceCase", "context-menu.groups")]
    public async Task GroupsRender() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); Assert.Equal(3,cut.FindAll("[role=menuitem]").Count); Assert.Single(cut.FindAll("[role=separator]")); }

    [Fact, Trait("ConformanceCase", "context-menu.pointer-selection")]
    public async Task PointerSelection() { var count=0; var cut=RenderMenu(_=>count++); await cut.Instance.ShowAsync(8,8); cut.FindAll("[role=menuitem]")[1].Click(); Assert.Equal(1,count); Assert.False(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.disabled-selection")]
    public async Task DisabledSelection() { var count=0; var cut=RenderMenu(_=>count++); await cut.Instance.ShowAsync(8,8); cut.FindAll("[role=menuitem]")[0].Click(); Assert.Equal(0,count); Assert.True(cut.Instance.IsOpen); }

    [Fact, Trait("ConformanceCase", "context-menu.dismissal")]
    public async Task Dismissal() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="Escape"}); Assert.False(cut.Instance.IsOpen); }

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
    public async Task AccessibleContext() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); var menu=cut.Find("[role=menu]"); Assert.Equal("Actions",menu.GetAttribute("aria-label")); Assert.Equal("ar-SA",menu.GetAttribute("lang")); Assert.Equal("rtl",menu.GetAttribute("dir")); }

    [Fact, Trait("ConformanceCase", "context-menu.viewport-position")]
    public void ViewportPosition() => Assert.Equal((812d,552d),HarborlineContextMenu.ClampPosition(995,795,180,240,1000,800));

    [Fact, Trait("ConformanceCase", "context-menu.danger-icon")]
    public async Task DangerIcon() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); var item=cut.FindAll("[role=menuitem]")[2]; Assert.Equal("true",item.GetAttribute("data-danger")); Assert.NotNull(item.QuerySelector("[aria-hidden=true]")); }

    // T-1130: a host that handles Shift+F10 or the ContextMenu key on its own element opens the menu as keyboard-opened:
    // the first enabled item takes focus, and closing returns focus to the element focused when it opened.
    [Fact, Trait("Holds", "context-menu.quality.focus")]
    public async Task HostKeyboardOpenFocusesTheFirstItemAndClosingReturnsFocusToTheOpener()
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/context-menu.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderMenu();
        await cut.Instance.ShowFromKeyboardAsync(8, 8);
        Assert.Single(module.Invocations["triggerAnchor"]);
        Assert.Equal("copy", cut.Find("[role=menu]").GetAttribute("data-active-id"));
        Assert.Single(module.Invocations["focusActive"]);
        await cut.Instance.HideAsync();
        Assert.Single(module.Invocations["focusTrigger"]);
    }

    [Theory, Trait("Holds", "context-menu.quality.focus")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedHostKeyboardOpenPreservesOpenerUntilClosed(bool pointerOpened)
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/context-menu.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderMenu();
        if (pointerOpened) await cut.Instance.ShowAsync(8, 8);
        await cut.Instance.ShowFromKeyboardAsync(8, 8);
        await cut.Instance.ShowFromKeyboardAsync(40, 60);
        Assert.Single(module.Invocations["triggerAnchor"]);
        Assert.Equal("copy", cut.Find("[role=menu]").GetAttribute("data-active-id"));
        Assert.Contains("left:40px;top:60px", cut.Find("[role=menu]").GetAttribute("style"));
        await cut.Instance.HideAsync();
        Assert.Single(module.Invocations["focusTrigger"]);
        await cut.Instance.ShowFromKeyboardAsync(8, 8);
        Assert.Equal(2, module.Invocations["triggerAnchor"].Count);
    }

    [Fact, Trait("ConformanceCase", "context-menu.reopen-reset")]
    public async Task ReopenResets() { var cut=RenderMenu(); await cut.Instance.ShowAsync(8,8); await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="End"}); await cut.Instance.HideAsync(); await cut.Instance.ShowAsync(8,8); Assert.Equal("copy",cut.Find("[role=menu]").GetAttribute("data-active-id")); }
}
