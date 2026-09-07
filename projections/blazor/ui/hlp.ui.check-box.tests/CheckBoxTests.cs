using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class CheckBoxTests : BunitContext
{
    [Fact]
    public void MixedRequiredLabelledAndHostAttributesMatchContract()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<HarborlineCheckBox>(p => p
            .Add(x => x.Checked, CheckBoxState.Mixed)
            .Add(x => x.Label, "Include")
            .Add(x => x.Required, true)
            .Add(x => x.Error, true)
            .Add(x => x.Id, "include")
            .Add(x => x.Name, "include")
            .Add(x => x.Value, "yes")
            .Add(x => x.Class, "consumer"));
        var input = cut.Find("input");
        Assert.Equal("mixed", input.GetAttribute("aria-checked"));
        Assert.Equal("true", input.GetAttribute("aria-required"));
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal("include", input.GetAttribute("name"));
        Assert.Contains("consumer", cut.Find("label").ClassList);
    }

    [Fact]
    public void ControlledAndDisabledActivationRemainInertLocally()
    {
        var calls = 0;
        var cut = Render<HarborlineCheckBox>(p => p
            .Add(x => x.Checked, CheckBoxState.Unchecked)
            .Add(x => x.Disabled, true)
            .Add(x => x.CheckedChanged, _ => calls++));
        cut.Find("input").Change(true);
        Assert.Equal(0, calls);
        Assert.False(cut.Find("input").HasAttribute("checked"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.check-box")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var id = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith("check-box.", id);
        Assert.Equal("checkbox", Render<HarborlineCheckBox>().Find("input").GetAttribute("type"));

        if (id != "check-box.class-parity") return;
        var input = fixture.RootElement.GetProperty("input");
        var expected = fixture.RootElement.GetProperty("expected");
        var size = input.GetProperty("size").GetString() switch
        {
            "sm" => CheckBoxSize.Small,
            "lg" => CheckBoxSize.Large,
            _ => CheckBoxSize.Medium,
        };

        var labelled = Render<HarborlineCheckBox>(p => p
            .Add(x => x.Label, input.GetProperty("label").GetString())
            .Add(x => x.Size, size)
            .Add(x => x.Error, input.GetProperty("error").GetBoolean()));
        Assert.Equal(Classes(expected, "controlClasses"), labelled.Find("input").ClassList);
        Assert.Equal(Classes(expected, "rootClasses"), labelled.Find("label").ClassList);

        // The root class list must not depend on whether a label is present: this lane used to
        // render a hl-check-box__standalone root in that case, which the authority never defined.
        var bare = Render<HarborlineCheckBox>(p => p
            .Add(x => x.Size, size)
            .Add(x => x.Error, input.GetProperty("error").GetBoolean()));
        Assert.Equal(Classes(expected, "controlClasses"), bare.Find("input").ClassList);
        Assert.Equal(Classes(expected, "rootClasses"), bare.Find("span").ClassList);
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
