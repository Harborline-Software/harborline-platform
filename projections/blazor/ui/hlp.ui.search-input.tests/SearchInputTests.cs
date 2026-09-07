using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SearchInputTests : BunitContext
{
    [Fact] public async Task CoalescesToLatestValue(){var values=new List<string>();var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.DebounceMs,250).Add(x=>x.ValueChanged,value=>values.Add(value)));cut.Find("input").Input("o");cut.Find("input").Input("oak");await cut.WaitForAssertionAsync(()=>Assert.Equal(["oak"],values),TimeSpan.FromSeconds(5));Assert.Equal("false",cut.Find(".hl-search-input").GetAttribute("aria-busy"));}
    [Fact] public void ClearPublishesImmediately(){var values=new List<string>();var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.Value,"oak").Add(x=>x.DebounceMs,1000).Add(x=>x.ValueChanged,value=>values.Add(value)));cut.Find("input").Input("stale");cut.Find("button").Click();Assert.Equal([string.Empty],values);Assert.Empty(cut.FindAll("button"));}
    [Fact] public void ExternalValueReplacesDraft(){var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.Value,"old"));cut.Find("input").Input("draft");cut.Render(p=>p.Add(x=>x.Value,"saved"));Assert.Equal("saved",cut.Find("input").GetAttribute("value"));Assert.Equal("false",cut.Find(".hl-search-input").GetAttribute("aria-busy"));}
    [Fact,Trait("ModuleConformance","hlp.ui.search-input")] public void SharedFixtureConforms(){AssertFixturePrefix("search-input.");Assert.NotNull(Render<HarborlineSearchInput>());}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
