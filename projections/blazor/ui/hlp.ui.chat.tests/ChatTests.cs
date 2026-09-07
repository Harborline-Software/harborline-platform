using Bunit;
using Harborline.UIAdapters.Blazor.Components.AI;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class ChatTests : BunitContext
{
    public ChatTests(){var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/chat.js");module.SetupVoid("scrollLogToEnd",_=>true).SetVoidResult();module.SetupVoid("focusMessage",_=>true).SetVoidResult();module.Setup<bool>("isNearEnd",_=>true).SetResult(true);}
    private static readonly ChatParticipant User = new("Ada");
    [Fact] public void MessagesPreserveExplicitRolesAndOrder(){var messages=new[]{new ChatMessage("a",User,ChatMessageRole.User,"Hi"),new ChatMessage("b",new("Copilot"),ChatMessageRole.Assistant,"Hello")};var cut=Render<HarborlineChat>(p=>p.Add(x=>x.Messages,messages).Add(x=>x.User,User));Assert.Equal(new[]{"a","b"},cut.FindAll("article").Select(x=>x.GetAttribute("data-hl-message-id")));Assert.Equal("user",cut.FindAll("article")[0].GetAttribute("data-hl-role"));}
    [Fact] public void ControlledSubmitTrimsAndRequestsClear(){var submitted=new List<string>();var changes=new List<string>();var cut=Render<HarborlineChat>(p=>p.Add(x=>x.Messages,Array.Empty<ChatMessage>()).Add(x=>x.User,User).Add(x=>x.InputValue,"  inspect  ").Add(x=>x.InputValueChanged,v=>changes.Add(v)).Add(x=>x.OnSubmit,v=>submitted.Add(v)));cut.Find("button.hl-chat__send").Click();Assert.Equal(new[]{"inspect"},submitted);Assert.Equal(new[]{""},changes);}
    [Fact] public void DisabledComposerSuppressesFormSubmitAndSuggestion(){var count=0;var cut=Render<HarborlineChat>(p=>p.Add(x=>x.Messages,Array.Empty<ChatMessage>()).Add(x=>x.User,User).Add(x=>x.InputValue,"inspect").Add(x=>x.InputValueChanged,_=>{}).Add(x=>x.OnSubmit,_=>count++).Add(x=>x.ComposerDisabled,true).Add(x=>x.Suggestions,new[]{new ChatSuggestion("Explain","explain")}));cut.Find("form").Submit();foreach(var button in cut.FindAll("button"))button.Click();Assert.Equal(0,count);}
    [Fact,Trait("ModuleConformance","hlp.ui.chat")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("chat.",fixture.RootElement.GetProperty("id").GetString());Assert.Equal("log",Render<HarborlineChat>(p=>p.Add(x=>x.Messages,Array.Empty<ChatMessage>()).Add(x=>x.User,User)).Find("[role=log]").GetAttribute("role"));}
}
