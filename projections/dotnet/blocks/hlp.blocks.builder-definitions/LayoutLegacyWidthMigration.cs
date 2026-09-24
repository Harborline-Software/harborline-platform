using Harborline.Contracts.Forms;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// DES-0052 layout-ck-41 — migrates legacy Forms section placement into Layout, as ruled by the
/// owner on 2026-09-24 (T-582). A legacy field used <c>col_span</c> only in a <c>grid</c> section,
/// counted against that section's own column count, and width and grow only in a <c>flex</c>
/// section. A grid therefore keeps its count and its spans unchanged; flex widths migrate onto
/// twelve tracks, the smallest count divisible by both three and four. Invalid legacy numbers
/// refuse rather than normalise (R-0108 addendum 2026-09-24).
/// </summary>
public static class LayoutLegacyWidthMigration
{
    private const string Stage = "definition.migrate";

    /// <summary>The legacy grid column count when a section states none.</summary>
    private const int LegacyGridColumns = 2;

    /// <summary>The track count flex widths migrate onto.</summary>
    private const int FlexTracks = 12;

    private static readonly Dictionary<string, FieldWidth> WidthTokens = new(StringComparer.Ordinal)
    {
        ["auto"] = FieldWidth.Auto,
        ["1/4"] = FieldWidth.OneQuarter,
        ["1/3"] = FieldWidth.OneThird,
        ["1/2"] = FieldWidth.OneHalf,
        ["2/3"] = FieldWidth.TwoThirds,
        ["3/4"] = FieldWidth.ThreeQuarters,
        ["full"] = FieldWidth.Full,
    };

    private static readonly Dictionary<string, LayoutAlignment> AlignTokens = new(StringComparer.Ordinal)
    {
        ["start"] = LayoutAlignment.Start,
        ["center"] = LayoutAlignment.Center,
        ["end"] = LayoutAlignment.End,
        ["stretch"] = LayoutAlignment.Stretch,
    };

    /// <summary>The column count of the Layout container a legacy section migrates to.</summary>
    /// <param name="section">The legacy section layout.</param>
    /// <returns>A grid's own count (legacy default two), twelve for flex, one for stack.</returns>
    /// <exception cref="LayoutDefinitionAdmissionException">A grid's column count is not a whole number from one to twelve.</exception>
    public static int ColumnCount(SectionLayout section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section.Kind switch
        {
            SectionLayoutKind.Grid => GridColumns(section),
            SectionLayoutKind.Flex => FlexTracks,
            _ => 1,
        };
    }

    /// <summary>Maps one legacy field placement within its section to a Layout placement.</summary>
    /// <param name="section">The legacy section the field sits in.</param>
    /// <param name="legacy">The legacy field placement.</param>
    /// <returns>The equivalent Layout placement.</returns>
    /// <exception cref="LayoutDefinitionAdmissionException">A legacy number the section uses is fractional or out of range.</exception>
    public static LayoutPlacement Migrate(SectionLayout section, FieldPlacement legacy)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(legacy);

        var refusals = new List<LayoutDefinitionRefusal>();
        var placement = section.Kind switch
        {
            // Width and grow were flex-only: a grid keeps the span exactly as its own count read it.
            SectionLayoutKind.Grid => new LayoutPlacement(
                Span: LegacyInteger(legacy.ColSpan, new LayoutNumericRange(1, GridColumns(section)), "/col_span", refusals)),
            // col_span was grid-only.
            SectionLayoutKind.Flex => WithLegacyWidth(
                new LayoutPlacement(Width: LayoutSizing.Hug,
                    Grow: LegacyInteger(legacy.Grow, LayoutDefinitionSchema.Numeric(LayoutNumericMember.Grow), "/grow", refusals) ?? 0),
                legacy.Width.HasValue ? legacy.Width.Value : FieldWidth.Auto),
            // A stack placed neither spans nor widths.
            _ => new LayoutPlacement(),
        };
        if (refusals.Count > 0) throw new LayoutDefinitionAdmissionException(Stage, refusals.AsReadOnly());
        return placement;
    }

    /// <summary>
    /// Maps one legacy Forms table column (its width and cell alignment tokens) to the placement of
    /// that column's cell in a Layout repeating block, so the tabular widths survive (layout-eng-9).
    /// </summary>
    /// <param name="width">The legacy width token; <see langword="null"/> is <c>auto</c>.</param>
    /// <param name="align">The legacy alignment token; <see langword="null"/> is <c>start</c>, as the legacy renderer drew it.</param>
    /// <returns>The equivalent Layout placement.</returns>
    /// <exception cref="LayoutDefinitionAdmissionException">A token is not one the legacy contract admitted.</exception>
    public static LayoutPlacement MigrateColumn(string? width, string? align)
    {
        var refusals = new List<LayoutDefinitionRefusal>();
        if (!WidthTokens.TryGetValue(width ?? "auto", out var legacyWidth))
            refusals.Add(new(LayoutDefinitionCodes.PlacementTokenUnknown, "/width"));
        // Stated explicitly: an absent Layout alignment inherits the container's stretch.
        if (!AlignTokens.TryGetValue(align ?? "start", out var justify))
            refusals.Add(new(LayoutDefinitionCodes.PlacementTokenUnknown, "/align"));
        if (refusals.Count > 0) throw new LayoutDefinitionAdmissionException(Stage, refusals.AsReadOnly());

        return WithLegacyWidth(new LayoutPlacement(Width: LayoutSizing.Hug, JustifySelf: justify), legacyWidth);
    }

    private static int GridColumns(SectionLayout section)
    {
        var refusals = new List<LayoutDefinitionRefusal>();
        var columns = LegacyInteger(section.Columns, LayoutDefinitionSchema.Numeric(LayoutNumericMember.ColumnCount), "/columns", refusals);
        if (refusals.Count > 0) throw new LayoutDefinitionAdmissionException(Stage, refusals.AsReadOnly());
        return columns ?? LegacyGridColumns;
    }

    private static LayoutPlacement WithLegacyWidth(LayoutPlacement placement, FieldWidth width) => width switch
    {
        FieldWidth.OneQuarter => placement with { Span = 3 },
        FieldWidth.OneThird => placement with { Span = 4 },
        FieldWidth.OneHalf => placement with { Span = 6 },
        FieldWidth.TwoThirds => placement with { Span = 8 },
        FieldWidth.ThreeQuarters => placement with { Span = 9 },
        // `full` is fill and nothing else: it writes no span.
        FieldWidth.Full => placement with { Width = LayoutSizing.Fill },
        _ => placement,
    };

    private static int? LegacyInteger(
        Optional<decimal> legacy,
        LayoutNumericRange range,
        string pointer,
        ICollection<LayoutDefinitionRefusal> refusals)
    {
        if (!legacy.HasValue) return null;
        var value = legacy.Value;
        if (value == decimal.Truncate(value)
            && value is >= int.MinValue and <= int.MaxValue
            && range.Contains((int)value))
            return (int)value;

        refusals.Add(new(LayoutDefinitionCodes.NumericOutOfRange, pointer));
        return null;
    }
}
