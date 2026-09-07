using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class NumericTextBoxPerformanceTests : BunitContext
{
    [Fact] public void NinetySixReplacementsKeepConstantStructure(){var cut=Render<HarborlineNumericTextBox>(p=>p.Add(x=>x.Value,0));for(var i=0;i<96;i++){cut.Render(p=>p.Add(x=>x.Value,(double)i));Assert.Single(cut.FindAll("input"));Assert.Equal(2,cut.FindAll("button").Count);Assert.Equal($"{i}.00",cut.Find("input").GetAttribute("value"));}}
}
