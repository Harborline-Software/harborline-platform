using Harborline.Foundation.Navigation;
using Xunit;
namespace Harborline.Foundation.UI.Tests;
public sealed class SideNavGroupTests
{
    [Fact] public void PreservesOptionalLabelAndOrder(){var group=new SideNavGroup("main",[new SideNavItem("home","Home"),new SideNavItem("reports","Reports",children:[new SideNavItem("daily","Daily")])]);Assert.Null(group.Label);Assert.Equal(["home","reports"],group.Items.Select(x=>x.Id));Assert.Equal("daily",group.Items[1].Children[0].Id);}
    [Fact] public void RejectsBlankIdentity()=>Assert.Throws<ArgumentException>(()=>new SideNavGroup(" ",[]));
    [Theory,Trait("ModuleConformance","hlp.ui.side-nav-group")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.side-nav-group", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("side-nav-group.",fixture.RootElement.GetProperty("id").GetString());Assert.Equal("main",new SideNavGroup("main",[]).Id);
        });
    }
}
