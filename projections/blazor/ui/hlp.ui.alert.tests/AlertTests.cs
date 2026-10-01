using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class AlertTests : BunitContext
{
    [Fact] public void RolesContentAndDismissalMatch(){var calls=0;var cut=Render<HarborlineAlert>(p=>p.Add(x=>x.Variant,AlertVariant.Error).Add(x=>x.Title,"Failed").Add(x=>x.Closable,true).Add(x=>x.DismissLabel,"Close notice").Add(x=>x.OnClose,()=>calls++).AddChildContent("Try again"));Assert.Equal("alert",cut.Find("[role]").GetAttribute("role"));cut.Find("button").Click();Assert.Equal(1,calls);}
    [Theory,Trait("ModuleConformance","hlp.ui.alert")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.alert", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("alert.",fixture.RootElement.GetProperty("id").GetString());Assert.Equal("status",Render<HarborlineAlert>(p=>p.AddChildContent("Body")).Find("[role]").GetAttribute("role"));
        });
    }
}
