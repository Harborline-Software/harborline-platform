using Harborline.Blocks.Aggregates;
using Harborline.Blocks.MeasureCatalogue;

namespace Harborline.Blocks.EntityViews;

/// <summary>
/// Adapts the Views measure seam to the shared measure catalogue. It translates shapes and nothing
/// else: no aggregation, no resolution of its own, and no branch on whether the entry behind a
/// reference is declared or written in code.
/// </summary>
public sealed class CatalogueViewMeasures(IMeasureCatalogue catalogue) : IViewMeasureCatalog
{
    private readonly IMeasureCatalogue _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));

    /// <summary>Resolves a measure by the one public reference. A malformed address is unknown.</summary>
    public async ValueTask<ViewMeasureDescriptor?> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!MeasureRef.TryParse(name, out var reference)) return null;
        var descriptor = await _catalogue.ResolveAsync(reference!, cancellationToken).ConfigureAwait(false);
        return descriptor is null ? null : new ViewMeasureDescriptor(descriptor.Reference.Value, descriptor.ParameterNames);
    }

    /// <summary>
    /// Evaluates over the rows this refresh is currently showing, at this refresh's instant. The
    /// catalogue narrows those rows through the production Access filter before it computes.
    /// An unknown reference refuses with a stable code and pointer before any row is read.
    /// </summary>
    public async ValueTask<ViewMeasureResult> EvaluateAsync(ViewMeasureBinding binding, IReadOnlyList<ViewRow> rows,
        DateTimeOffset evaluatedAt, string tenant, string principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(rows);
        var reference = new MeasureRef(binding.Name);
        // The projection is deferred, so a refusal the catalogue raises on resolution or admission
        // still happens before anything walks the caller's rows.
        var result = await _catalogue.EvaluateAsync(reference, new MeasureRequest(tenant, principal,
            new SuppliedRows(new ProjectedRows(rows)), evaluatedAt, binding.Parameters), cancellationToken).ConfigureAwait(false);
        return new ViewMeasureResult(binding.Name, Headline(result));
    }

    private sealed class ProjectedRows(IReadOnlyList<ViewRow> rows) : IReadOnlyList<MeasureRow>
    {
        public MeasureRow this[int index] => Row(rows[index]);

        public int Count => rows.Count;

        public IEnumerator<MeasureRow> GetEnumerator()
        {
            foreach (var row in rows) yield return Row(row);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static MeasureRow Row(ViewRow row) =>
        new(row.Id, row.Values.ToDictionary(pair => pair.Key, pair => Value(pair.Value), StringComparer.Ordinal));

    // Views carries untyped record values; the declared entry's source contract is what decides the
    // measure's types, so the mapping keeps the value and lets that contract refuse a mismatch.
    private static AggregateValue Value(object? value) => value switch
    {
        null => AggregateValue.Null(AggregateValueType.String),
        bool item => new AggregateValue(AggregateValueType.Boolean, item),
        int item => new AggregateValue(AggregateValueType.Integer, (long)item),
        long item => new AggregateValue(AggregateValueType.Integer, item),
        double item => new AggregateValue(AggregateValueType.Number, item),
        decimal item => new AggregateValue(AggregateValueType.Decimal, CanonicalDecimal.Parse(item.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        DateOnly item => new AggregateValue(AggregateValueType.Date, item),
        DateTimeOffset item => new AggregateValue(AggregateValueType.DateTime, item),
        _ => new AggregateValue(AggregateValueType.String, value.ToString() ?? string.Empty),
    };

    // A view shows one headline figure. Null and unavailable cells stay absent rather than zero.
    private static object? Headline(MeasureResult result)
    {
        var group = result.Groups.FirstOrDefault(item => item.Kind == AggregateGroupKind.GrandTotal)
            ?? result.Groups.FirstOrDefault();
        var cell = group?.Measures.FirstOrDefault();
        return cell?.State == AggregateCellState.Value ? cell.Value : null;
    }
}
