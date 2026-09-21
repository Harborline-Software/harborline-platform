using Harborline.Blocks.Aggregates;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.MeasureCatalogue.Tests;

// Every fixture here binds the PRODUCTION AccessProvider. The only double is the host's own gate
// adapter, which is the seam Access is designed to sit on; there is no allow-all filter anywhere.
internal static class CatalogueFixture
{
    internal const string Tenant = "tenant-a";
    internal const string Principal = "alice";
    internal const string Operation = "records:read";
    internal const string Kind = "work";
    internal const string SourceRef = "test:rows/v1";

    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    internal static AggregateDefinition Definition(AggregateFilter? filter = null, AggregateMeasure[]? measures = null) =>
        new(1, "declared", 1, AggregateDefinitionStatus.Published, "Declared",
            new AggregateSourceDefinition(SourceRef, new Dictionary<string, AggregateValueType>(StringComparer.Ordinal)
            {
                ["value"] = AggregateValueType.Decimal,
                ["bucket"] = AggregateValueType.String,
            }),
            filter,
            Array.Empty<AggregateDimension>(),
            measures ??
            [
                new("count", AggregateOperator.Count, null, null, AggregateValueType.Integer, Count: AggregateCountMode.Rows),
                new("sum", AggregateOperator.Sum, "value", AggregateValueType.Decimal, AggregateValueType.Decimal),
            ],
            new AggregateTotals([], true),
            new AggregateBounds(100, 100, 100));

    internal static DeclaredMeasureEntry Declared(MeasureRef reference, AggregateDefinition? definition = null) =>
        new(reference, definition ?? Definition(), Operation, Kind, new AggregateDefinitionValidator(), new AggregateHostBounds(1000, 1000, 1000));

    internal static MeasureRow Row(string id, string bucket, decimal value) =>
        new(id, new Dictionary<string, AggregateValue>(StringComparer.Ordinal)
        {
            ["value"] = new(AggregateValueType.Decimal, CanonicalDecimal.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ["bucket"] = new(AggregateValueType.String, bucket),
        });

    internal static MeasureRow Row(string id, string bucket, AggregateValue value) =>
        new(id, new Dictionary<string, AggregateValue>(StringComparer.Ordinal)
        {
            ["value"] = value,
            ["bucket"] = new(AggregateValueType.String, bucket),
        });

    internal static AggregateFilter Bucket(string bucket) =>
        new AggregateComparisonFilter("bucket", AggregateComparisonOperator.Eq, new AggregateValue(AggregateValueType.String, bucket));

    internal static AggregateCell Cell(MeasureResult result, string key) =>
        result.Groups.Single(group => group.Kind == AggregateGroupKind.GrandTotal).Measures.Single(cell => cell.Key == key);

    internal static MeasureRequest Request(IReadOnlyList<MeasureRow> rows, DateTimeOffset? at = null,
        AggregateFilter? narrow = null, MeasurePage? page = null) =>
        new(Tenant, Principal, new SuppliedRows(rows), at ?? Now, null, narrow, page);

    // The host's sole authorization decider. It hides exactly the record ids it is told to hide and
    // decides nothing else; the verdict shape is the gate's, not the catalogue's.
    internal sealed class HostGate(params string[] hidden) : IAuthorizationDecider
    {
        private readonly HashSet<string> _hidden = new(hidden, StringComparer.Ordinal);

        internal List<DateTimeOffset> Instants { get; } = [];

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Instants.Add(request.At);
            var allowed = !_hidden.Contains(request.Record.Id);
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, allowed,
                allowed ? "None" : "record_not_visible", "grant:1", [], []));
        }
    }
}
