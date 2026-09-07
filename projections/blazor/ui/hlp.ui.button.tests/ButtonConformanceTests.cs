using Bunit;
using Harborline.Foundation.Enums;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ButtonConformanceTests : BunitContext
{
    [Fact]
    [Trait("ConformanceCase", "button.defaults")]
    public void DefaultsAreNativeAndCanonical()
    {
        AssertFixture("button.defaults");
        var cut = Render<HarborlineButton>(parameters => parameters.AddChildContent("Save"));
        Assert.Equal(ButtonVariant.Secondary, cut.Instance.Variant);
        Assert.Equal(ButtonSize.Medium, cut.Instance.Size);
        Assert.Equal(FillMode.Solid, cut.Instance.FillMode);
        Assert.Equal(RoundedMode.Medium, cut.Instance.Rounded);
        Assert.Equal("button", cut.Find("button").GetAttribute("type"));
    }

    [Fact]
    [Trait("ConformanceCase", "button.activation")]
    public void EnabledActivationEmitsExactlyOneEvent()
    {
        AssertFixture("button.activation");
        var activations = 0;
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.OnClick, _ => activations++)
            .AddChildContent("Save"));
        cut.Find("button").Click();
        Assert.Equal(1, activations);
    }

    [Fact]
    [Trait("ConformanceCase", "button.disabled")]
    public void DisabledStateIsNativeAndInert()
    {
        AssertFixture("button.disabled");
        var activations = 0;
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Enabled, false)
            .Add(component => component.OnClick, _ => activations++)
            .AddChildContent("Save"));
        var button = cut.Find("button");
        Assert.True(button.HasAttribute("disabled"));
        button.Click();
        Assert.Equal(0, activations);
    }

    [Fact]
    [Trait("ConformanceCase", "button.loading")]
    public void LoadingIsFocusableBusyAndInertWithoutHidingContent()
    {
        AssertFixture("button.loading");
        var activations = 0;
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Loading, true)
            .Add(component => component.OnClick, _ => activations++)
            .AddChildContent("Saving"));
        var button = cut.Find("button");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.Equal("true", button.GetAttribute("aria-disabled"));
        Assert.Contains("Saving", button.TextContent);
        Assert.NotNull(button.QuerySelector(".hl-button__loading-indicator"));
        button.Click();
        Assert.Equal(0, activations);
    }

    [Fact]
    [Trait("ConformanceCase", "button.form")]
    public void FormTypesAndAssociationSurviveProjection()
    {
        AssertFixture("button.form");
        foreach (var (type, expected) in new[]
        {
            (ButtonType.Button, "button"),
            (ButtonType.Submit, "submit"),
            (ButtonType.Reset, "reset"),
        })
        {
            var cut = Render<HarborlineButton>(parameters => parameters
                .Add(component => component.ButtonType, type)
                .Add(component => component.Form, "profile")
                .AddChildContent("Save"));
            Assert.Equal(expected, cut.Find("button").GetAttribute("type"));
            Assert.Equal("profile", cut.Find("button").GetAttribute("form"));
        }
    }

    [Fact]
    [Trait("ConformanceCase", "button.content-order")]
    public void ContentOrderRemainsLogicalInBothDirections()
    {
        AssertFixture("button.content-order");
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.LeadingIcon, builder => builder.AddContent(0, "L"))
            .AddChildContent("Save")
            .Add(component => component.TrailingIcon, builder => builder.AddContent(0, "T"))
            .AddUnmatched("dir", "rtl"));
        Assert.Equal("LSaveT", string.Concat(cut.Find("button").TextContent.Where(character => !char.IsWhiteSpace(character))));
        Assert.Equal("rtl", cut.Find("button").GetAttribute("dir"));
    }

    [Fact]
    [Trait("ConformanceCase", "button.accessible-name")]
    public void UnnamedIconOnlyUseIsRejected()
    {
        AssertFixture("button.accessible-name");
        var exception = Assert.ThrowsAny<Exception>(() => Render<HarborlineButton>(parameters => parameters
            .Add(component => component.LeadingIcon, builder => builder.AddContent(0, "+"))));
        Assert.Contains("accessible-name-required", exception.ToString());
    }

    [Fact]
    [Trait("ConformanceCase", "button.host-attributes")]
    public void HostAttributesAndConsumerClassSurviveProjection()
    {
        AssertFixture("button.host-attributes");
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Class, "consumer")
            .Add(component => component.Form, "profile")
            .AddUnmatched("aria-controls", "panel")
            .AddUnmatched("data-case", "shared")
            .AddChildContent("Save"));
        var button = cut.Find("button");
        Assert.Equal("panel", button.GetAttribute("aria-controls"));
        Assert.Equal("shared", button.GetAttribute("data-case"));
        Assert.Contains("consumer", button.ClassList);
        Assert.Equal("profile", button.GetAttribute("form"));
    }

    [Fact]
    [Trait("ConformanceCase", "button.appearance")]
    public void EveryCanonicalAppearanceMapsToNonEmptyStyles()
    {
        AssertFixture("button.appearance");
        foreach (var variant in Enum.GetValues<ButtonVariant>())
        {
            var cut = Render<HarborlineButton>(parameters => parameters
                .Add(component => component.Variant, variant)
                .AddChildContent("Save"));
            Assert.False(string.IsNullOrWhiteSpace(cut.Find("button").GetAttribute("class")));
        }
    }

    private static void AssertFixture(string caseId)
    {
        var fixture = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(fixture)) return;
        using var document = System.Text.Json.JsonDocument.Parse(fixture);
        Assert.Equal(caseId, document.RootElement.GetProperty("id").GetString());
        Assert.True(document.RootElement.TryGetProperty("input", out _));
        Assert.True(document.RootElement.TryGetProperty("expected", out _));
    }
}
