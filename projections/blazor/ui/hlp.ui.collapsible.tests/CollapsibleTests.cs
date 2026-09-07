using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class CollapsibleTests : BunitContext
{
    [Fact]
    public void TitledPanelRelationshipsAndUncontrolledToggleMatchContract()
    {
        var calls = new List<bool>();
        var cut = Render<HarborlineCollapsible>(p => p
            .Add(x => x.Title, "Settings")
            .Add(x => x.Subtitle, "Optional configuration")
            .Add(x => x.OpenChanged, calls.Add)
            .Add(x => x.Class, "consumer")
            .AddUnmatched("dir", "rtl")
            .AddChildContent("Body"));
        var button = cut.Find("button");
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal(button.GetAttribute("aria-controls"), cut.Find("[role=region]").Id);
        button.Click();
        Assert.Equal([true], calls);
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-expanded"));
        Assert.Contains("consumer", cut.Find(".hl-collapsible").ClassList);
    }

    [Fact]
    public void HeaderActionDoesNotToggleAndMissingTitleFailsClosed()
    {
        var actionCalls = 0;
        var openCalls = 0;
        var cut = Render<HarborlineCollapsible>(p => p
            .Add(x => x.Title, "Settings")
            .Add(x => x.OpenChanged, _ => openCalls++)
            .Add(x => x.HeaderActions, builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddAttribute(2, "onclick", Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => actionCalls++));
                builder.AddContent(3, "Action");
                builder.CloseElement();
            }));
        cut.Find(".hl-collapsible__actions button").Click();
        Assert.Equal(1, actionCalls);
        Assert.Equal(0, openCalls);
        Assert.ThrowsAny<Exception>(() => Render<HarborlineCollapsible>());
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.collapsible")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("collapsible.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("button", Render<HarborlineCollapsible>(p => p.Add(x => x.Title, "Settings")).Find("button").GetAttribute("type"));
    }
}
