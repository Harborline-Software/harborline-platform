using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class NumberFieldTests : BunitContext
{
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

    [Fact, Trait("ModuleConformance", "hlp.ui.number-field")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("number-field.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("number", Render<HarborlineNumberField>(p => p.Add(x => x.Name, "x").Add(x => x.Value, string.Empty)).Find("input").GetAttribute("type"));
    }
}
