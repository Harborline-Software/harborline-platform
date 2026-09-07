using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class TableTests : BunitContext
{
    [Fact]
    public void NativeFamilyCaptionScopeDensityAndAttributesArePreserved()
    {
        var cut = Render(BuildTable(TableDensity.Small, ["Ada", "Grace"]));
        Assert.Single(cut.FindAll("table"));
        Assert.Single(cut.FindAll("thead"));
        Assert.Equal(2, cut.FindAll("tbody tr").Count);
        Assert.Equal("People", cut.Find("caption").TextContent);
        Assert.Equal("col", cut.Find("th").GetAttribute("scope"));
        Assert.Contains("hl-table__cell--density-sm", cut.Find("td").ClassList);
        Assert.Equal("semantic", cut.Find("table").GetAttribute("data-owner"));
        Assert.False(cut.Find(".hl-table-scroll").HasAttribute("data-owner"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.table")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("table.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Single(Render(BuildTable(TableDensity.Medium, ["one"])).FindAll("table"));
    }

    internal static RenderFragment BuildTable(TableDensity density, IReadOnlyList<string> rows) => builder =>
    {
        builder.OpenComponent<HarborlineTable>(0); builder.AddAttribute(1, nameof(HarborlineTable.Density), density);
        builder.AddAttribute(2, nameof(HarborlineTable.AdditionalAttributes), new Dictionary<string, object> { ["data-owner"] = "semantic" });
        builder.AddAttribute(3, nameof(HarborlineTable.ChildContent), BuildContent(rows)); builder.CloseComponent();
    };

    internal static RenderFragment BuildContent(IReadOnlyList<string> rows) => table =>
    {
            table.OpenComponent<HarborlineTableCaption>(0); table.AddAttribute(1, nameof(HarborlineTableCaption.ChildContent), (RenderFragment)(c => c.AddContent(0, "People"))); table.CloseComponent();
            table.OpenComponent<HarborlineTableHead>(2); table.AddAttribute(3, nameof(HarborlineTableHead.ChildContent), (RenderFragment)(head =>
            {
                head.OpenComponent<HarborlineTableRow>(0); head.AddAttribute(1, nameof(HarborlineTableRow.ChildContent), (RenderFragment)(row =>
                { row.OpenComponent<HarborlineTableHeaderCell>(0); row.AddAttribute(1, nameof(HarborlineTableHeaderCell.ChildContent), (RenderFragment)(cell => cell.AddContent(0, "Name"))); row.CloseComponent(); })); head.CloseComponent();
            })); table.CloseComponent();
            table.OpenComponent<HarborlineTableBody>(4); table.AddAttribute(5, nameof(HarborlineTableBody.ChildContent), (RenderFragment)(body =>
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    var value = rows[index]; body.OpenComponent<HarborlineTableRow>(index * 4); body.SetKey(value);
                    body.AddAttribute(index * 4 + 1, nameof(HarborlineTableRow.ChildContent), (RenderFragment)(row =>
                    { row.OpenComponent<HarborlineTableCell>(0); row.AddAttribute(1, nameof(HarborlineTableCell.ChildContent), (RenderFragment)(cell => cell.AddContent(0, value))); row.CloseComponent(); })); body.CloseComponent();
                }
            })); table.CloseComponent();
    };
}
