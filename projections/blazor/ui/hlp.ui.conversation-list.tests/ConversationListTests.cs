using Bunit;
using Harborline.UIAdapters.Blazor.Components.AI;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class ConversationListTests:BunitContext
{
 public ConversationListTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
 private static IReadOnlyList<ConversationSummary> Rows=>[new("c1","First","now","Preview"),new("c2","Second","yesterday")];
 [Fact] public void RowsAndActiveSemanticsMatch(){var cut=Render<HarborlineConversationList>(p=>p.Add(x=>x.Conversations,Rows).Add(x=>x.ActiveId,"c2").Add(x=>x.OnSelect,_=>{}));Assert.Equal(2,cut.FindAll("li").Count);Assert.Equal("true",cut.FindAll(".hl-conversation-list__select")[1].GetAttribute("aria-current"));}
 [Fact] public void RenameAndDeleteAreHostControlled(){var renamed=new List<(string,string)>();var deleted=new List<string>();var cut=Render<HarborlineConversationList>(p=>p.Add(x=>x.Conversations,Rows).Add(x=>x.OnSelect,_=>{}).Add(x=>x.OnRename,v=>renamed.Add(v)).Add(x=>x.OnDelete,id=>deleted.Add(id)));cut.Find("[aria-label='Rename: First']").Click();cut.Find("input").Input("  Renamed  ");cut.Find("form").Submit();Assert.Equal(("c1","Renamed"),renamed.Single());cut.Find("[aria-label='Delete: First']").Click();Assert.Empty(deleted);cut.Find(".danger").Click();Assert.Equal(["c1"],deleted);}
 [Fact,Trait("ModuleConformance","hlp.ui.conversation-list")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);var id=fixture.RootElement.GetProperty("id").GetString();Assert.StartsWith("conversation-list.",id);var cut=Render<HarborlineConversationList>(p=>p.Add(x=>x.Conversations,Rows).Add(x=>x.ActiveId,"c2").Add(x=>x.OnSelect,_=>{}).Add(x=>x.OnRename,_=>{}).Add(x=>x.OnDelete,_=>{}));Assert.NotNull(cut);
  if(id!="conversation-list.row-classes")return;
  // Both lanes emit exactly these classes on a row and its action group. This lane used to add
  // hl-conversation-list__row and hl-conversation-list__actions, which the authority never defined,
  // so the two lanes styled the same row differently and parity could not see it.
  var expected=fixture.RootElement.GetProperty("expected");
  var items=cut.FindAll("li");
  Assert.Equal(2,items.Count);
  Assert.Equal(Classes(expected,"itemClasses"),items[0].ClassList);
  Assert.Equal(Classes(expected,"activeItemClasses"),items[1].ClassList);
  var actions=cut.FindAll(".hl-conversation-list__row-actions");
  Assert.Equal(2,actions.Count);
  foreach(var group in actions)Assert.Equal(Classes(expected,"rowActionsClasses"),group.ClassList);
 }

 private static string[] Classes(System.Text.Json.JsonElement expected,string property)=>expected.GetProperty(property).EnumerateArray().Select(value=>value.GetString()!).ToArray();
}
