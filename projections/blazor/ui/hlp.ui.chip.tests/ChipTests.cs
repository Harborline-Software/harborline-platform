using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class ChipTests:BunitContext
{
 [Fact] public void DefaultsAndControlledSelectionMatch(){var requested=new List<bool>();var cut=Render<HarborlineChip>(p=>p.Add(x=>x.Text,"Priority").Add(x=>x.Selected,false).Add(x=>x.SelectedChanged,v=>requested.Add(v)));Assert.Equal("false",cut.Find(".hl-chip").GetAttribute("data-selected"));cut.Find(".hl-chip__body").Click();Assert.Equal([true],requested);Assert.Equal("false",cut.Find(".hl-chip").GetAttribute("data-selected"));}
 [Fact] public async Task RemovalIsSiblingAndDoesNotToggle(){var remove=0;var select=0;var cut=Render<HarborlineChip>(p=>p.Add(x=>x.Text,"Priority").Add(x=>x.Removable,true).Add(x=>x.OnRemove,()=>remove++).Add(x=>x.SelectedChanged,_=>select++));Assert.Equal(2,cut.FindAll("button").Count);await cut.Find(".hl-chip__remove").KeyDownAsync(new KeyboardEventArgs{Key="Delete"});Assert.Equal(1,remove);Assert.Equal(0,select);}
 [Fact,Trait("ModuleConformance","hlp.ui.chip")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("chip.",fixture.RootElement.GetProperty("id").GetString());Assert.NotNull(Render<HarborlineChip>(p=>p.Add(x=>x.Text,"Chip")));}
}
