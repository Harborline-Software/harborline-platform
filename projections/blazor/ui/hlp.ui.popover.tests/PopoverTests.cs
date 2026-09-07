using Bunit;
using Microsoft.AspNetCore.Components;
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

    [Fact, Trait("ModuleConformance", "hlp.ui.popover")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); var id = fixture.RootElement.GetProperty("id").GetString(); Assert.StartsWith("popover.", id);
        Assert.Equal("false", Render<HarborlinePopover>(p => p.Add(x => x.ChildContent, (RenderFragment)(b => { b.OpenComponent<HarborlinePopoverTrigger>(0); b.CloseComponent(); }))).Find("button").GetAttribute("aria-expanded"));
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
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
