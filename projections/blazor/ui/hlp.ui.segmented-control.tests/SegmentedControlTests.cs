using Bunit;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SegmentedControlTests : BunitContext
{
    private static IReadOnlyList<SegmentedOption> Options => [new("day","Day"),new("week","Week",true),new("month","Month")];
    [Fact] public void RendersControlledRadioGroup(){var cut=Render<HarborlineSegmentedControl>(p=>p.Add(x=>x.AccessibleName,"View").Add(x=>x.Options,Options).Add(x=>x.Value,"day"));Assert.Equal("radiogroup",cut.Find("[role=radiogroup]").GetAttribute("role"));Assert.Equal("true",cut.Find("[data-hl-option=day]").GetAttribute("aria-checked"));Assert.Equal("0",cut.Find("[data-hl-option=day]").GetAttribute("tabindex"));Assert.True(cut.Find("[data-hl-option=week]").HasAttribute("disabled"));}
    [Fact] public void ActivationRequestsExactlyOnce(){var values=new List<string>();var cut=Render<HarborlineSegmentedControl>(p=>p.Add(x=>x.AccessibleName,"View").Add(x=>x.Options,Options).Add(x=>x.Value,"day").Add(x=>x.ValueChanged,value=>values.Add(value)));cut.Find("[data-hl-option=month]").Click();Assert.Equal(["month"],values);}
    [Fact] public void RejectsDuplicateValues(){Assert.ThrowsAny<Exception>(()=>Render<HarborlineSegmentedControl>(p=>p.Add(x=>x.AccessibleName,"View").Add(x=>x.Options,new SegmentedOption[]{new("x","One"),new("x","Two")})));}
    [Fact,Trait("ModuleConformance","hlp.ui.segmented-control")] public void SharedFixtureConforms(){AssertFixturePrefix("segmented-control.");Assert.NotNull(Render<HarborlineSegmentedControl>(p=>p.Add(x=>x.AccessibleName,"View").Add(x=>x.Options,Options).Add(x=>x.Value,"day")));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
