using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class EmptyStateNativeTests : BunitContext
{
    [Fact]
    public void VariantsContentActionAndHostContextRemainEquivalent()
    {
        var activations = 0;
        var cut = Render<HarborlineEmptyState>(parameters => parameters
            .Add(component => component.Title, "No payments yet.")
            .Add(component => component.Description, "Record the first payment.")
            .Add(component => component.Variant, EmptyStateVariant.Actionable)
            .Add(component => component.ActionLabel, "Record payment")
            .Add(component => component.OnAction, () => activations++)
            .Add(component => component.Class, "consumer")
            .AddUnmatched("lang", "en-US"));
        Assert.Equal("actionable", cut.Find("[data-hl-variant]").GetAttribute("data-hl-variant"));
        Assert.Equal("true", cut.Find("[data-hl-icon]").GetAttribute("aria-hidden"));
        Assert.Equal("button", cut.Find("button").GetAttribute("type"));
        cut.Find("button").Click();
        Assert.Equal(1, activations);
        Assert.Contains("consumer", cut.Find("[data-hl-variant]").ClassList);
    }

    [Fact]
    public void InvalidTitleAndIncompleteActionFailClosed()
    {
        Assert.Contains("title-required", Assert.ThrowsAny<Exception>(() => Render<HarborlineEmptyState>()).ToString());
        var error = Assert.ThrowsAny<Exception>(() => Render<HarborlineEmptyState>(parameters => parameters
            .Add(component => component.Title, "No items")
            .Add(component => component.ActionLabel, "Act")));
        Assert.Contains("action-incomplete", error.ToString());
    }

    [Fact]
    [Trait("ModuleConformance", "hlp.ui.empty-state")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("empty-state.", fixture.RootElement.GetProperty("id").GetString());
        var cut = Render<HarborlineEmptyState>(parameters => parameters.Add(component => component.Title, "No results"));
        Assert.Empty(cut.FindAll("[role=status]"));
        Assert.Equal("true", cut.Find("[data-hl-icon]").GetAttribute("aria-hidden"));
    }
}
