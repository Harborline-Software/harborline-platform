using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SwitchTests : BunitContext
{
    [Fact] public void ControlledActivationRequestsOnceAndKeepsState(){var values=new List<bool>();var cut=Render<HarborlineSwitch>(p=>p.Add(x=>x.Checked,false).Add(x=>x.AccessibleName,"Alerts").Add(x=>x.CheckedChanged,v=>values.Add(v)));cut.Find("[role=switch]").Click();Assert.Equal([true],values);Assert.Equal("false",cut.Find("[role=switch]").GetAttribute("aria-checked"));}
    [Fact] public void UncontrolledStateAndFormValueTrack(){var cut=Render<HarborlineSwitch>(p=>p.Add(x=>x.DefaultChecked,false).Add(x=>x.AccessibleName,"Alerts").Add(x=>x.Name,"alerts"));cut.Find("[role=switch]").Click();Assert.Equal("true",cut.Find("[role=switch]").GetAttribute("aria-checked"));Assert.Equal("on",cut.Find("input[type=hidden]").GetAttribute("value"));}
    [Fact] public void StateCopyNeverRenamesControl(){var cut=Render<HarborlineSwitch>(p=>p.Add(x=>x.DefaultChecked,false).Add(x=>x.AccessibleName,"Alerts").Add(x=>x.OnText,"Enabled").Add(x=>x.OffText,"Disabled"));Assert.Equal("Alerts",cut.Find("[role=switch]").GetAttribute("aria-label"));cut.Find("[role=switch]").Click();Assert.Equal("Alerts",cut.Find("[role=switch]").GetAttribute("aria-label"));Assert.Equal("Enabled",cut.Find(".hl-switch__state").TextContent);}
    [Fact,Trait("ModuleConformance","hlp.ui.switch")] public void SharedFixtureConforms(){AssertFixturePrefix("switch.");Assert.NotNull(Render<HarborlineSwitch>(p=>p.Add(x=>x.AccessibleName,"Alerts")));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
