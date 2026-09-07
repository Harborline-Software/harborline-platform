using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ChartPerformanceTests : BunitContext
{
    [Fact]
    public void LargeDataAndNinetySixReplacementsStayStructurallyExact()
    {
        var categories = Enumerable.Range(0, 256).Select(index => $"C{index}").ToArray();
        var cut = Render<HarborlineChart>(parameters => parameters.Add(component => component.Definition, Definition(0, categories)));
        for (var cycle = 0; cycle < 96; cycle++)
        {
            cut.Render(parameters => parameters.Add(component => component.Definition, Definition(cycle, categories)));
            Assert.Equal(2, cut.FindAll(".hl-chart__line").Count);
            Assert.Equal(2, cut.FindAll("tbody tr").Count);
            Assert.Equal(512, cut.FindAll("tbody td").Count);
            Assert.Equal((cycle + 255).ToString(), cut.FindAll("tbody tr")[0].QuerySelectorAll("td")[255].TextContent);
        }
    }

    private static LineChartDefinition Definition(int cycle, IReadOnlyList<string> categories) => new(
        categories,
        [new("Primary", Enumerable.Range(0, 256).Select(index => (double?)(cycle + index)).ToArray()),
         new("Secondary", Enumerable.Range(0, 256).Select(index => (double?)(cycle + index + 1)).ToArray())]);
}
