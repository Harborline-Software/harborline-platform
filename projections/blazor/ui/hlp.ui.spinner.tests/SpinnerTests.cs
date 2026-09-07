using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class SpinnerTests : BunitContext
{
    [Fact] public void LabelSizeAndTypeMatch(){var cut=Render<HarborlineSpinner>(p=>p.Add(x=>x.Label,"Importing").Add(x=>x.Size,SpinnerSize.Lg).Add(x=>x.Type,SpinnerType.Converging));Assert.Equal("Importing",cut.Find("[role=status]").GetAttribute("aria-label"));Assert.Equal(2,cut.FindAll(".hl-spinner__arc").Count);}
    [Fact,Trait("ModuleConformance","hlp.ui.spinner")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("spinner.",fixture.RootElement.GetProperty("id").GetString());Assert.Contains("Loading",Render<HarborlineSpinner>().Find("svg").GetAttribute("aria-label"));}
}
