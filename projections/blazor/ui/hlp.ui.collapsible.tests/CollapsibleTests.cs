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
        AssertTriggerNamedAndWired(cut, "Settings", "Optional configuration");
        button.Click();
        Assert.Equal([true], calls);
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-expanded"));
        AssertTriggerNamedAndWired(cut, "Settings", "Optional configuration");
        Assert.Contains("consumer", cut.Find(".hl-collapsible").ClassList);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutASubtitleTheTriggerHasNoDescription(string? subtitle)
    {
        var cut = Render<HarborlineCollapsible>(p => p.Add(x => x.Title, "Settings").Add(x => x.Subtitle, subtitle));
        AssertTriggerNamedAndWired(cut, "Settings", "");
    }

    // The trigger is named by the title alone, through aria-labelledby, and described by the subtitle; it
    // controls the region, which the same title names. Ids resolve, once each.
    private static void AssertTriggerNamedAndWired(IRenderedComponent<HarborlineCollapsible> cut, string name, string description)
    {
        Accname.AssertIdReferencesResolve(cut);
        var trigger = cut.Find(".hl-collapsible__trigger");
        var region = cut.Find("[role=region]");
        Assert.Equal(name, Accname.AccessibleName(trigger));
        Assert.Equal(description, Accname.AccessibleDescription(trigger));
        Accname.AssertReferences(trigger, "aria-labelledby", cut.Find(".hl-collapsible__title"));
        Accname.AssertReferences(trigger, "aria-controls", region);
        Assert.Equal(name, Accname.AccessibleName(region));
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

    [Theory, Trait("ModuleConformance", "hlp.ui.collapsible")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.collapsible", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current;
            if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw);
            Assert.StartsWith("collapsible.", fixture.RootElement.GetProperty("id").GetString());
            Assert.Equal("button", Render<HarborlineCollapsible>(p => p.Add(x => x.Title, "Settings")).Find("button").GetAttribute("type"));
        });
    }
}
