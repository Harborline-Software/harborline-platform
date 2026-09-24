using Harborline.Contracts.Forms;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// DES-0052 layout-ck-41 — migrates a legacy Forms field placement onto Layout's twelve tracks,
/// the smallest track count divisible by both three and four.
/// </summary>
public static class LayoutLegacyWidthMigration
{
    private const string Stage = "definition.migrate";

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

    /// <summary>Maps one legacy placement to its Layout placement.</summary>
    /// <param name="legacy">The legacy Forms placement.</param>
    /// <returns>The equivalent Layout placement.</returns>
    /// <exception cref="LayoutDefinitionAdmissionException">
    /// A legacy number is fractional or outside Layout's range. The legacy contract never bounded
    /// these, so an invalid value is refused rather than normalised.
    /// </exception>
    public static LayoutPlacement Migrate(FieldPlacement legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        var refusals = new List<LayoutDefinitionRefusal>();
        var span = Numeric(legacy.ColSpan, LayoutNumericMember.Span, "/col_span", refusals);
        var grow = Numeric(legacy.Grow, LayoutNumericMember.Grow, "/grow", refusals);
        if (refusals.Count > 0) throw new LayoutDefinitionAdmissionException(Stage, refusals.AsReadOnly());

        var placement = new LayoutPlacement(Width: LayoutSizing.Hug, Span: span ?? 1, Grow: grow ?? 0);
        return Apply(placement, legacy.Width.HasValue ? legacy.Width.Value : FieldWidth.Auto);
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

        return Apply(new LayoutPlacement(Width: LayoutSizing.Hug, JustifySelf: justify), legacyWidth);
    }

    private static LayoutPlacement Apply(LayoutPlacement placement, FieldWidth width) => width switch
    {
        FieldWidth.OneQuarter => placement with { Span = 3 },
        FieldWidth.OneThird => placement with { Span = 4 },
        FieldWidth.OneHalf => placement with { Span = 6 },
        FieldWidth.TwoThirds => placement with { Span = 8 },
        FieldWidth.ThreeQuarters => placement with { Span = 9 },
        // `full` is fill and nothing else: a twelve-track span beside it would contradict it.
        FieldWidth.Full => placement with { Width = LayoutSizing.Fill, Span = 1 },
        _ => placement,
    };

    private static int? Numeric(
        Optional<decimal> legacy,
        LayoutNumericMember member,
        string pointer,
        ICollection<LayoutDefinitionRefusal> refusals)
    {
        if (!legacy.HasValue) return null;
        var value = legacy.Value;
        if (value == decimal.Truncate(value)
            && value is >= int.MinValue and <= int.MaxValue
            && LayoutDefinitionSchema.Numeric(member).Contains((int)value))
            return (int)value;

        refusals.Add(new(LayoutDefinitionCodes.NumericOutOfRange, pointer));
        return null;
    }
}
