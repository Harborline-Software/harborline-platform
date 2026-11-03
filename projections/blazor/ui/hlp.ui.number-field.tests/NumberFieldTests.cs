using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class NumberFieldTests : BunitContext
{
    // T-962 review: HTML parses tabindex as an integer and min, max and step as floating-point numbers,
    // all with an ASCII minus and a dot decimal. Blazor writes int and double attributes with the
    // current culture: de-DE writes 0.5 as "0,5", ar-SA as "0٫5", and sv-SE writes -1 with U+2212, so
    // the browser ignored the step and the bounds, and a tabindex of -1 left the field in the tab order.
    [Theory, InlineData("de-DE"), InlineData("ar-SA"), InlineData("fa-IR"), InlineData("he-IL"), InlineData("sv-SE"), InlineData("nb-NO")]
    public void NumericAttributesAreInvariantUnderEveryCulture(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var input = Render<HarborlineNumberField>(p => p.Add(x => x.Name, "depth").Add(x => x.Value, "1.5")
                .Add(x => x.Min, -2.5).Add(x => x.Max, 1000.75).Add(x => x.Step, .5).Add(x => x.TabIndex, -1)).Find("input");
            Assert.Equal(("-2.5", "1000.75", "0.5", "-1"), (input.GetAttribute("min"), input.GetAttribute("max"), input.GetAttribute("step"), input.GetAttribute("tabindex")));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void EmitsRawEditingStringAndPreservesConstraints()
    {
        string? next = null;
        var cut = Render<HarborlineNumberField>(p => p.Add(x => x.Name, "quantity").Add(x => x.Value, "100.50")
            .Add(x => x.Min, 0).Add(x => x.Max, 1000).Add(x => x.Step, .5).Add(x => x.TabIndex, -1)
            .Add(x => x.ValueChanged, value => next = value));
        var input = cut.Find("input");
        Assert.Equal("number", input.GetAttribute("type"));
        Assert.Equal("100.50", input.GetAttribute("value"));
        Assert.Equal("-1", input.GetAttribute("tabindex"));
        input.Input("-");
        Assert.Equal("-", next);
    }

    [Fact]
    public void InheritedRequiredAndDisabledAreNative()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<Microsoft.AspNetCore.Components.CascadingValue<FormFieldContextValue>>(0);
            builder.AddAttribute(1, "Value", new FormFieldContextValue("quantity", "quantity-label", "quantity-hint", true, true));
            builder.AddAttribute(2, "ChildContent", (Microsoft.AspNetCore.Components.RenderFragment)(child =>
            {
                child.OpenComponent<HarborlineNumberField>(0); child.AddAttribute(1, "Name", "quantity"); child.AddAttribute(2, "Value", ""); child.CloseComponent();
            }));
            builder.CloseComponent();
        });
        var input = cut.Find("input");
        Assert.True(input.HasAttribute("required"));
        Assert.True(input.HasAttribute("disabled"));
        Assert.Equal("quantity-hint", input.GetAttribute("aria-describedby"));
    }

    [Theory, Trait("ModuleConformance", "hlp.ui.number-field")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.number-field", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("number-field.", fixture.RootElement.GetProperty("id").GetString());
            Assert.Equal("number", Render<HarborlineNumberField>(p => p.Add(x => x.Name, "x").Add(x => x.Value, string.Empty)).Find("input").GetAttribute("type"));
        });
    }
}
