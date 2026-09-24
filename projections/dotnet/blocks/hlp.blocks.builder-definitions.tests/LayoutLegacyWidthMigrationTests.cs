using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Forms;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

// DES-0052 layout-ck-41, as ruled by the owner on 2026-09-24 (T-582): a legacy grid section keeps
// its own column count and its col_span unchanged; a flex section's widths migrate onto twelve
// tracks, `full` becoming `fill` with no span. A legacy field used col_span only in a grid and
// width/grow only in flex, so each kind ignores the other's members, as the legacy renderer did.
public sealed class LayoutLegacyWidthMigrationTests
{
    private static SectionLayout Grid(decimal? columns = null)
        => columns is { } count ? new() { Kind = SectionLayoutKind.Grid, Columns = count } : new() { Kind = SectionLayoutKind.Grid };

    private static readonly SectionLayout Flex = new() { Kind = SectionLayoutKind.Flex };

    private static readonly SectionLayout Stack = new() { Kind = SectionLayoutKind.Stack };

    [Fact(DisplayName = "grid: the form-view.layout fixture (3 columns, colSpan 2, width 2/3) keeps span 2 in a 3-column container")]
    public void A_grid_keeps_its_column_count_and_its_span()
    {
        var section = Grid(3m);

        Assert.Equal(3, LayoutLegacyWidthMigration.ColumnCount(section));
        Assert.Equal(new LayoutPlacement(Span: 2),
            LayoutLegacyWidthMigration.Migrate(section, new FieldPlacement { ColSpan = 2m, Width = FieldWidth.TwoThirds }));
    }

    [Fact]
    public void A_grid_with_no_legacy_columns_keeps_the_legacy_default_of_two()
    {
        Assert.Equal(2, LayoutLegacyWidthMigration.ColumnCount(Grid()));
    }

    [Fact]
    public void A_grid_field_with_no_col_span_carries_no_span()
    {
        Assert.Equal(new LayoutPlacement(), LayoutLegacyWidthMigration.Migrate(Grid(4m), new FieldPlacement { Grow = 3m }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(2.5)]
    public void A_grid_column_count_outside_one_to_twelve_refuses(double columns)
    {
        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutLegacyWidthMigration.ColumnCount(Grid((decimal)columns)));

        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.NumericOutOfRange, "/columns")], refused.Refusals);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(1.5)]
    public void A_grid_col_span_outside_one_to_its_column_count_refuses(double colSpan)
    {
        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(
            () => LayoutLegacyWidthMigration.Migrate(Grid(3m), new FieldPlacement { ColSpan = (decimal)colSpan }));

        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.NumericOutOfRange, "/col_span")], refused.Refusals);
    }

    [Theory]
    [InlineData(FieldWidth.OneQuarter, 3)]
    [InlineData(FieldWidth.OneThird, 4)]
    [InlineData(FieldWidth.OneHalf, 6)]
    [InlineData(FieldWidth.TwoThirds, 8)]
    [InlineData(FieldWidth.ThreeQuarters, 9)]
    public void A_flex_fractional_width_becomes_its_twelve_track_span(FieldWidth width, int span)
    {
        Assert.Equal(12, LayoutLegacyWidthMigration.ColumnCount(Flex));
        Assert.Equal(new LayoutPlacement(Span: span), LayoutLegacyWidthMigration.Migrate(Flex, new FieldPlacement { Width = width }));
    }

    [Fact]
    public void Flex_auto_becomes_hug_and_full_becomes_fill_with_no_span()
    {
        Assert.Equal(new LayoutPlacement(Width: LayoutSizing.Hug), LayoutLegacyWidthMigration.Migrate(Flex, new FieldPlacement { Width = FieldWidth.Auto }));
        Assert.Equal(new LayoutPlacement(Width: LayoutSizing.Fill), LayoutLegacyWidthMigration.Migrate(Flex, new FieldPlacement { Width = FieldWidth.Full }));
    }

    [Fact]
    public void Flex_carries_grow_and_ignores_a_col_span()
    {
        Assert.Equal(new LayoutPlacement(Span: 6, Grow: 2),
            LayoutLegacyWidthMigration.Migrate(Flex, new FieldPlacement { Width = FieldWidth.OneHalf, Grow = 2m, ColSpan = 4m }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    [InlineData(0.5)]
    public void A_flex_grow_outside_its_range_refuses(double grow)
    {
        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(
            () => LayoutLegacyWidthMigration.Migrate(Flex, new FieldPlacement { Grow = (decimal)grow }));

        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.NumericOutOfRange, "/grow")], refused.Refusals);
    }

    [Fact]
    public void A_stack_ignores_span_width_and_grow()
    {
        Assert.Equal(1, LayoutLegacyWidthMigration.ColumnCount(Stack));
        Assert.Equal(new LayoutPlacement(),
            LayoutLegacyWidthMigration.Migrate(Stack, new FieldPlacement { ColSpan = 2m, Width = FieldWidth.OneHalf, Grow = 1m }));
    }

    [Theory(DisplayName = "layout-eng-9 successor: a Forms table column keeps its width and alignment")]
    [InlineData("1/3", "end", 4, LayoutSizing.Hug, LayoutAlignment.End)]
    [InlineData("full", "center", null, LayoutSizing.Fill, LayoutAlignment.Center)]
    [InlineData(null, null, null, LayoutSizing.Hug, LayoutAlignment.Start)]
    public void A_forms_table_column_keeps_its_width_and_alignment(
        string? width, string? align, int? span, LayoutSizing sizing, LayoutAlignment justify)
    {
        var placement = LayoutLegacyWidthMigration.MigrateColumn(width, align);

        Assert.Equal(new LayoutPlacement(Width: sizing, Span: span, JustifySelf: justify), placement);
    }

    [Theory]
    [InlineData("2/5", null, "/width")]
    [InlineData(null, "baseline", "/align")]
    public void An_unknown_column_token_refuses(string? width, string? align, string pointer)
    {
        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutLegacyWidthMigration.MigrateColumn(width, align));

        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.PlacementTokenUnknown, pointer)], refused.Refusals);
    }
}
