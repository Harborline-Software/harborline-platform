using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class PopoverTests : BunitContext
{
    [Fact]
    public void TriggerExposesStateAndTogglesUncontrolledContent()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var states = new List<bool>();
        RenderFragment content = builder =>
        {
            builder.OpenComponent<HarborlinePopoverTrigger>(0); builder.AddAttribute(1, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Open"))); builder.CloseComponent();
            builder.OpenComponent<HarborlinePopoverContent>(2); builder.AddAttribute(3, "AccessibleLabel", "Details"); builder.AddAttribute(4, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Body"))); builder.CloseComponent();
        };
        var cut = Render<HarborlinePopover>(p => p.Add(x => x.ChildContent, content).Add(x => x.OpenChanged, value => states.Add(value)));
        var trigger = cut.Find("button");
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        trigger.Click();
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-expanded"));
        Assert.Equal("dialog", cut.Find("[role=dialog]").GetAttribute("role"));
        Assert.Equal([true], states);
    }

    [Fact]
    public void ControlledRootEmitsWithoutChangingRenderedState()
    {
        var states = new List<bool>();
        RenderFragment content = builder => { builder.OpenComponent<HarborlinePopoverTrigger>(0); builder.CloseComponent(); };
        var cut = Render<HarborlinePopover>(p => p.Add(x => x.Open, false).Add(x => x.ChildContent, content).Add(x => x.OpenChanged, value => states.Add(value)));
        cut.Find("button").Click();
        Assert.Equal("false", cut.Find("button").GetAttribute("aria-expanded"));
        Assert.Equal([true], states);
    }

    [Theory, Trait("ModuleConformance", "hlp.ui.popover")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.popover", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw); var id = fixture.RootElement.GetProperty("id").GetString(); Assert.StartsWith("popover.", id);
            Assert.Equal("false", Render<HarborlinePopover>(p => p.Add(x => x.ChildContent, (RenderFragment)(b => { b.OpenComponent<HarborlinePopoverTrigger>(0); b.CloseComponent(); }))).Find("button").GetAttribute("aria-expanded"));
            AssertCase(id!, fixture.RootElement.GetProperty("input"), fixture.RootElement.GetProperty("expected"));
            if (id != "popover.class-vocabulary") return;

            // The class list of the root, the anchor and the content, from the same fixture row the
            // React lane reads. This lane used to emit hl-popover-root, hl-popover-anchor and an
            // hl-popover-content alias styled only by its own wwwroot copy; the authority defined none
            // of them, so the two lanes styled the same surface differently and parity could not see it.
            JSInterop.Mode = JSRuntimeMode.Loose;
            var expected = fixture.RootElement.GetProperty("expected");
            RenderFragment body = builder =>
            {
                builder.OpenComponent<HarborlinePopoverAnchor>(0); builder.AddAttribute(1, "ChildContent", (RenderFragment)(b => b.AddContent(0, "anchor"))); builder.CloseComponent();
                builder.OpenComponent<HarborlinePopoverTrigger>(2); builder.AddAttribute(3, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Open"))); builder.CloseComponent();
                builder.OpenComponent<HarborlinePopoverContent>(4); builder.AddAttribute(5, "AccessibleLabel", "Details"); builder.AddAttribute(6, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Body"))); builder.CloseComponent();
            };
            var open = Render<HarborlinePopover>(p => p.Add(x => x.Open, true).Add(x => x.ChildContent, body));
            var root = open.Find("button").ParentElement!;
            Assert.Equal(Classes(expected, "rootClasses"), root.ClassList);
            Assert.Equal(Classes(expected, "anchorClasses"), root.Children[0].ClassList);
            Assert.Equal(Classes(expected, "contentClasses"), open.Find("[role=dialog]").ClassList);
        });
    }

    // Targeted neutral-case semantics: each case runs the production components against the fixture's
    // own input and expected values. Escape and outside pointers reach the component through
    // DismissFromJavaScriptAsync (popover.js observes them in a browser); focus entry and restoration
    // live in popover.js connect/dispose, so the case asserts that wiring. placement is computed by
    // popover.js place(); projection-equivalence compares lanes. An unknown case id fails.
    private static readonly string[] Unasserted = ["popover.placement", "popover.projection-equivalence", "popover.class-vocabulary"];
    private (IRenderedComponent<HarborlinePopover> Cut, BunitJSModuleInterop Module, List<bool> Emitted) Build(bool? open = null, bool anchor = false, bool close = false, Action<RenderTreeBuilder>? contentAttributes = null)
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/popover.js"); module.Mode = JSRuntimeMode.Loose;
        var emitted = new List<bool>();
        RenderFragment body = builder =>
        {
            var seq = 0;
            if (anchor) { builder.OpenComponent<HarborlinePopoverAnchor>(seq++); builder.AddAttribute(seq++, "ChildContent", (RenderFragment)(b => b.AddContent(0, "anchor"))); builder.CloseComponent(); }
            builder.OpenComponent<HarborlinePopoverTrigger>(seq++); builder.AddAttribute(seq++, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Open"))); builder.CloseComponent();
            builder.OpenComponent<HarborlinePopoverContent>(seq++);
            builder.AddAttribute(seq++, "AccessibleLabel", "Details");
            contentAttributes?.Invoke(builder);
            builder.AddAttribute(seq++, "ChildContent", (RenderFragment)(b => { b.AddContent(0, "Body"); if (close) { b.OpenComponent<HarborlinePopoverClose>(1); b.AddAttribute(2, "ChildContent", (RenderFragment)(c => c.AddContent(0, "Close"))); b.CloseComponent(); } }));
            builder.CloseComponent();
        };
        var cut = Render<HarborlinePopover>(p => { p.Add(x => x.ChildContent, body).Add(x => x.OpenChanged, value => emitted.Add(value)); if (open is bool value) p.Add(x => x.DefaultOpen, value); });
        return (cut, module, emitted);
    }
    private static bool IsOpen(IRenderedComponent<HarborlinePopover> cut) => cut.FindAll(".hl-popover__content").Count > 0;
    private static IRenderedComponent<HarborlinePopoverContent> Content(IRenderedComponent<HarborlinePopover> cut) => cut.FindComponent<HarborlinePopoverContent>();
    private void AssertCase(string id, System.Text.Json.JsonElement input, System.Text.Json.JsonElement expected)
    {
        switch (id)
        {
            case "popover.closed":
            {
                var (cut, _, _) = Build();
                Assert.Equal(expected.GetProperty("contentMounted").GetBoolean(), IsOpen(cut));
                Assert.Equal(expected.GetProperty("ariaExpanded").GetString(), cut.Find("button").GetAttribute("aria-expanded"));
                break;
            }
            case "popover.uncontrolled":
            {
                var (cut, _, emitted) = Build(); var states = new List<bool> { IsOpen(cut) };
                for (var i = 0; i < input.GetProperty("activations").GetInt32(); i++) { cut.Find("button").Click(); states.Add(IsOpen(cut)); }
                Assert.Equal(expected.GetProperty("states").EnumerateArray().Select(v => v.GetBoolean()).ToArray(), states.ToArray());
                Assert.Equal(expected.GetProperty("emitted").EnumerateArray().Select(v => v.GetBoolean()).ToArray(), emitted.ToArray());
                break;
            }
            case "popover.controlled":
            {
                var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/popover.js"); module.Mode = JSRuntimeMode.Loose; var emitted = new List<bool>();
                // The content carries child content, as a real caller's does, so it re-renders with the root.
                RenderFragment body = b => { b.OpenComponent<HarborlinePopoverTrigger>(0); b.CloseComponent(); b.OpenComponent<HarborlinePopoverContent>(1); b.AddAttribute(2, "AccessibleLabel", "Details"); b.AddAttribute(3, "ChildContent", (RenderFragment)(c => c.AddContent(0, "Body"))); b.CloseComponent(); };
                var cut = Render<HarborlinePopover>(p => p.Add(x => x.Open, input.GetProperty("open").GetBoolean()).Add(x => x.ChildContent, body).Add(x => x.OpenChanged, v => emitted.Add(v)));
                if (input.GetProperty("activate").GetBoolean()) cut.Find("button").Click();
                Assert.Equal(expected.GetProperty("rendered").GetBoolean(), IsOpen(cut));
                Assert.Equal(expected.GetProperty("emitted").GetBoolean(), emitted.SequenceEqual([true]));
                break;
            }
            case "popover.trigger-aria":
            {
                var (cut, _, _) = Build(open: input.GetProperty("open").GetBoolean());
                var trigger = cut.Find("button");
                Assert.Equal(expected.GetProperty("hasPopup").GetString(), trigger.GetAttribute("aria-haspopup"));
                Assert.Equal(expected.GetProperty("expanded").GetString(), trigger.GetAttribute("aria-expanded"));
                Assert.Equal(expected.GetProperty("controlsMountedContent").GetBoolean(), trigger.GetAttribute("aria-controls") == cut.Find(".hl-popover__content").Id);
                break;
            }
            case "popover.anchor":
            {
                var (cut, module, _) = Build(open: true, anchor: true, contentAttributes: b => { b.AddAttribute(90, "Side", Enum.Parse<PopoverSide>(input.GetProperty("side").GetString()!, true)); b.AddAttribute(91, "Align", Enum.Parse<PopoverAlign>(input.GetProperty("align").GetString()!, true)); b.AddAttribute(92, "SideOffset", input.GetProperty("sideOffset").GetDouble()); });
                var connect = Assert.Single(module.Invocations["connect"]);
                var trigger = (ElementReference)connect.Arguments[0]!; var reference = (ElementReference)connect.Arguments[1]!;
                Assert.Equal(expected.GetProperty("positionedFromAnchor").GetBoolean(), !string.IsNullOrEmpty(reference.Id) && reference.Id != trigger.Id);
                break;
            }
            case "popover.content-options":
            {
                var (cut, module, _) = Build(open: true, contentAttributes: b => { b.AddAttribute(90, "Role", input.GetProperty("role").GetString()); b.AddAttribute(91, "AccessibleLabel", input.GetProperty("ariaLabel").GetString()); b.AddAttribute(92, "class", input.GetProperty("class").GetString()); b.AddAttribute(93, "Side", Enum.Parse<PopoverSide>(input.GetProperty("side").GetString()!, true)); b.AddAttribute(94, "Align", Enum.Parse<PopoverAlign>(input.GetProperty("align").GetString()!, true)); b.AddAttribute(95, "SideOffset", input.GetProperty("sideOffset").GetDouble()); });
                var content = cut.Find(".hl-popover__content");
                var options = System.Text.Json.JsonSerializer.SerializeToElement(Assert.Single(module.Invocations["connect"]).Arguments[3]);
                Assert.Equal(expected.GetProperty("preserved").GetBoolean(),
                    content.GetAttribute("role") == input.GetProperty("role").GetString() && content.GetAttribute("aria-label") == input.GetProperty("ariaLabel").GetString()
                    && content.ClassList.Contains(input.GetProperty("class").GetString()!) && content.GetAttribute("data-side") == input.GetProperty("side").GetString() && content.GetAttribute("data-align") == input.GetProperty("align").GetString()
                    && options.GetProperty("sideOffset").GetDouble() == input.GetProperty("sideOffset").GetDouble());
                break;
            }
            case "popover.escape":
            case "popover.outside":
            {
                var (cut, _, emitted) = Build(open: input.GetProperty("open").GetBoolean());
                cut.InvokeAsync(() => Content(cut).Instance.DismissFromJavaScriptAsync()).GetAwaiter().GetResult();
                Assert.Equal(expected.GetProperty("open").GetBoolean(), IsOpen(cut));
                Assert.Equal(expected.GetProperty("callbacks").GetInt32(), emitted.Count);
                break;
            }
            case "popover.close-control":
            {
                var (cut, _, emitted) = Build(open: input.GetProperty("open").GetBoolean(), close: true);
                cut.FindAll("button").Single(b => b.TextContent == "Close").Click();
                Assert.Equal(expected.GetProperty("open").GetBoolean(), IsOpen(cut));
                Assert.Equal(expected.GetProperty("callbacks").GetInt32(), emitted.Count);
                break;
            }
            case "popover.focus":
            {
                var (cut, module, _) = Build(open: input.GetProperty("open").GetBoolean());
                // popover.js connect() moves focus into the content and dispose() restores it to the trigger.
                Assert.Equal(expected.GetProperty("focusEnteredContent").GetBoolean(), module.Invocations["connect"].Count == 1);
                cut.InvokeAsync(() => Content(cut).Instance.DismissFromJavaScriptAsync()).GetAwaiter().GetResult();
                Assert.Equal(expected.GetProperty("focusRestoredToTrigger").GetBoolean(), JSInterop.Invocations.Any(invocation => invocation.Identifier == "dispose"));
                break;
            }
            default:
                Assert.Contains(id, Unasserted);
                break;
        }
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
