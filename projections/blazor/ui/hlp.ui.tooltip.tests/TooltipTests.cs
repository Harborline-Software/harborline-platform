using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class TooltipTests : BunitContext
{
    [Fact]
    public void ActualTriggerOwnsDescriptionAndCombinedHoverFocusOwnership()
    {
        var cut = Render<HarborlineTooltip>(p => p.Add(x => x.Content, "More details").Add(x => x.DelayDuration, 0).Add(x => x.ChildContent, Trigger()));
        var trigger = cut.Find("button");
        Assert.False(trigger.HasAttribute("aria-describedby"));
        trigger.MouseEnter();
        cut.WaitForAssertion(() => Assert.Equal("tooltip", cut.Find("[role=tooltip]").GetAttribute("role")));
        trigger = cut.Find("button"); trigger.Focus(); trigger.MouseLeave();
        Assert.Single(cut.FindAll("[role=tooltip]"));
        trigger.Blur();
        Assert.Empty(cut.FindAll("[role=tooltip]"));
    }

    [Fact]
    public void ControlledTriggerEmitsWithoutMutatingVisibility()
    {
        var states = new List<bool>();
        var cut = Render<HarborlineTooltip>(p => p.Add(x => x.Content, "Hint").Add(x => x.DelayDuration, 0).Add(x => x.Open, false)
            .Add(x => x.OpenChanged, value => states.Add(value)).Add(x => x.ChildContent, Trigger()));
        cut.Find("button").MouseEnter();
        cut.WaitForAssertion(() => Assert.Equal([true], states));
        Assert.Empty(cut.FindAll("[role=tooltip]"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.tooltip")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("tooltip.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Empty(Render<HarborlineTooltip>(p => p.Add(x => x.Content, "Hint").Add(x => x.ChildContent, Trigger())).FindAll("[role=tooltip]"));
    }

    private static RenderFragment Trigger() => builder =>
    {
        builder.OpenComponent<HarborlineTooltipTrigger>(0);
        builder.AddAttribute(1, nameof(HarborlineTooltipTrigger.ChildContent), (RenderFragment)(b => b.AddContent(0, "Help")));
        builder.CloseComponent();
    };
}
