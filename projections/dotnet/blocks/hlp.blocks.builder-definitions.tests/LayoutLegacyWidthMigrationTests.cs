using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Forms;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

// DES-0052 layout-ck-41: legacy Forms widths migrate onto twelve tracks, the smallest count
// divisible by three and four, and `full` maps to `fill` with no span.
public sealed class LayoutLegacyWidthMigrationTests
{
    [Theory]
    [InlineData(FieldWidth.OneQuarter, 3)]
    [InlineData(FieldWidth.OneThird, 4)]
    [InlineData(FieldWidth.OneHalf, 6)]
    [InlineData(FieldWidth.TwoThirds, 8)]
    [InlineData(FieldWidth.ThreeQuarters, 9)]
    public void A_fractional_width_becomes_its_twelve_track_span(FieldWidth width, int span)
    {
        var placement = LayoutLegacyWidthMigration.Migrate(new FieldPlacement { Width = width });

        Assert.Equal(span, placement.Span);
    }

    [Fact]
    public void Auto_becomes_hug()
    {
        var placement = LayoutLegacyWidthMigration.Migrate(new FieldPlacement { Width = FieldWidth.Auto });

        Assert.Equal(new LayoutPlacement(Width: LayoutSizing.Hug), placement);
    }

    [Fact]
    public void Full_becomes_fill_and_carries_no_span_that_contradicts_it()
    {
        var placement = LayoutLegacyWidthMigration.Migrate(new FieldPlacement { Width = FieldWidth.Full });

        Assert.Equal(new LayoutPlacement(Width: LayoutSizing.Fill), placement);
    }

    [Fact]
    public void Full_drops_a_legacy_col_span_rather_than_emit_it_beside_fill()
    {
        var placement = LayoutLegacyWidthMigration.Migrate(new FieldPlacement { Width = FieldWidth.Full, ColSpan = 4m });

        Assert.Equal(new LayoutPlacement(Width: LayoutSizing.Fill), placement);
    }

    [Fact]
    public void An_in_range_legacy_col_span_and_grow_carry_over()
    {
        var placement = LayoutLegacyWidthMigration.Migrate(new FieldPlacement { ColSpan = 5m, Grow = 2m });

        Assert.Equal(new LayoutPlacement(Width: LayoutSizing.Hug, Span: 5, Grow: 2), placement);
    }

    [Theory(DisplayName = "layout-eng-9 successor: a Forms table column keeps its width and alignment")]
    [InlineData("1/3", "end", 4, LayoutSizing.Hug, LayoutAlignment.End)]
    [InlineData("full", "center", 1, LayoutSizing.Fill, LayoutAlignment.Center)]
    [InlineData(null, null, 1, LayoutSizing.Hug, LayoutAlignment.Start)]
    public void A_forms_table_column_keeps_its_width_and_alignment(
        string? width, string? align, int span, LayoutSizing sizing, LayoutAlignment justify)
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

    [Theory]
    [InlineData(0, 0, "/col_span")]
    [InlineData(13, 0, "/col_span")]
    [InlineData(2.5, 0, "/col_span")]
    [InlineData(1, -1, "/grow")]
    [InlineData(1, 13, "/grow")]
    [InlineData(1, 0.5, "/grow")]
    public void An_invalid_legacy_number_refuses_rather_than_clamping(double colSpan, double grow, string pointer)
    {
        var legacy = new FieldPlacement { ColSpan = (decimal)colSpan, Grow = (decimal)grow };

        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutLegacyWidthMigration.Migrate(legacy));

        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.NumericOutOfRange, pointer)], refused.Refusals);
    }
}
