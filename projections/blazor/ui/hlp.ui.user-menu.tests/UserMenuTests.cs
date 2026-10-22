using Bunit;
using Harborline.UIAdapters.Blazor.Shell;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class UserMenuTests : BunitContext
{
    public UserMenuTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    [Fact] public void SimpleItemsUseMenuAndPreserveOrder(){var items=new UserMenuEntry[]{new("profile",UserMenuEntryKind.Action,"Profile",ActivateAsync:()=>Task.CompletedTask),new("settings",UserMenuEntryKind.Link,"Settings",Destination:"/settings")};var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada Lovelace","ada@example.test")).Add(x=>x.Items,items));Assert.Equal("AL",cut.Find(".hl-user-menu__avatar").TextContent.Trim());cut.Find(".hl-user-menu__trigger").Click();Assert.Equal("menu",cut.Find(".hl-user-menu__surface").GetAttribute("role"));Assert.False(cut.Find(".hl-user-menu__surface").HasAttribute("aria-modal"));Assert.Equal(new[]{"profile","settings"},cut.FindAll("[data-hl-item]").Select(node=>node.GetAttribute("data-hl-item")));}
    [Fact] public void CustomContentUsesDialog(){var items=new[]{new UserMenuEntry("theme",UserMenuEntryKind.Custom,Content:builder=>builder.AddMarkupContent(0,"<button>Theme</button>"),CloseOnSelect:false)};var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada")).Add(x=>x.Items,items));cut.Find(".hl-user-menu__trigger").Click();Assert.Equal("dialog",cut.Find(".hl-user-menu__surface").GetAttribute("role"));}
    [Fact] public void ActionInvokesOnceAndCloses(){var calls=0;var items=new[]{new UserMenuEntry("profile",UserMenuEntryKind.Action,"Profile",ActivateAsync:()=>{calls++;return Task.CompletedTask;})};var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada")).Add(x=>x.Items,items));cut.Find(".hl-user-menu__trigger").Click();cut.Find("[data-hl-item=profile]").Click();Assert.Equal(1,calls);Assert.Empty(cut.FindAll(".hl-user-menu__surface"));}
    // Roving focus must reach the item that was navigated to whatever kinds and separators precede it,
    // must include the appended Sign out entry (as in React), and typeahead must cycle from the
    // current item. Focus requests are asserted by the item identity they name.
    private static UserMenuEntry[] Mixed()=>[new("profile",UserMenuEntryKind.Action,"Profile",ActivateAsync:()=>Task.CompletedTask),new("docs",UserMenuEntryKind.Link,"Docs",Destination:"/docs"),new("separator-1",UserMenuEntryKind.Separator),new("settings",UserMenuEntryKind.Action,"Settings",ActivateAsync:()=>Task.CompletedTask)];
    private (IRenderedComponent<HarborlineUserMenu> Cut,BunitJSModuleInterop Module,List<bool> Changes) OpenMixed()
    {
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/user-menu.js");module.Mode=JSRuntimeMode.Loose;var changes=new List<bool>();
        var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada")).Add(x=>x.Items,Mixed()).Add(x=>x.OnSignOut,()=>{}).Add(x=>x.OpenChanged,v=>changes.Add(v)));
        cut.Find("button.hl-user-menu__trigger").Click();
        return (cut,module,changes);
    }
    private static string? FocusedItem(BunitJSModuleInterop module)=>module.Invocations["focusItem"].Count==0?null:module.Invocations["focusItem"][^1].Arguments[1] as string??"<sign-out>";
    [Fact] public void ArrowsReachTheNavigatedItemAcrossKindsSeparatorsAndSignOut()
    {
        var (cut,module,_)=OpenMixed();var panel=cut.Find("[role=menu]");
        var visited=new List<string?>();
        foreach(var key in new[]{"ArrowDown","ArrowDown","ArrowDown","ArrowDown","Home","End"}){panel.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key=key});visited.Add(FocusedItem(module));}
        Assert.Equal(["docs","settings","<sign-out>","profile","profile","<sign-out>"],visited);
    }
    [Fact] public void TypeaheadCyclesFromTheCurrentItem()
    {
        var (cut,module,_)=OpenMixed();var panel=cut.Find("[role=menu]");
        var visited=new List<string?>();
        foreach(var key in new[]{"s","s","s"}){panel.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key=key});visited.Add(FocusedItem(module));}
        Assert.Equal(["settings","<sign-out>","settings"],visited);
    }
    [Fact] public void TabLeavingTheMenuClosesItWithoutPullingFocusBack()
    {
        var (cut,module,changes)=OpenMixed();var focusBefore=module.Invocations["focus"].Count;
        cut.Find("[role=menu]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key="Tab"});
        Assert.Empty(cut.FindAll("[role=menu]"));
        Assert.Equal([true,false],changes);
        Assert.Equal(focusBefore,module.Invocations["focus"].Count);
    }
    [Theory,Trait("ModuleConformance","hlp.ui.user-menu")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.user-menu", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("user-menu.");Assert.NotNull(Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada"))));
        });
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
