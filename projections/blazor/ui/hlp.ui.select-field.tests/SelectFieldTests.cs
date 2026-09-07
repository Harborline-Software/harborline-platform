using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SelectFieldTests : BunitContext
{
    private static IReadOnlyList<SelectOption> Options=>[new("active","Active"),new("pending","Pending")];
    public SelectFieldTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    [Fact] public void RendersControlledComboboxAndListbox(){var cut=Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"active").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status"));Assert.Equal("Active",cut.Find(".hl-select-field__value").TextContent);cut.Find("[role=combobox]").Click();Assert.Equal("true",cut.Find("[role=combobox]").GetAttribute("aria-expanded"));Assert.Equal(2,cut.FindAll("[role=option]").Count);}
    [Fact] public void SelectionRequestsValueAndCloseOnce(){var values=new List<string>();var opens=new List<bool>();var cut=Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"active").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status").Add(x=>x.ValueChanged,v=>values.Add(v)).Add(x=>x.OpenChanged,v=>opens.Add(v)));cut.Find("[role=combobox]").Click();cut.Find("[data-hl-option=pending]").Click();Assert.Equal(["pending"],values);Assert.Equal([true,false],opens);}
    [Fact] public void ControlledOpenRequestsWithoutChangingRenderedState(){var opens=new List<bool>();var cut=Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status").Add(x=>x.Open,false).Add(x=>x.OpenChanged,v=>opens.Add(v)));cut.Find("button").Click();Assert.Equal([true],opens);Assert.Equal("false",cut.Find("button").GetAttribute("aria-expanded"));}
    [Fact,Trait("ModuleConformance","hlp.ui.select-field")] public void SharedFixtureConforms(){AssertFixturePrefix("select-field.");Assert.NotNull(Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status")));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
