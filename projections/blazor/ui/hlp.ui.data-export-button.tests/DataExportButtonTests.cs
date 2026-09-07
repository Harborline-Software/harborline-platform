using Bunit;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class DataExportButtonTests : BunitContext
{
    public DataExportButtonTests() => JSInterop.Mode = JSRuntimeMode.Loose;
    [Fact] public void OpensOrderedMenuAndInvokesOnce(){ExportFormat? selected=null;var cut=Render<HarborlineDataExportButton>(p=>p.Add(x=>x.Formats,[ExportFormat.Pdf,ExportFormat.Json]).Add(x=>x.OnExport,value=>selected=value));cut.Find("button").Click();Assert.Equal(["pdf","json"],cut.FindAll("[role=menuitem]").Select(x=>x.GetAttribute("data-format")));cut.FindAll("[role=menuitem]")[0].Click();Assert.Equal(ExportFormat.Pdf,selected);Assert.Empty(cut.FindAll("[role=menu]"));}
    [Fact] public void SingleFormatInvokesDirectlyWithoutMenuSemantics(){ExportFormat? selected=null;var cut=Render<HarborlineDataExportButton>(p=>p.Add(x=>x.Formats,[ExportFormat.Csv]).Add(x=>x.OnExport,value=>selected=value));var button=cut.Find("button");Assert.Null(button.GetAttribute("aria-haspopup"));button.Click();Assert.Equal(ExportFormat.Csv,selected);Assert.Empty(cut.FindAll("[role=menu]"));}
    [Fact,Trait("ModuleConformance","hlp.ui.data-export-button")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("data-export.",fixture.RootElement.GetProperty("id").GetString());Assert.Equal("false",Render<HarborlineDataExportButton>().Find("button").GetAttribute("aria-expanded"));}
}
