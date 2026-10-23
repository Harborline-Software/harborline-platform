using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class NotificationCenterTests : BunitContext
{
    private readonly CapturingOutside outside = new();
    public NotificationCenterTests(){JSInterop.Mode=JSRuntimeMode.Loose;Services.AddSingleton<IOutsidePointerObserver>(outside);}
    private static IReadOnlyList<NotificationCenterItem> Items=>[new("p1","Approve","now","proposal"),new("r1","Done","later","result")];
    [Fact] public void BadgeAndDialogSemanticsMatch(){var cut=Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items));Assert.Equal("Notifications, 2 unread",cut.Find("button").GetAttribute("aria-label"));cut.Find("button").Click();Assert.Equal("dialog",cut.Find("[role=dialog]").GetAttribute("role"));Assert.Equal(3,cut.FindAll("[role=tab]").Count);}
    [Fact] public void ProposalDecisionInvokesOnceAndCloses(){var calls=0;var cut=Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items).Add(x=>x.OnConfirm,_=>calls++));cut.Find("button").Click();cut.FindAll(".hl-notification-center__actions button")[0].Click();Assert.Equal(1,calls);Assert.Empty(cut.FindAll("[role=dialog]"));}
    [Fact] public void ItemDismissalNeverMutatesCallerList(){var dismissed=new List<string>();var cut=Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items).Add(x=>x.OnDismiss,item=>dismissed.Add(item.Id)));cut.Find("button").Click();var dismiss=cut.Find("[aria-label='Dismiss: Done']");Assert.NotNull(dismiss.QuerySelector("svg"));Assert.DoesNotContain("×",dismiss.TextContent);dismiss.Click();Assert.Equal(["r1"],dismissed);Assert.Equal(2,Items.Count);}
    [Theory,Trait("ModuleConformance","hlp.ui.notification-center")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.notification-center", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("notification-center.");
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!, fixture.RootElement.GetProperty("input"), fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the fixture's own
    // input and expected values. dismissal-focus asserts the close and the trigger-focus request through the
    // module (bUnit cannot observe focus; the Chromium probe checks it); rtl-touch asserts the logical
    // direction and touch marker, the 44px bell being the gallery's touch check. projection-equivalence
    // compares lanes. An unknown case id fails.
    private static readonly string[] Unasserted = ["notification-center.projection-equivalence"];
    private static NotificationCenterItem Item(string id, string kind, bool read = false) => new(id, $"Title {id}", "now", kind, read);
    private IRenderedComponent<HarborlineNotificationCenter> Center(IReadOnlyList<NotificationCenterItem> items, Action<ComponentParameterCollectionBuilder<HarborlineNotificationCenter>>? configure = null) =>
        Render<HarborlineNotificationCenter>(p => { p.Add(x => x.Items, items); configure?.Invoke(p); });
    private static string? Badge(IRenderedComponent<HarborlineNotificationCenter> cut) => cut.FindAll(".hl-notification-center__badge").FirstOrDefault()?.TextContent;
    private static string Bell(IRenderedComponent<HarborlineNotificationCenter> cut) => cut.Find(".hl-notification-center__trigger").GetAttribute("aria-label")!;
    private static void Open(IRenderedComponent<HarborlineNotificationCenter> cut) => cut.Find(".hl-notification-center__trigger").Click();
    private static bool IsOpen(IRenderedComponent<HarborlineNotificationCenter> cut) => cut.FindAll("[role=dialog]").Count > 0;
    private static string[] Strings(System.Text.Json.JsonElement value) => value.EnumerateArray().Select(v => v.GetString()!).ToArray();

    private void AssertCase(string id, System.Text.Json.JsonElement input, System.Text.Json.JsonElement expected)
    {
        switch (id)
        {
            case "notification-center.empty":
            {
                var cut = Center([]);
                Assert.Equal(expected.GetProperty("bellLabel").GetString(), Bell(cut));
                Assert.Equal(expected.GetProperty("badge").ValueKind == System.Text.Json.JsonValueKind.Null ? null : expected.GetProperty("badge").GetString(), Badge(cut));
                Open(cut);
                Assert.Equal(expected.GetProperty("empty").GetString(), cut.Find(".hl-notification-center__empty").TextContent);
                break;
            }
            case "notification-center.badge-unread":
            {
                var items = input.GetProperty("items").EnumerateArray().Select((entry, index) => Item($"n{index}", entry.GetProperty("kind").GetString()!, entry.GetProperty("read").GetBoolean())).ToArray();
                var cut = Center(items, p => p.Add(x => x.BadgeMode, NotificationBadgeMode.Unread));
                Assert.Equal(expected.GetProperty("numericBadge").GetString(), Badge(cut));
                Assert.Equal(expected.GetProperty("bellLabel").GetString(), Bell(cut));
                break;
            }
            case "notification-center.badge-decisions":
            {
                var items = Enumerable.Range(0, input.GetProperty("proposalUnread").GetInt32()).Select(i => Item($"p{i}", "proposal")).Concat(Enumerable.Range(0, input.GetProperty("otherUnread").GetInt32()).Select(i => Item($"o{i}", "result"))).ToArray();
                var cut = Center(items, p => p.Add(x => x.BadgeMode, NotificationBadgeMode.Decisions));
                Assert.Equal(expected.GetProperty("numericBadge").GetString(), Badge(cut));
                Assert.Equal(expected.GetProperty("bellLabel").GetString(), Bell(cut));
                break;
            }
            case "notification-center.overflow-format":
            {
                var prefix = input.GetProperty("formatPrefix").GetString();
                var cut = Center(Enumerable.Range(0, input.GetProperty("unread").GetInt32()).Select(i => Item($"n{i}", "result")).ToArray(), p => p.Add(x => x.FormatCount, count => $"{prefix}{count}"));
                Assert.Equal(expected.GetProperty("badge").GetString(), Badge(cut));
                Assert.Equal(expected.GetProperty("bounded").GetBoolean(), Badge(cut)!.Length <= $"{prefix}99+".Length);
                break;
            }
            case "notification-center.open-close":
            {
                var cut = Center([Item("r1", "result")]);
                var states = Strings(input.GetProperty("commands")).Select(_ => { Open(cut); return IsOpen(cut); }).ToArray();
                Assert.Equal(expected.GetProperty("states").EnumerateArray().Select(v => v.GetBoolean()).ToArray(), states);
                Open(cut);
                Assert.Equal(expected.GetProperty("role").GetString(), cut.Find("[role=dialog]").GetAttribute("role"));
                break;
            }
            case "notification-center.dismissal-focus":
            {
                var outcomes = new List<(bool Closed, bool TriggerFocus)>();
                foreach (var method in Strings(input.GetProperty("methods")))
                {
                    var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/notification-center.js"); module.Mode = JSRuntimeMode.Loose;
                    var cut = Center([Item("p1", "proposal"), Item("r1", "result")], p => p.Add(x => x.OnConfirm, _ => { }).Add(x => x.OnViewAll, () => { }));
                    Open(cut);
                    var focusCalls = module.Invocations["focus"].Count;
                    if (method == "Escape") cut.Find("[role=dialog]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
                    else if (method == "outside-pointer") cut.InvokeAsync(() => outside.Callback!(default!).AsTask()).Wait();
                    else if (method == "proposal") cut.Find("[aria-label='Confirm: Title p1']").Click();
                    else cut.Find("footer button").Click();
                    var triggerFocusRequested = module.Invocations["focus"].Count > focusCalls;
                    outcomes.Add((!IsOpen(cut), method == "outside-pointer" ? !triggerFocusRequested : triggerFocusRequested));
                }
                Assert.Equal(expected.GetProperty("closed").GetBoolean(), outcomes.All(o => o.Closed));
                Assert.Equal(expected.GetProperty("triggerFocusRestoredExceptOutsideTarget").GetBoolean(), outcomes.All(o => o.TriggerFocus));
                break;
            }
            case "notification-center.groups":
            {
                var kinds = Strings(input.GetProperty("kinds"));
                var cut = Center(kinds.Select((kind, i) => Item($"n{i}", kind)).ToArray());
                Open(cut);
                var tabs = cut.FindAll("[role=tab]");
                Assert.Equal(expected.GetProperty("tabs").GetInt32(), tabs.Count);
                var labels = tabs.Skip(1).Select(tab => tab.TextContent.ToLowerInvariant()).ToArray();
                Assert.Equal(Strings(expected.GetProperty("groupKinds")), labels);
                Assert.Equal(expected.GetProperty("firstAppearanceOrder").GetBoolean(), labels.SequenceEqual(kinds.Distinct()));
                break;
            }
            case "notification-center.filter":
            {
                var items = Strings(input.GetProperty("items")).Select(entry => entry.Split(':')).Select(parts => Item(parts[1], parts[0])).ToArray();
                var active = input.GetProperty("activeKind").GetString()!;
                var cut = Center(items);
                Open(cut);
                cut.FindAll("[role=tab]").Single(tab => tab.TextContent.Equals(active, StringComparison.OrdinalIgnoreCase)).Click();
                var visible = cut.FindAll(".hl-notification-center__item strong").Select(node => node.TextContent.Replace("Title ", "", StringComparison.Ordinal)).ToArray();
                Assert.Equal(Strings(expected.GetProperty("visibleIds")), visible);
                Assert.Equal(expected.GetProperty("callerOrderPreserved").GetBoolean(), visible.SequenceEqual(items.Where(i => i.Kind == active).Select(i => i.Id)));
                break;
            }
            case "notification-center.proposal":
            {
                // The host's handler is still running when the second activation arrives, so only the
                // per-open latch can stop it.
                var confirms = new List<NotificationCenterItem>(); var denies = 0; var item = Item(input.GetProperty("id").GetString()!, "proposal");
                TaskCompletionSource gate = new();
                var cut = Center([item], p => p.Add(x => x.OnConfirm, async (NotificationCenterItem value) => { confirms.Add(value); await gate.Task; }).Add(x => x.OnDeny, async (NotificationCenterItem _) => { denies++; await gate.Task; }));
                foreach (var action in Strings(input.GetProperty("actions")))
                {
                    gate = new();
                    Open(cut);
                    var selector = $"[aria-label='{(action == "confirm" ? "Confirm" : "Deny")}: {item.Title}']";
                    var button = cut.Find(selector);
                    button.Click();
                    if (!button.HasAttribute("disabled")) button.Click();
                    cut.InvokeAsync(() => gate.SetResult()).Wait();
                    cut.WaitForAssertion(() => Assert.Equal(!expected.GetProperty("closes").GetBoolean(), IsOpen(cut)));
                }
                Assert.Equal(expected.GetProperty("confirmCountPerOpen").GetInt32(), confirms.Count);
                Assert.Equal(expected.GetProperty("denyCountPerOpen").GetInt32(), denies);
                Assert.Equal(expected.GetProperty("itemPreserved").GetBoolean(), ReferenceEquals(confirms[0], item));
                break;
            }
            case "notification-center.informational":
            {
                var actions = new List<string>(); var itemId = input.GetProperty("id").GetString()!;
                var cut = Center([Item(itemId, "result")], p => p.Add(x => x.OnAction, item => actions.Add(item.Id)));
                Open(cut);
                cut.FindAll(".hl-notification-center__actions button").Single(button => button.TextContent == $"Title {itemId}").Click();
                Assert.Equal(Strings(expected.GetProperty("actionIds")), actions.ToArray());
                Assert.Equal(expected.GetProperty("confirmPresent").GetBoolean(), cut.FindAll("[aria-label^='Confirm']").Count > 0);
                Assert.Equal(expected.GetProperty("denyPresent").GetBoolean(), cut.FindAll("[aria-label^='Deny']").Count > 0);
                Assert.Equal(expected.GetProperty("remainsOpen").GetBoolean(), IsOpen(cut));
                break;
            }
            case "notification-center.mark-read":
            {
                var calls = 0;
                var cut = Center(Enumerable.Range(0, input.GetProperty("unread").GetInt32()).Select(i => Item($"n{i}", "result")).ToArray(), p => { if (input.GetProperty("hasCallback").GetBoolean()) p.Add(x => x.OnMarkAllRead, () => calls++); });
                Open(cut);
                var buttons = cut.FindAll(".hl-notification-center__header button");
                Assert.Equal(expected.GetProperty("present").GetBoolean(), buttons.Count > 0);
                if (input.GetProperty("activate").GetBoolean()) buttons[0].Click();
                Assert.Equal(expected.GetProperty("callbackCount").GetInt32(), calls);
                break;
            }
            case "notification-center.dismiss-item":
            {
                var dismissed = 0; IReadOnlyList<NotificationCenterItem> items = [Item("n1", input.GetProperty("kind").GetString()!)];
                var cut = Center(items, p => { if (input.GetProperty("hasCallback").GetBoolean()) p.Add(x => x.OnDismiss, _ => dismissed++); });
                Open(cut);
                if (input.GetProperty("activate").GetBoolean()) cut.Find("[aria-label='Dismiss: Title n1']").Click();
                Assert.Equal(expected.GetProperty("callbackCount").GetInt32(), dismissed);
                Assert.Equal(expected.GetProperty("itemMutated").GetBoolean(), items.Count != 1 || cut.FindAll("[aria-label='Dismiss: Title n1']").Count == 0);
                break;
            }
            case "notification-center.limit-view-all":
            {
                var calls = 0;
                var cut = Center(Enumerable.Range(0, input.GetProperty("items").GetInt32()).Select(i => Item($"n{i}", "result")).ToArray(), p => { p.Add(x => x.MaxVisible, input.GetProperty("maxVisible").GetInt32()); if (input.GetProperty("hasCallback").GetBoolean()) p.Add(x => x.OnViewAll, () => calls++); });
                Open(cut);
                Assert.Equal(expected.GetProperty("visible").GetInt32(), cut.FindAll(".hl-notification-center__item").Count);
                var footer = cut.FindAll("footer button");
                Assert.Equal(expected.GetProperty("viewAll").GetBoolean(), footer.Count > 0);
                footer[0].Click();
                Assert.Equal(expected.GetProperty("callbackCount").GetInt32(), calls);
                Assert.Equal(expected.GetProperty("closes").GetBoolean(), !IsOpen(cut));
                break;
            }
            case "notification-center.localization":
            {
                var given = input.GetProperty("labels");
                string Label(string name) => given.GetProperty(name).GetString()!;
                var labels = NotificationCenterLabels.English with { Title = Label("title"), Confirm = Label("confirm"), Deny = Label("deny"), MarkAllRead = Label("markAllRead"), ViewAll = Label("viewAll"), Empty = Label("empty") };
                var cut = Center([Item("p1", "proposal")], p => p.Add(x => x.Labels, labels).Add(x => x.OnMarkAllRead, () => { }).Add(x => x.OnViewAll, () => { }));
                var bellOverridden = Bell(cut).StartsWith(labels.Title, StringComparison.Ordinal);
                Open(cut);
                var chrome = new[]
                {
                    cut.Find("[role=dialog]").GetAttribute("aria-label") == labels.Title,
                    cut.Find("h2").TextContent == labels.Title,
                    cut.Find($"[aria-label='{labels.Confirm}: Title p1']").TextContent == labels.Confirm,
                    cut.Find($"[aria-label='{labels.Deny}: Title p1']").TextContent == labels.Deny,
                    cut.Find(".hl-notification-center__header button").TextContent == labels.MarkAllRead,
                    cut.Find("footer button").TextContent == labels.ViewAll,
                };
                var emptyCut = Center([], p => p.Add(x => x.Labels, labels));
                Open(emptyCut);
                var empty = emptyCut.Find(".hl-notification-center__empty").TextContent == labels.Empty;
                Assert.Equal(expected.GetProperty("allChromeOverridden").GetBoolean(), bellOverridden && chrome.All(ok => ok) && empty);
                break;
            }
            case "notification-center.rtl-touch":
            {
                var direction = input.GetProperty("direction").GetString();
                var cut = Center([Item("r1", "result")], p => p.Add(x => x.Direction, direction).Add(x => x.CanShowRail, input.GetProperty("canShowRail").GetBoolean()));
                Open(cut);
                Assert.Equal(expected.GetProperty("logicalEdges").GetBoolean(), cut.Find(".hl-notification-center").GetAttribute("dir") == direction);
                Assert.Equal("true", cut.Find(".hl-notification-center__trigger").GetAttribute("data-hl-touch"));
                Assert.Equal(44, expected.GetProperty("bellMinimumCssPixels").GetInt32());
                break;
            }
            case "notification-center.invalid-input":
            {
                var errors = new List<string>();
                try { Center(Strings(input.GetProperty("ids")).Select(itemId => Item(itemId, "result")).ToArray()); } catch (InvalidOperationException error) { errors.Add(error.Message); }
                try { Center([], p => p.Add(x => x.MaxVisible, input.GetProperty("maxVisible").GetInt32())); } catch (InvalidOperationException error) { errors.Add(error.Message); }
                Assert.Equal(Strings(expected.GetProperty("errors")), errors.ToArray());
                break;
            }
            default:
                Assert.Contains(id, Unasserted);
                break;
        }
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
    private sealed class FakeOutside:IOutsidePointerObserver{public ValueTask<IOutsidePointerRegistration> ObserveAsync(Microsoft.AspNetCore.Components.ElementReference e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>throw new NotSupportedException();public ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<Microsoft.AspNetCore.Components.ElementReference> e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>new(new Registration());private sealed class Registration:IOutsidePointerRegistration{public ValueTask DisposeAsync()=>ValueTask.CompletedTask;public ValueTask SetEnabledAsync(bool enabled)=>ValueTask.CompletedTask;public void SetCallback(Func<OutsidePointerEvent,ValueTask> c){}}}
}
