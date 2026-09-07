using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class RadioGroupTests : BunitContext
{
    private static readonly IReadOnlyList<HarborlineRadioOption> Options =
    [new("monthly", "Monthly"), new("quarterly", "Quarterly", "Four times a year"), new("annual", "Annual", Disabled: true)];

    [Fact]
    public void NativeOptionsPreserveSelectionDescriptionAndDisabledState()
    {
        string? next = null;
        var cut = Render<HarborlineRadioGroup>(p => p.Add(x => x.Name, "billing").Add(x => x.Value, "monthly")
            .Add(x => x.Options, Options).Add(x => x.Orientation, RadioGroupOrientation.Horizontal)
            .Add(x => x.ValueChanged, value => next = value));
        Assert.Equal("radiogroup", cut.Find("[role=radiogroup]").GetAttribute("role"));
        Assert.Equal(3, cut.FindAll("input[type=radio]").Count);
        cut.Find("input[value=quarterly]").Change(true);
        Assert.Equal("quarterly", next);
        cut.Find("input[value=annual]").Change(true);
        Assert.Equal("quarterly", next);
        Assert.Contains("Four times a year", cut.Markup);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.radio-group")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var id = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith("radio-group.", id);
        Assert.Equal(3, Render<HarborlineRadioGroup>(p => p.Add(x => x.Name, "billing").Add(x => x.Value, "").Add(x => x.Options, Options)).FindAll("input").Count);

        if (id != "radio-group.class-parity") return;
        var input = fixture.RootElement.GetProperty("input");
        var expected = fixture.RootElement.GetProperty("expected");
        var options = input.GetProperty("options").EnumerateArray().Select(option => new HarborlineRadioOption(
            option.GetProperty("value").GetString()!,
            option.GetProperty("label").GetString()!,
            option.TryGetProperty("description", out var description) ? description.GetString() : null,
            option.TryGetProperty("disabled", out var disabled) && disabled.GetBoolean())).ToArray();
        var orientation = input.GetProperty("orientation").GetString() == "horizontal"
            ? RadioGroupOrientation.Horizontal
            : RadioGroupOrientation.Vertical;

        var cut = Render<HarborlineRadioGroup>(p => p.Add(x => x.Name, "billing").Add(x => x.Value, "")
            .Add(x => x.Options, options).Add(x => x.Orientation, orientation)
            .Add(x => x.Error, input.GetProperty("error").GetBoolean()));

        Assert.Equal(Classes(expected, "rootClasses"), cut.Find("[role=radiogroup]").ClassList);
        foreach (var option in options)
        {
            var control = cut.Find($"input[value={option.Value}]");
            var label = control.ParentElement!;
            Assert.Equal(Classes(expected, "controlClasses"), control.ClassList);
            Assert.Equal(Classes(expected, option.Disabled ? "disabledLabelClasses" : "labelClasses"), label.ClassList);
            Assert.Equal(Classes(expected, "optionClasses"), label.ParentElement!.ClassList);
            Assert.Equal(Classes(expected, "labelTextClasses"), label.QuerySelector("span")!.ClassList);
        }

        // This lane used to nest the description in a hl-radio-group__copy span inside the label,
        // a class the authority never defined; the authority spaces it as a sibling of the label.
        var describedValue = options.First(option => !string.IsNullOrEmpty(option.Description)).Value;
        var described = cut.Find($"input[value={describedValue}]").ParentElement!.ParentElement!;
        var description = described.QuerySelector(".hl-radio-group__description")!;
        Assert.Equal(Classes(expected, "descriptionClasses"), description.ClassList);
        Assert.Equal(Classes(expected, "optionClasses"), description.ParentElement!.ClassList);
        if (expected.GetProperty("descriptionOutsideLabel").GetBoolean())
        {
            Assert.Null(description.Closest("label"));
        }
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
