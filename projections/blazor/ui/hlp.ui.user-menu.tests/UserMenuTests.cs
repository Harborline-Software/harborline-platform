using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Localization;
using Harborline.UIAdapters.Blazor.Shell;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class UserMenuTests : BunitContext
{
    public UserMenuTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    [Fact] public void SimpleItemsUseMenuAndPreserveOrder(){var items=new UserMenuEntry[]{new("profile",UserMenuEntryKind.Action,"Profile",ActivateAsync:()=>Task.CompletedTask),new("settings",UserMenuEntryKind.Link,"Settings",Destination:"/settings")};var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada Lovelace","ada@example.test")).Add(x=>x.Items,items));Assert.Equal("AL",cut.Find(".hl-user-menu__avatar").TextContent.Trim());cut.Find(".hl-user-menu__trigger").Click();Assert.Equal("menu",cut.Find(".hl-user-menu__surface").GetAttribute("role"));Assert.False(cut.Find(".hl-user-menu__surface").HasAttribute("aria-modal"));Assert.Equal(new[]{"profile","settings"},cut.FindAll("[data-hl-item]").Select(node=>node.GetAttribute("data-hl-item")));}
    [Fact] public void CustomContentUsesDialog(){var items=new[]{new UserMenuEntry("theme",UserMenuEntryKind.Custom,Content:builder=>builder.AddMarkupContent(0,"<button>Theme</button>"),CloseOnSelect:false)};var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada")).Add(x=>x.Items,items));cut.Find(".hl-user-menu__trigger").Click();Assert.Equal("dialog",cut.Find(".hl-user-menu__surface").GetAttribute("role"));}
    [Fact] public void ActionInvokesOnceAndCloses(){var calls=0;var items=new[]{new UserMenuEntry("profile",UserMenuEntryKind.Action,"Profile",ActivateAsync:()=>{calls++;return Task.CompletedTask;})};var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada")).Add(x=>x.Items,items));cut.Find(".hl-user-menu__trigger").Click();cut.Find("[data-hl-item=profile]").Click();Assert.Equal(1,calls);Assert.Empty(cut.FindAll(".hl-user-menu__surface"));}
    // Roving focus must reach the item that was navigated to whatever kinds and separators precede it,
    // must include the appended Sign out entry (as in React), and typeahead must cycle from the
    // current item. Focus requests are asserted by the item identity they name.
    private static UserMenuEntry[] Mixed()=>[new("profile",UserMenuEntryKind.Action,"Profile",ActivateAsync:()=>Task.CompletedTask),new("docs",UserMenuEntryKind.Link,"Docs",Destination:"/docs"),new("separator-1",UserMenuEntryKind.Separator),new("settings",UserMenuEntryKind.Action,"Settings",ActivateAsync:()=>Task.CompletedTask)];
    private (IRenderedComponent<HarborlineUserMenu> Cut,BunitJSModuleInterop Module,List<bool> Changes) OpenMixed()
    {
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/user-menu.js");module.Mode=JSRuntimeMode.Loose;var changes=new List<bool>();
        var cut=Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada")).Add(x=>x.Items,Mixed()).Add(x=>x.OnSignOut,()=>{}).Add(x=>x.OpenChanged,v=>changes.Add(v)));
        cut.Find("button.hl-user-menu__trigger").Click();
        return (cut,module,changes);
    }
    private static string? FocusedItem(BunitJSModuleInterop module)=>module.Invocations["focusItem"].Count==0?null:module.Invocations["focusItem"][^1].Arguments[1] as string??"<sign-out>";
    [Fact] public void ArrowsReachTheNavigatedItemAcrossKindsSeparatorsAndSignOut()
    {
        var (cut,module,_)=OpenMixed();var panel=cut.Find("[role=menu]");
        var visited=new List<string?>();
        foreach(var key in new[]{"ArrowDown","ArrowDown","ArrowDown","ArrowDown","Home","End"}){panel.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key=key});visited.Add(FocusedItem(module));}
        Assert.Equal(["docs","settings","<sign-out>","profile","profile","<sign-out>"],visited);
    }
    [Fact] public void TypeaheadCyclesFromTheCurrentItem()
    {
        var (cut,module,_)=OpenMixed();var panel=cut.Find("[role=menu]");
        var visited=new List<string?>();
        foreach(var key in new[]{"s","s","s"}){panel.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key=key});visited.Add(FocusedItem(module));}
        Assert.Equal(["settings","<sign-out>","settings"],visited);
    }
    [Fact] public void DirectFocusMovesTheRovingPosition()
    {
        var (cut,module,_)=OpenMixed();
        cut.Find("[data-hl-item=docs]").Focus();
        cut.Find("[role=menu]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key="ArrowDown"});
        Assert.Equal("settings",FocusedItem(module));
    }
    [Fact] public void TypeaheadIgnoresModifiedKeys()
    {
        var (cut,module,_)=OpenMixed();
        cut.Find("[role=menu]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key="s",CtrlKey=true});
        Assert.Null(FocusedItem(module));
    }
    [Fact] public void TabLeavingTheMenuClosesItWithoutPullingFocusBack()
    {
        var (cut,module,changes)=OpenMixed();var focusBefore=module.Invocations["focus"].Count;
        cut.Find("[role=menu]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key="Tab"});
        Assert.Empty(cut.FindAll("[role=menu]"));
        Assert.Equal([true,false],changes);
        Assert.Equal(focusBefore,module.Invocations["focus"].Count);
    }
    [Theory,Trait("ModuleConformance","hlp.ui.user-menu")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.user-menu", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("user-menu.");Assert.NotNull(Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity("Ada"))));
            var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;
            using var fixture=System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!,fixture.RootElement.GetProperty("input"),fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the fixture's
    // own input and expected values. Escape and outside pointers arrive through the component's
    // [JSInvokable] entry points (user-menu.js observes them in a browser). projection-equivalence
    // compares lanes; rtl-touch asserts the touch marker and direction, the 44px minimum being
    // stylesheet behaviour. An unknown case id fails.
    private static readonly string[] Unasserted=["user-menu.projection-equivalence"];
    private (IRenderedComponent<HarborlineUserMenu> Cut,BunitJSModuleInterop Module,List<bool> Changes) Menu(IReadOnlyList<UserMenuEntry> items,Action<ComponentParameterCollectionBuilder<HarborlineUserMenu>>? more=null,UserIdentity? identity=null,bool open=false)
    {
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/user-menu.js");module.Mode=JSRuntimeMode.Loose;var changes=new List<bool>();
        var cut=Render<HarborlineUserMenu>(p=>{p.Add(x=>x.Identity,identity??new UserIdentity("Ada Lovelace")).Add(x=>x.Items,items).Add(x=>x.OpenChanged,v=>changes.Add(v));more?.Invoke(p);});
        if(open)cut.Find("button.hl-user-menu__trigger").Click();
        return (cut,module,changes);
    }
    private static UserMenuEntry Act(string id,string label,Action? invoked=null,bool closeOnSelect=true,bool disabled=false)=>new(id,UserMenuEntryKind.Action,label,ActivateAsync:()=>{invoked?.Invoke();return Task.CompletedTask;},CloseOnSelect:closeOnSelect,Disabled:disabled);
    // The trigger, panel and sign-out names, read with the surface open, then closed again.
    private static (string? Trigger,string? Panel,string? SignOut) MenuNames<T>(IRenderedComponent<T> cut) where T:IComponent
    {
        cut.Find(".hl-user-menu__trigger").Click();
        var names=(cut.Find(".hl-user-menu__trigger").GetAttribute("aria-label"),cut.Find(".hl-user-menu__surface").GetAttribute("aria-label"),cut.Find(".hl-user-menu__sign-out").TextContent.Trim());
        cut.Find(".hl-user-menu__trigger").Click();
        return names;
    }
    private static (string? Trigger,string? Panel,string? SignOut) Names(System.Text.Json.JsonElement names)=>(names.GetProperty("trigger").GetString(),names.GetProperty("panel").GetString(),names.GetProperty("signOut").GetString());
    private IRenderedComponent<HarborlineLocaleProvider> InHostCatalog(System.Text.Json.JsonElement host,string name,UserMenuLabels? labels)=>
        Render<HarborlineLocaleProvider>(p=>p.Add(x=>x.Locale,host.GetProperty("locale").GetString()).Add(x=>x.Catalog,host.GetProperty("entries").EnumerateObject().ToDictionary(entry=>entry.Name,entry=>entry.Value.GetString()!))
            .AddChildContent<HarborlineUserMenu>(c=>{c.Add(x=>x.Identity,new UserIdentity(name)).Add(x=>x.OnSignOut,()=>{});if(labels is not null)c.Add(x=>x.Labels,labels);}));
    // A record holding only the fixture's labels: a trigger template ({name}), panel and sign-out text.
    internal static UserMenuLabels PartialLabels(System.Text.Json.JsonElement given)
    {
        string? Given(string field)=>given.TryGetProperty(field,out var value)?value.GetString():null;
        var trigger=Given("trigger");
        return new UserMenuLabels{Trigger=trigger is null?null:name=>trigger.Replace("{name}",name,StringComparison.Ordinal),Panel=Given("panel"),SignOut=Given("signOut")};
    }
    private static bool Open(IRenderedComponent<HarborlineUserMenu> cut)=>cut.FindAll(".hl-user-menu__surface").Count>0;
    private static void Key(IRenderedComponent<HarborlineUserMenu> cut,string key)=>cut.Find(".hl-user-menu__surface").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs{Key=key});
    private void AssertCase(string id,System.Text.Json.JsonElement input,System.Text.Json.JsonElement expected)
    {
        static string[] Strings(System.Text.Json.JsonElement element)=>element.EnumerateArray().Select(value=>value.GetString()!).ToArray();
        switch(id)
        {
            case "user-menu.identity":
            {
                var (cut,_,_)=Menu([Act("profile","Profile")],identity:new UserIdentity(input.GetProperty("name").GetString()!,input.GetProperty("email").GetString(),input.GetProperty("role").GetString()),open:true);
                var header=cut.Find(".hl-user-menu__identity").TextContent;
                Assert.Equal(expected.GetProperty("allIdentityFieldsPreserved").GetBoolean(),new[]{"name","email","role"}.All(field=>header.Contains(input.GetProperty(field).GetString()!,StringComparison.Ordinal)));
                Assert.Contains(expected.GetProperty("triggerNameIncludes").GetString()!,cut.Find("button.hl-user-menu__trigger").GetAttribute("aria-label"));
                break;
            }
            case "user-menu.initials":
                Assert.Equal(Strings(expected.GetProperty("initials")),Strings(input.GetProperty("names")).Select(name=>Menu([],identity:new UserIdentity(name)).Cut.Find(".hl-user-menu__avatar").TextContent.Trim()).ToArray());
                break;
            case "user-menu.avatar":
            {
                var (cut,_,_)=Menu([],identity:new UserIdentity("Ada Lovelace",AvatarUri:input.GetProperty("avatarUri").GetString()));
                var name=cut.Find("button.hl-user-menu__trigger").GetAttribute("aria-label");
                Assert.Equal(expected.GetProperty("initiallyImage").GetBoolean(),cut.FindAll(".hl-user-menu__avatar img").Count==1);
                cut.Find(".hl-user-menu__avatar img").TriggerEvent("onerror",new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());
                Assert.Equal(expected.GetProperty("fallbackInitials").GetBoolean(),cut.FindAll(".hl-user-menu__avatar img").Count==0&&cut.Find(".hl-user-menu__avatar").TextContent.Trim()=="AL");
                Assert.Equal(expected.GetProperty("triggerNameStable").GetBoolean(),cut.Find("button.hl-user-menu__trigger").GetAttribute("aria-label")==name);
                break;
            }
            case "user-menu.closed":
            {
                var (cut,_,_)=Menu([Act("profile","Profile")],p=>p.Add(x=>x.Open,input.GetProperty("open").GetBoolean()));
                Assert.Equal(expected.GetProperty("triggerExpanded").GetBoolean(),cut.Find("button.hl-user-menu__trigger").GetAttribute("aria-expanded")=="true");
                Assert.Equal(expected.GetProperty("surfaceMounted").GetBoolean(),Open(cut));
                break;
            }
            case "user-menu.toggle":
            {
                var (cut,_,_)=Menu([Act("profile","Profile")]);var states=new List<bool>();
                for(var i=0;i<input.GetProperty("activations").GetInt32();i++){cut.Find("button.hl-user-menu__trigger").Click();states.Add(cut.Find("button.hl-user-menu__trigger").GetAttribute("aria-expanded")=="true");Assert.Equal(expected.GetProperty("singleSurface").GetBoolean(),cut.FindAll(".hl-user-menu__surface").Count<=1);}
                Assert.Equal(expected.GetProperty("openStates").EnumerateArray().Select(value=>value.GetBoolean()).ToArray(),states.ToArray());
                break;
            }
            case "user-menu.simple-pattern":
            {
                var items=Strings(input.GetProperty("kinds")).Select((kind,index)=>kind switch{"separator"=>new UserMenuEntry($"s{index}",UserMenuEntryKind.Separator),"link"=>new UserMenuEntry($"l{index}",UserMenuEntryKind.Link,$"Link {index}",Destination:"/x"),_=>Act($"a{index}",$"Action {index}")}).ToArray();
                var (cut,module,_)=Menu(items,open:true);
                Assert.Equal(expected.GetProperty("surfaceRole").GetString(),cut.Find(".hl-user-menu__surface").GetAttribute("role"));
                Key(cut,"ArrowDown");
                Assert.Equal(expected.GetProperty("completeKeyboardModel").GetBoolean(),module.Invocations["focusItem"].Count==1&&(string?)module.Invocations["focusItem"][0].Arguments[1]=="l1");
                break;
            }
            case "user-menu.custom-pattern":
            {
                var (cut,module,_)=Menu([new("theme",UserMenuEntryKind.Custom,Content:builder=>builder.AddMarkupContent(0,"<select aria-label=\"Theme\"><option>Light</option></select>"))],open:true);
                Assert.Equal(expected.GetProperty("surfaceRole").GetString(),cut.Find(".hl-user-menu__surface").GetAttribute("role"));
                Assert.Equal(expected.GetProperty("embeddedNativeSemantics").GetBoolean(),cut.FindAll("select[aria-label=Theme]").Count==1&&cut.FindAll("[role=menuitem]").Count==0);
                break;
            }
            case "user-menu.ordered-entries":
            {
                var ids=Strings(input.GetProperty("ids"));
                var (cut,_,_)=Menu(ids.Select(entry=>entry.StartsWith("separator",StringComparison.Ordinal)?new UserMenuEntry(entry,UserMenuEntryKind.Separator):Act(entry,entry)).ToArray(),open:true);
                Assert.Equal(Strings(expected.GetProperty("renderOrder")),cut.FindAll(".hl-user-menu__items > *").Select(node=>node.TagName=="HR"?ids.First(entry=>entry.StartsWith("separator",StringComparison.Ordinal)):node.GetAttribute("data-hl-item")!).ToArray());
                break;
            }
            case "user-menu.selection":
            case "user-menu.keep-open":
            {
                var invoked=new List<string>();var activate=input.GetProperty("activate").GetString()!;
                var keep=input.TryGetProperty("closeOnSelect",out var close)&&!close.GetBoolean();
                var (cut,_,_)=Menu([Act(activate,"Entry",()=>invoked.Add(activate),closeOnSelect:!keep)],open:true);
                cut.Find($"[data-hl-item={activate}]").Click();
                Assert.Equal(Strings(expected.GetProperty("invocations")),invoked.ToArray());
                if(expected.TryGetProperty("closed",out var closed))Assert.Equal(closed.GetBoolean(),!Open(cut));
                if(expected.TryGetProperty("open",out var stays))Assert.Equal(stays.GetBoolean(),Open(cut));
                break;
            }
            case "user-menu.custom-keep-open":
            {
                var (cut,_,_)=Menu([new(input.GetProperty("customControl").GetString()!,UserMenuEntryKind.Custom,Content:builder=>builder.AddMarkupContent(0,"<button type=\"button\">Theme</button>"),CloseOnSelect:input.GetProperty("closeOnSelect").GetBoolean())],open:true);
                cut.Find(".hl-user-menu__custom").Click();
                Assert.Equal(expected.GetProperty("nativeControlActivated").GetBoolean(),cut.FindAll(".hl-user-menu__custom button").Count==1);
                Assert.Equal(expected.GetProperty("open").GetBoolean(),Open(cut));
                break;
            }
            case "user-menu.disabled":
            {
                var calls=0;var disabled=input.GetProperty("disabled").GetString()!;
                var (cut,module,_)=Menu([Act("profile","Profile"),Act(disabled,"Billing",()=>calls++,disabled:true),new UserMenuEntry("docs",UserMenuEntryKind.Link,"Docs",Destination:"/docs"),new UserMenuEntry("help",UserMenuEntryKind.Link,"Help",Destination:"/help",Disabled:true)],open:true);
                Assert.Equal(expected.GetProperty("visible").GetBoolean(),cut.FindAll($"[data-hl-item={disabled}]").Count==1);
                Key(cut,"ArrowDown");
                Assert.Equal(expected.GetProperty("navigationSkips").GetBoolean(),(string?)module.Invocations["focusItem"][^1].Arguments[1]=="docs");
                Assert.True(cut.Find($"[data-hl-item={disabled}]").HasAttribute("disabled"));
                cut.Find($"[data-hl-item={disabled}]").Click();
                Assert.Equal(expected.GetProperty("invocations").GetInt32(),calls);
                Assert.Equal(expected.GetProperty("navigations").GetInt32(),cut.FindAll("[data-hl-item=help][href]").Count);
                break;
            }
            case "user-menu.sign-out":
            {
                var calls=0;
                var (cut,_,_)=Menu([Act("profile","Profile")],p=>p.Add(x=>x.OnSignOut,()=>calls++),open:true);
                Assert.Equal(expected.GetProperty("appendedAfterItems").GetBoolean(),cut.FindAll(".hl-user-menu__items > *").Last().ClassList.Contains("hl-user-menu__sign-out"));
                cut.Find(".hl-user-menu__sign-out").Click();
                Assert.Equal(expected.GetProperty("invocations").GetInt32(),calls);
                Assert.Equal(expected.GetProperty("closed").GetBoolean(),!Open(cut));
                break;
            }
            case "user-menu.dismissal-focus":
            {
                var outcomes=new Dictionary<string,(bool Closed,bool TriggerFocused)>();
                foreach(var method in Strings(input.GetProperty("methods")))
                {
                    var (cut,module,_)=Menu([Act("profile","Profile")],open:true);var before=module.Invocations["focus"].Count;
                    if(method=="Escape")cut.InvokeAsync(()=>cut.Instance.OnEscapeKey()).GetAwaiter().GetResult();
                    else if(method=="trigger-toggle")cut.Find("button.hl-user-menu__trigger").Click();
                    else cut.InvokeAsync(()=>cut.Instance.OnOutsidePointer()).GetAwaiter().GetResult();
                    outcomes[method]=(!Open(cut),module.Invocations["focus"].Count>before);
                }
                Assert.Equal(expected.GetProperty("allClose").GetBoolean(),outcomes.Values.All(outcome=>outcome.Closed));
                Assert.Equal(expected.GetProperty("escapeAndToggleRestoreTrigger").GetBoolean(),outcomes["Escape"].TriggerFocused&&outcomes["trigger-toggle"].TriggerFocused);
                Assert.Equal(expected.GetProperty("outsideTargetRetainsFocus").GetBoolean(),!outcomes["outside-pointer"].TriggerFocused);
                break;
            }
            case "user-menu.keyboard-menu":
            {
                var calls=0;
                var (cut,module,_)=Menu([Act("profile","Profile",()=>calls++),new UserMenuEntry("docs",UserMenuEntryKind.Link,"Docs",Destination:"/docs")],p=>p.Add(x=>x.OnSignOut,()=>{}),open:true);
                string? Focused()=>module.Invocations["focusItem"].Count==0?null:module.Invocations["focusItem"][^1].Arguments[1] as string??"<sign-out>";
                Key(cut,"ArrowDown");var roving=Focused()=="docs"&&cut.Find("[data-hl-item=docs]").GetAttribute("tabindex")=="0";
                Key(cut,"End");Key(cut,"ArrowDown");var wraps=Focused()=="profile";Key(cut,"ArrowUp");var wrapsBack=Focused()=="<sign-out>";
                Key(cut,"p");var typeahead=Focused()=="profile";
                cut.Find("[data-hl-item=profile]").Click();
                Assert.Equal(expected.GetProperty("rovingFocus").GetBoolean(),roving);
                Assert.Equal(expected.GetProperty("wraps").GetBoolean(),wraps&&wrapsBack);
                Assert.Equal(expected.GetProperty("typeahead").GetBoolean(),typeahead);
                Assert.Equal(expected.GetProperty("activationExactlyOnce").GetBoolean(),calls==1);
                break;
            }
            case "user-menu.keyboard-dialog":
            {
                var (cut,module,changes)=Menu([new("theme",UserMenuEntryKind.Custom,Content:builder=>builder.AddMarkupContent(0,"<button type=\"button\">First</button><button type=\"button\">Second</button>"))],open:true);
                Key(cut,"ArrowDown");
                Assert.Equal(expected.GetProperty("noInventedMenuNavigation").GetBoolean(),module.Invocations["focusItem"].Count==0);
                Key(cut,"Tab");
                Assert.Equal(expected.GetProperty("nativeTabOrder").GetBoolean(),Open(cut));
                cut.InvokeAsync(()=>cut.Instance.OnEscapeKey()).GetAwaiter().GetResult();
                Assert.Equal(expected.GetProperty("escapeCloses").GetBoolean(),!Open(cut)&&changes.Last()==false);
                break;
            }
            case "user-menu.placement":
            {
                var seen=new HashSet<string>();
                foreach(var placement in Strings(input.GetProperty("placements")))foreach(var alignment in Strings(input.GetProperty("alignments")))
                {
                    var p=Enum.Parse<UserMenuPlacement>(placement,true);var a=alignment switch{"inline-start"=>UserMenuAlignment.InlineStart,"center"=>UserMenuAlignment.Center,_=>UserMenuAlignment.InlineEnd};
                    var root=Menu([],b=>b.Add(x=>x.Placement,p).Add(x=>x.Alignment,a)).Cut.Find(".hl-user-menu");
                    seen.Add($"{root.GetAttribute("data-placement")}|{root.GetAttribute("data-alignment")}");
                }
                Assert.Equal(expected.GetProperty("allDistinct").GetBoolean(),seen.Count==Strings(input.GetProperty("placements")).Length*Strings(input.GetProperty("alignments")).Length);
                var defaults=Menu([]).Cut.Find(".hl-user-menu");
                Assert.Equal(expected.GetProperty("defaults").GetProperty("placement").GetString(),defaults.GetAttribute("data-placement"));
                Assert.Equal(expected.GetProperty("defaults").GetProperty("alignment").GetString(),defaults.GetAttribute("data-alignment"));
                break;
            }
            case "user-menu.rtl-touch":
            {
                var (cut,_,_)=Menu([],p=>p.Add(x=>x.Direction,input.GetProperty("direction").GetString()).Add(x=>x.CanShowRail,input.GetProperty("canShowRail").GetBoolean()));
                Assert.Equal(expected.GetProperty("logicalAlignment").GetBoolean(),cut.Find(".hl-user-menu").GetAttribute("dir")=="rtl");
                // Touch sizing applies exactly when the rail cannot be shown; the pixel minimum is stylesheet behaviour.
                Assert.Equal(!input.GetProperty("canShowRail").GetBoolean(),cut.Find("button.hl-user-menu__trigger").GetAttribute("data-hl-touch")=="true");
                break;
            }
            case "user-menu.localization":
            {
                var template=input.GetProperty("triggerLabel").GetString()!;
                var (cut,_,_)=Menu([Act("profile","Profile")],p=>p.Add(x=>x.Labels,new UserMenuLabels(name=>template.Replace("{name}",name,StringComparison.Ordinal),input.GetProperty("panelLabel").GetString()!,input.GetProperty("signOutLabel").GetString()!)).Add(x=>x.OnSignOut,()=>{}),open:true);
                Assert.Equal(expected.GetProperty("allChromeCallerControlled").GetBoolean(),
                    cut.Find("button.hl-user-menu__trigger").GetAttribute("aria-label")==template.Replace("{name}","Ada Lovelace",StringComparison.Ordinal)
                    &&cut.Find(".hl-user-menu__surface").GetAttribute("aria-label")==input.GetProperty("panelLabel").GetString()
                    &&cut.Find(".hl-user-menu__sign-out").TextContent.Trim()==input.GetProperty("signOutLabel").GetString());
                break;
            }
            case "user-menu.catalog-defaults":
            {
                var name=input.GetProperty("name").GetString()!;
                Assert.Equal(Names(expected.GetProperty("defaultCatalog")),MenuNames(Render<HarborlineUserMenu>(p=>p.Add(x=>x.Identity,new UserIdentity(name)).Add(x=>x.OnSignOut,()=>{}))));
                Assert.Equal(Names(expected.GetProperty("hostCatalog")),MenuNames(InHostCatalog(input.GetProperty("hostCatalog"),name,null)));
                break;
            }
            case "user-menu.caller-text-exact":
            {
                // The record is caller text: the fixture's labels are written onto the English default record.
                var given=input.GetProperty("labels");
                string? Given(string field)=>given.TryGetProperty(field,out var value)?value.GetString():null;
                var labels=UserMenuLabels.English with{Panel=Given("panel")??UserMenuLabels.English.Panel,SignOut=Given("signOut")??UserMenuLabels.English.SignOut};
                var (_,panel,signOut)=MenuNames(InHostCatalog(input.GetProperty("hostCatalog"),input.GetProperty("name").GetString()!,labels));
                Assert.Equal((expected.GetProperty("panel").GetString(),expected.GetProperty("signOut").GetString()),(panel,signOut));
                break;
            }
            case "user-menu.partial-labels":
            {
                // Each fixture label sets that field of a new record; every field it omits stays null and comes
                // from the catalog. Every name is asserted.
                Assert.Equal(Names(expected.GetProperty("partial")),MenuNames(InHostCatalog(input.GetProperty("hostCatalog"),input.GetProperty("name").GetString()!,PartialLabels(input.GetProperty("labels")))));
                break;
            }
            case "user-menu.invalid-input":
            {
                var errors=new List<string>();
                void Capture(Action render){try{render();errors.Add("none");}catch(InvalidOperationException error){errors.Add(error.Message);}}
                Capture(()=>Menu([],identity:new UserIdentity(input.GetProperty("name").GetString()!)));
                Capture(()=>Menu(Strings(input.GetProperty("duplicateIds")).Select(entry=>Act(entry,entry)).ToArray()));
                Capture(()=>Menu([new UserMenuEntry("docs",UserMenuEntryKind.Link,"Docs")]));
                Assert.Equal("link-without-destination",input.GetProperty("invalidEntry").GetString());
                Assert.Equal(Strings(expected.GetProperty("errors")),errors.ToArray());
                break;
            }
            default:
                Assert.Contains(id,Unasserted);
                break;
        }
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
