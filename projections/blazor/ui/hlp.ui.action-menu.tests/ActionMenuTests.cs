using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ActionMenuTests:BunitContext
{
 public ActionMenuTests(){JSInterop.Mode=JSRuntimeMode.Loose;Services.AddSingleton<IOutsidePointerObserver>(new FakeOutside());}
 private static IReadOnlyList<ActionMenuEntry> Items=>[new ActionMenuItem("edit","Edit"),new ActionMenuItem("blocked","Blocked",Disabled:true),new ActionMenuSeparator(),new ActionMenuItem("delete","Delete",Destructive:true)];
 private IRenderedComponent<HarborlineActionMenu> RenderMenu(Action<ActionMenuItem>? selected=null)=>Render<HarborlineActionMenu>(p=>p.Add(x=>x.Items,Items).Add(x=>x.OnSelect,item=>selected?.Invoke(item)));
 [Fact] public void ClosedAndTriggerSemantics(){var cut=RenderMenu();Assert.Empty(cut.FindAll("[role=menu]"));Assert.Equal("menu",cut.Find("button").GetAttribute("aria-haspopup"));}
 [Fact] public void CustomTriggerUsesTheSharedResilientTreatment(){var cut=Render<HarborlineActionMenu>(p=>p.Add(x=>x.Items,Items).Add(x=>x.AccessibleLabel,"Review certification").Add(x=>x.Trigger,b=>b.AddContent(0,"Review certification")));var trigger=cut.Find("button");Assert.Contains("hl-action-menu__trigger--custom",trigger.ClassList);Assert.Equal("Review certification",trigger.GetAttribute("aria-label"));}
 [Fact] public async Task KeyboardNavigationSkipsAndWraps(){var cut=RenderMenu();await cut.Find("button").KeyDownAsync(new KeyboardEventArgs{Key="ArrowDown"});Assert.Equal("edit",cut.Find("[role=menu]").GetAttribute("data-active-id"));await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="End"});Assert.Equal("delete",cut.Find("[role=menu]").GetAttribute("data-active-id"));await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="ArrowDown"});Assert.Equal("edit",cut.Find("[role=menu]").GetAttribute("data-active-id"));}
 [Fact] public async Task SelectionAndEscapeClose(){var calls=0;var cut=RenderMenu(_=>calls++);cut.Find("button").Click();cut.FindAll("[role=menuitem]")[0].Click();Assert.Equal(1,calls);Assert.False(cut.Instance.IsOpen);cut.Find("button").Click();await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="Escape"});Assert.False(cut.Instance.IsOpen);}
 [Fact] public void EmptyCollectionDrawsTheDeclaredMessageAndKeepsTheTrigger(){
  var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(FixturePath));
  var shared=doc.RootElement.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()=="action-menu.empty-collection");
  var sets=shared.GetProperty("input").GetProperty("sets").EnumerateArray().Select(x=>x.EnumerateArray().Select(y=>y.GetString()!).ToArray()).ToArray();
  var expected=shared.GetProperty("expected");
  var states=expected.GetProperty("menuState").EnumerateArray().Select(x=>x.GetString()!).ToArray();
  var messages=expected.GetProperty("emptyMessages").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
  var items=expected.GetProperty("menuItems").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
  Assert.True(expected.GetProperty("triggerPresent").GetBoolean());
  for(var i=0;i<sets.Length;i++){
   var cut=Render<HarborlineActionMenu>(p=>p.Add(x=>x.Items,Build(sets[i])).Add(x=>x.ScopeId,$"action-menu.empty-collection:{i}"));
   var trigger=cut.Find("button.hl-action-menu__trigger");
   Assert.NotNull(trigger);
   trigger.Click();
   Assert.Equal(states[i],cut.Find("[role=menu]").GetAttribute("data-hl-state"));
   Assert.Equal(messages[i],cut.FindAll(".hl-action-menu__empty").Count);
   Assert.Equal(items[i],cut.FindAll("button.hl-action-menu__item").Count);
  }
 }
 [Fact] public void EmptyTextPrefersTheCallerThenTheCatalogThenTheDefault(){
  var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(FixturePath));
  var shared=doc.RootElement.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()=="action-menu.empty-collection");
  var expected=shared.GetProperty("expected");
  var byDefault=Render<HarborlineActionMenu>(p=>p.Add(x=>x.Items,(IReadOnlyList<ActionMenuEntry>)[]).Add(x=>x.ScopeId,"action-menu.empty-text:default"));
  byDefault.Find("button.hl-action-menu__trigger").Click();
  Assert.Equal(expected.GetProperty("defaultText").GetString(),byDefault.Find(".hl-action-menu__empty").TextContent.Trim());
  var overridden=Render<HarborlineActionMenu>(p=>p.Add(x=>x.Items,(IReadOnlyList<ActionMenuEntry>)[]).Add(x=>x.Empty,shared.GetProperty("input").GetProperty("empty").GetString()).Add(x=>x.ScopeId,"action-menu.empty-text:override"));
  overridden.Find("button.hl-action-menu__trigger").Click();
  Assert.Equal(expected.GetProperty("overrideText").GetString(),overridden.Find(".hl-action-menu__empty").TextContent.Trim());
 }
 [Fact] public void BlankEmptyTextFallsBackToTheDefault(){
  var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(FixturePath));
  var shared=doc.RootElement.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()=="action-menu.blankEmptyText");
  var text=shared.GetProperty("input").GetProperty("empty").GetString();
  var expected=shared.GetProperty("expected");
  var cut=Render<HarborlineActionMenu>(p=>p.Add(x=>x.Items,(IReadOnlyList<ActionMenuEntry>)[]).Add(x=>x.Empty,text));
  cut.Find("button.hl-action-menu__trigger").Click();
  Assert.Equal(expected.GetProperty("defaultText").GetString(),cut.Find(".hl-action-menu__empty").TextContent.Trim());
 }
 [Fact] public async Task EmptyMessageNeverTakesFocusAndTheMenuStaysDismissible(){
  var cut=Render<HarborlineActionMenu>(p=>p.Add(x=>x.Items,(IReadOnlyList<ActionMenuEntry>)[new ActionMenuSeparator()]));
  await cut.Find("button.hl-action-menu__trigger").KeyDownAsync(new KeyboardEventArgs{Key="ArrowDown"});
  var message=cut.Find(".hl-action-menu__empty");
  Assert.Equal("true",message.GetAttribute("aria-disabled"));
  Assert.Equal("-1",message.GetAttribute("tabindex"));
  Assert.Null(cut.Find("[role=menu]").GetAttribute("data-active-id"));
  await cut.Find("[role=menu]").KeyDownAsync(new KeyboardEventArgs{Key="Escape"});
  Assert.False(cut.Instance.IsOpen);
 }
 private static string FixturePath=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../../conformance/hlp.ui.action-menu/fixtures.yaml"));
 private static IReadOnlyList<ActionMenuEntry> Build(IReadOnlyList<string> set)=>[..set.Select<string,ActionMenuEntry>(kind=>kind=="separator"?new ActionMenuSeparator():new ActionMenuItem("blocked","Blocked",Disabled:kind=="disabled"))];
 [Fact,Trait("ModuleConformance","hlp.ui.action-menu")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("action-menu.",fixture.RootElement.GetProperty("id").GetString());Assert.NotNull(RenderMenu());}
 private sealed class FakeOutside:IOutsidePointerObserver{public ValueTask<IOutsidePointerRegistration> ObserveAsync(Microsoft.AspNetCore.Components.ElementReference e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>throw new NotSupportedException();public ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<Microsoft.AspNetCore.Components.ElementReference> e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>new(new Registration());private sealed class Registration:IOutsidePointerRegistration{public ValueTask DisposeAsync()=>ValueTask.CompletedTask;public ValueTask SetEnabledAsync(bool enabled)=>ValueTask.CompletedTask;public void SetCallback(Func<OutsidePointerEvent,ValueTask> c){}}}
}
