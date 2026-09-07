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
    [Fact,Trait("ModuleConformance","hlp.ui.user-menu")] public void SharedFixtureConforms(){AssertFixturePrefix("user-menu.");Assert.NotNull(Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada"))));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
