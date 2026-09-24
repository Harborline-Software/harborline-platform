using Harborline.Contracts.Forms;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// DES-0052 layout-ck-41 — migrates a legacy Forms field placement onto Layout's twelve tracks,
/// the smallest track count divisible by both three and four.
/// </summary>
public static class LayoutLegacyWidthMigration
{
    private const string Stage = "definition.migrate";

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
        var width = legacy.Width.HasValue ? legacy.Width.Value : FieldWidth.Auto;
        return width switch
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
    }

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
