using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class TablePerformanceTests : BunitContext
{
    [Fact]
    public void TwoHundredFiftySixRowsAndNinetySixReplacementsStayStructurallyExact()
    {
        var rows = Enumerable.Range(0, 256).Select(i => $"0-{i}").ToArray();
        var cut = Render<HarborlineTable>(parameters => parameters
            .Add(component => component.Density, TableDensity.Medium)
            .Add(component => component.ChildContent, TableTests.BuildContent(rows)));
        for (var cycle = 0; cycle < 96; cycle++)
        {
            rows = Enumerable.Range(0, 256).Select(i => $"{cycle}-{i}").ToArray();
            cut.Render(parameters => parameters
                .Add(component => component.Density, TableDensity.Medium)
                .Add(component => component.ChildContent, TableTests.BuildContent(rows)));
            Assert.Equal(256, cut.FindAll("tbody tr").Count);
            Assert.Equal($"{cycle}-0", cut.Find("tbody tr:first-child td").TextContent);
            Assert.Equal($"{cycle}-255", cut.Find("tbody tr:last-child td").TextContent);
        }
    }
}
