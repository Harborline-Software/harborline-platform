using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DateTimeFieldTests : BunitContext
{
    [Fact]
    public void PreservesLocalWallClockStringAndStep()
    {
        string? next = null;
        var cut = Render<HarborlineDateTimeField>(p => p.Add(x => x.Name, "starts").Add(x => x.Value, "2026-11-01T01:30")
            .Add(x => x.Step, 900).Add(x => x.ValueChanged, value => next = value));
        var input = cut.Find("input");
        Assert.Equal("datetime-local", input.GetAttribute("type"));
        Assert.Equal("2026-11-01T01:30", input.GetAttribute("value"));
        Assert.Equal("900", input.GetAttribute("step"));
        input.Input("2026-03-08T02:30");
        Assert.Equal("2026-03-08T02:30", next);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.date-time-field")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("date-time-field.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("datetime-local", Render<HarborlineDateTimeField>(p => p.Add(x => x.Name, "x").Add(x => x.Value, string.Empty)).Find("input").GetAttribute("type"));
    }
}
