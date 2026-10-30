using System.Text.Json;
using Bunit;
using Harborline.Gallery.Blazor.Stories;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppShellRev6NativeScenarioTests : BunitContext
{
    private const string TabId = "[\"Details\",\"record-b\"]";

    [Fact]
    public void Retained_fragment_cannot_read_or_edit_another_owner_or_a_later_same_owner_epoch()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/app-shell-rev6-focus.js");
        var module = JSInterop.SetupModule("./rev6/tab-identity-state.mjs");
        module.Setup<JsonElement>("runScenario", _ => true).SetResult(State("alice"));
        foreach (var person in new[] { "alice", "bob" })
        {
            module.Setup<JsonElement>("presentation", call =>
                ((JsonElement)call.Arguments[0]!).GetProperty("owner").GetProperty("person").GetString() == person)
                .SetResult(Presentation());
            module.Setup<JsonElement>("transition", call =>
                JsonSerializer.Serialize(call.Arguments[1]).Contains("\"person\":\"" + person + "\"", StringComparison.Ordinal))
                .SetResult(State(person));
        }
        module.Setup<JsonElement>("transition", call =>
            JsonSerializer.Serialize(call.Arguments[1]).Contains("\"type\":\"edit\"", StringComparison.Ordinal))
            .SetResult(State("alice"));

        var cut = Render<AppShellRev6NativeScenario>();
        cut.WaitForAssertion(() => Assert.Contains("Alice private", cut.Find("textarea").OuterHtml, StringComparison.Ordinal));
        var retained = cut.FindComponent<HarborlineAppShellRev6TabHost>().Instance.TabContent!;
        var tab = new AppShellRev6Tab(TabId, "Details", "record-b", false, false, true);

        // Rev2's caller-owned guard must cover both replacement owners and Alice→Bob→Alice.
        // The literal fixture values and same-address identity are independent of the caller.
        foreach (var expectedCurrentDraft in new[] { "Bob private", "Alice private" })
        {
            cut.FindAll("button").Single(button => button.TextContent == "Switch person").Click();
            cut.WaitForAssertion(() => Assert.Contains(expectedCurrentDraft, cut.Find("textarea").OuterHtml, StringComparison.Ordinal));
            var stale = Render<RetainedFragmentHost>(p => p.Add(c => c.Content, retained(tab)));
            var staleMarkup = stale.Markup;
            var before = JSInterop.Invocations["transition"].Count;
            stale.Find("textarea").Change("Alice stale edit");
            Assert.Equal(before, JSInterop.Invocations["transition"].Count);
            Assert.DoesNotContain("Alice private", staleMarkup, StringComparison.Ordinal);
            Assert.DoesNotContain("Bob private", staleMarkup, StringComparison.Ordinal);
            Assert.Contains(expectedCurrentDraft, cut.Find("textarea").OuterHtml, StringComparison.Ordinal);
        }

        // Positive control: the current fragment still sends its edit to the real caller seam.
        var currentCount = JSInterop.Invocations["transition"].Count;
        cut.Find("textarea").Change("Alice current edit");
        Assert.Equal(currentCount + 1, JSInterop.Invocations["transition"].Count);
        Assert.Contains("Alice current edit", JsonSerializer.Serialize(JSInterop.Invocations["transition"].Last().Arguments[1]), StringComparison.Ordinal);
    }

    private static JsonElement State(string person) => JsonSerializer.SerializeToElement(new
    {
        owner = new { person, tenant = "north", workspace = "work" },
        owners = new Dictionary<string, object>
        {
            ["[\"alice\",\"north\",\"work\"]"] = new { drafts = new Dictionary<string, string> { [TabId] = "Alice private" } },
            ["[\"bob\",\"north\",\"work\"]"] = new { drafts = new Dictionary<string, string> { [TabId] = "Bob private" } },
        },
    });

    private static JsonElement Presentation() => JsonSerializer.SerializeToElement(new
    {
        destination = "records",
        tabs = new[] { new { id = TabId, kind = "Details", target = "record-b", preview = false, pinned = false, dirty = true } },
        active = TabId,
        pendingClose = (string?)null,
        saveRequest = (object?)null,
    });

    public sealed class RetainedFragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Content);
    }
}
