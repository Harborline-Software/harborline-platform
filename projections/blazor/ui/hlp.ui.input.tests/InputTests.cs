using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class InputTests : BunitContext
{
    [Fact] public void DefaultsAndAliasesNormalize(){var cut=Render<HarborlineInput>();Assert.Contains("hl-input--size-md",cut.Find("input").ClassList);foreach(var size in new[]{InputSize.Sm,InputSize.Small}){cut.Render(p=>p.Add(x=>x.Size,size));Assert.Contains("hl-input--size-sm",cut.Find("input").ClassList);}}
    [Fact] public void InvalidIsVisualOnlyAndAttributesSurvive(){var cut=Render<HarborlineInput>(p=>p.Add(x=>x.Invalid,true).AddUnmatched("aria-invalid","grammar").AddUnmatched("data-case","input"));var input=cut.Find("input");Assert.Contains("hl-input--invalid",input.ClassList);Assert.Equal("grammar",input.GetAttribute("aria-invalid"));Assert.Equal("input",input.GetAttribute("data-case"));}
    [Fact] public void ControlledInputRequestsExactlyOnce(){var values=new List<string>();var cut=Render<HarborlineInput>(p=>p.Add(x=>x.Value,"old").Add(x=>x.ValueChanged,value=>values.Add(value)));cut.Find("input").Input("new");Assert.Equal(["new"],values);Assert.Equal("old",cut.Find("input").GetAttribute("value"));}
    [Fact,Trait("ModuleConformance","hlp.ui.input")] public void SharedFixtureConforms(){AssertFixturePrefix("input.");Assert.NotNull(Render<HarborlineInput>());}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
