using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DateFieldTests : BunitContext
{
    [Fact]
    public void PreservesRawDateBoundsAndAccessibility()
    {
        string? next = null;
        var cut = Render<HarborlineDateField>(p => p.Add(x => x.Name, "issued").Add(x => x.Value, "2026-08-09")
            .Add(x => x.Min, "2026-01-01").Add(x => x.Max, "2026-12-31").Add(x => x.Error, true)
            .Add(x => x.AccessibleLabel, "Issue date").Add(x => x.ValueChanged, value => next = value));
        var input = cut.Find("input");
        Assert.Equal("date", input.GetAttribute("type"));
        Assert.Equal("issued", input.Id);
        Assert.Equal("2026-08-09", input.GetAttribute("value"));
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        input.Input("2026-12-31");
        Assert.Equal("2026-12-31", next);
        input.Input(string.Empty);
        Assert.Equal(string.Empty, next);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.date-field")]
    public void SharedFixtureConforms() => AssertFixture("date-field.", "date");
    private void AssertFixture(string prefix, string type)
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal(type, Render<HarborlineDateField>(p => p.Add(x => x.Name, "x").Add(x => x.Value, string.Empty)).Find("input").GetAttribute("type"));
    }
}
