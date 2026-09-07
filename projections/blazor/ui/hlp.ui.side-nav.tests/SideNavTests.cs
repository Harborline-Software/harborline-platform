using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SideNavTests : BunitContext
{
    private static readonly SideNavNavigationItem[] Items = [new("home", "Home"), new("reports", "Reports", Children: [new("daily", "Daily")])];
    [Fact] public void ActiveDescendantExpandsAndCarriesCurrentPage() { var cut = Render<HarborlineSideNav>(p => p.Add(x => x.Items, Items).Add(x => x.ActiveItemId, "daily")); Assert.Equal("true", cut.Find("button[aria-expanded]").GetAttribute("aria-expanded")); Assert.Equal("page", cut.Find("[data-item-id=daily] [aria-current]").GetAttribute("aria-current")); }
    [Fact] public void DisabledLeafDoesNotActivateAndCollapsedKeepsName() { var count = 0; var items = new[] { new SideNavNavigationItem("locked", "Locked", Disabled: true) }; var cut = Render<HarborlineSideNav>(p => p.Add(x => x.Items, items).Add(x => x.Collapsed, true).Add(x => x.ItemActivated, _ => count++)); cut.Find("button").Click(); Assert.Equal(0, count); Assert.Equal("Locked", cut.Find("button").GetAttribute("aria-label")); Assert.Equal("tooltip", cut.Find("[role=tooltip]").GetAttribute("role")); }
    [Fact] public void InteractiveTrailingContentIsSibling() { RenderFragment accessory = builder => { builder.OpenElement(0, "button"); builder.AddContent(1, "Pin"); builder.CloseElement(); }; var cut = Render<HarborlineSideNav>(p => p.Add(x => x.Items, new[] { new SideNavNavigationItem("home", "Home", TrailingContent: accessory) })); Assert.Single(cut.FindAll(".hl-side-nav__row > .hl-side-nav__accessory > button")); Assert.Empty(cut.FindAll(".hl-side-nav__control button")); }
    [Fact, Trait("ModuleConformance", "hlp.ui.side-nav")] public void SharedFixtureConforms() { AssertFixture("side-nav."); }
    private void AssertFixture(string prefix)
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var id = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith(prefix, id);
        if (id != "side-nav.class-vocabulary") return;
        // 282 s10: this lane once spelled the item list hl-side-nav__items, the badge slot
        // hl-side-nav__trailing and the collapsed root hl-side-nav--collapsed, none of them defined
        // by the authority stylesheet. The React case asserts the same fixture row in the other lane.
        var expected = fixture.RootElement.GetProperty("expected");
        RenderFragment badge = builder => { builder.OpenElement(0, "span"); builder.AddContent(1, "3"); builder.CloseElement(); };
        var items = new[] { new SideNavNavigationItem("reports", "Reports", Children: [new SideNavNavigationItem("daily", "Daily", TrailingContent: badge)]) };
        var cut = Render<HarborlineSideNav>(p => p.Add(x => x.Items, items).Add(x => x.ActiveItemId, "daily"));
        var lists = cut.FindAll("ul");
        Assert.Equal(Classes(expected, "listClasses"), lists[0].ClassList);
        Assert.Equal(Classes(expected, "nestedListClasses"), lists[1].ClassList);
        Assert.Equal(Classes(expected, "activeControlClasses"), cut.Find("[data-item-id=daily] .hl-side-nav__control").ClassList);
        Assert.Equal(Classes(expected, "branchControlClasses"), cut.Find("[data-item-id=reports] > .hl-side-nav__row > .hl-side-nav__control").ClassList);
        Assert.Equal(Classes(expected, "accessoryClasses"), cut.Find(".hl-side-nav__row > span:last-child").ClassList);

        var collapsed = Render<HarborlineSideNav>(p => p.Add(x => x.Items, items).Add(x => x.ActiveItemId, "daily").Add(x => x.Collapsed, true));
        Assert.Equal(Classes(expected, "collapsedRootClasses"), collapsed.Find("nav").ClassList);
        Assert.Equal(expected.GetProperty("collapsedTooltipClasses").GetProperty("blazor").EnumerateArray().Select(value => value.GetString()!).ToArray(), collapsed.Find("[role=tooltip]").ClassList);
    }
    private static string[] Classes(System.Text.Json.JsonElement expected, string property) => expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
