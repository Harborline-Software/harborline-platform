using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ChartTests : BunitContext
{
    [Fact]
    public void LinePreservesOrderNullGapsAndAccessibleData()
    {
        var definition = new LineChartDefinition(["Jan", "Feb", "Mar"], [new LineChartSeries("Occupancy", [91, null, 93])]);
        var cut = Render<HarborlineChart>(parameters => parameters
            .Add(component => component.Definition, definition)
            .Add(component => component.AccessibleName, "Occupancy trend"));
        Assert.Equal("Occupancy trend", cut.Find("figure").GetAttribute("aria-label"));
        var path = Assert.Single(cut.FindAll(".hl-chart__line"));
        Assert.Equal(2, path.GetAttribute("d")!.Split('M').Length - 1);
        Assert.Equal(["Series", "Jan", "Feb", "Mar"], cut.FindAll("thead th").Select(cell => cell.TextContent).ToArray());
        Assert.Equal("Occupancy", Assert.Single(cut.FindAll("tbody th")).TextContent);
        Assert.Equal("missing", cut.FindAll("tbody td")[1].GetAttribute("data-value-state"));
    }

    [Fact]
    public void DonutOmitsNullVisualSliceButRetainsAccessibleMissingValue()
    {
        var definition = new DonutChartDefinition([new("Palm", 10), new("Bay", null)]);
        var cut = Render<HarborlineChart>(parameters => parameters.Add(component => component.Definition, definition).Add(component => component.Title, "Sites"));
        Assert.Single(cut.FindAll(".hl-chart__slice"));
        Assert.Equal(2, cut.FindAll("tbody tr").Count);
        Assert.Equal("missing", cut.FindAll("tbody td")[1].GetAttribute("data-value-state"));
    }

    [Theory]
    [InlineData("label-required")]
    [InlineData("duplicate-series-name")]
    [InlineData("category-value-count-mismatch")]
    [InlineData("non-finite-value")]
    public void InvalidDefinitionsUseStableErrorCodes(string code)
    {
        ChartDefinition definition = code switch
        {
            "label-required" => new DonutChartDefinition([new(" ", 1)]),
            "duplicate-series-name" => new LineChartDefinition(["A"], [new("S", [1]), new("S", [2])]),
            "category-value-count-mismatch" => new LineChartDefinition(["A", "B"], [new("S", [1])]),
            _ => new DonutChartDefinition([new("S", double.PositiveInfinity)])
        };
        var exception = Assert.ThrowsAny<Exception>(() => Render<HarborlineChart>(parameters => parameters.Add(component => component.Definition, definition)));
        Assert.Contains(code, exception.ToString());
    }

    [Theory]
    [InlineData("line-no-series")]
    [InlineData("line-all-null")]
    [InlineData("donut-no-slices")]
    public void NothingToPlotDrawsTheEmptyStateAndNoAxes(string shape)
    {
        ChartDefinition definition = shape switch
        {
            "line-no-series" => new LineChartDefinition([], []),
            "line-all-null" => new LineChartDefinition(["Jan"], [new("A", [null])]),
            _ => new DonutChartDefinition([])
        };
        var cut = Render<HarborlineChart>(parameters => parameters.Add(component => component.Definition, definition));
        Assert.Equal("empty", cut.Find(".hl-chart__visual").GetAttribute("data-chart-state"));
        Assert.Empty(cut.FindAll(".hl-chart__axis"));
        Assert.Empty(cut.FindAll(".hl-chart__line"));
        Assert.Empty(cut.FindAll(".hl-chart__slice"));
        Assert.Equal("No data to display", cut.Find("[data-chart-empty]").TextContent);
    }

    [Fact]
    public void EmptyParameterOverridesTheDefaultText()
    {
        var cut = Render<HarborlineChart>(parameters => parameters
            .Add(component => component.Definition, (ChartDefinition)new DonutChartDefinition([]))
            .Add(component => component.Empty, "Nothing measured yet"));
        Assert.Equal("Nothing measured yet", cut.Find("[data-chart-empty]").TextContent);
    }

    [Fact]
    public void OnePlottableValueKeepsTheAxes()
    {
        var definition = new LineChartDefinition(["Jan", "Feb"], [new LineChartSeries("A", [null, 3])]);
        var cut = Render<HarborlineChart>(parameters => parameters.Add(component => component.Definition, definition));
        Assert.Equal("plotted", cut.Find(".hl-chart__visual").GetAttribute("data-chart-state"));
        Assert.Equal(2, cut.FindAll(".hl-chart__axis").Count);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.chart")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("chart.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("line", RenderLine(1, 0).Find("figure").GetAttribute("data-hl-chart-kind"));
    }

    internal IRenderedComponent<HarborlineChart> RenderLine(int seriesCount, int cycle)
    {
        var categories = Enumerable.Range(0, 256).Select(index => $"C{index}").ToArray();
        var series = Enumerable.Range(0, seriesCount).Select(seriesIndex =>
            new LineChartSeries($"S{seriesIndex}", Enumerable.Range(0, 256).Select(index => (double?)(cycle + seriesIndex + index)).ToArray())).ToArray();
        return Render<HarborlineChart>(parameters => parameters.Add(component => component.Definition, new LineChartDefinition(categories, series)));
    }
}
