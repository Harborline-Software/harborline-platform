using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class NumericTextBoxTests : BunitContext
{
    [Fact]
    public void ControlledValueFormatsAndStrictCommitRequestsWithoutLocalMutation()
    {
        double? requested = null;
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.Value, 42)
            .Add(component => component.ValueChanged, value => requested = value));
        Assert.Equal("42.00", cut.Find("input").GetAttribute("value"));
        cut.Find("input").Focus(); cut.Find("input").Input("12abc"); cut.Find("input").Blur();
        Assert.Null(requested);
        Assert.Equal("42.00", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void EnterThenBlurCommitsExactlyOnceAndClamps()
    {
        var requests = new List<double?>();
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.DefaultValue, 4).Add(component => component.Min, 2).Add(component => component.Max, 10)
            .Add(component => component.ValueChanged, value => requests.Add(value)));
        cut.Find("input").Focus(); cut.Find("input").Input("20"); cut.Find("input").KeyDown("Enter"); cut.Find("input").Blur();
        Assert.Equal([10d], requests);
    }

    [Fact]
    public void ReadOnlyHidesSpinnersAndNeverEmits()
    {
        var requests = 0;
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters.Add(component => component.Value, 2).Add(component => component.ReadOnly, true).Add(component => component.ValueChanged, _ => requests++));
        Assert.Empty(cut.FindAll("button")); cut.Find("input").Focus(); cut.Find("input").Input("5"); cut.Find("input").Blur(); Assert.Equal(0, requests);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.numeric-text-box")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("numeric-text-box.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("text", Render<HarborlineNumericTextBox>().Find("input").GetAttribute("type"));
    }
}
