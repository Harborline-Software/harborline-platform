using System.Runtime.CompilerServices;
using Harborline.Blocks.Aggregates;

namespace Harborline.Blocks.Aggregates.Tests;

internal static class TestFixture
{
    internal static AggregateDefinition Definition(
        IReadOnlyDictionary<string, AggregateValueType>? fields = null,
        IReadOnlyList<AggregateDimension>? grouping = null,
        IReadOnlyList<AggregateMeasure>? measures = null,
        AggregateFilter? filter = null,
        AggregateTotals? totals = null,
        AggregateBounds? bounds = null) => new(1, "test", 1, AggregateDefinitionStatus.Published, "Test",
            new AggregateSourceDefinition("test:rows/v1", fields ?? new Dictionary<string, AggregateValueType> { ["value"] = AggregateValueType.Decimal }),
            filter, grouping ?? Array.Empty<AggregateDimension>(), measures ?? [new("sum", AggregateOperator.Sum, "value", AggregateValueType.Decimal, AggregateValueType.Decimal)],
            totals ?? new AggregateTotals([], true), bounds ?? new AggregateBounds(100, 100, 100));

    internal static AggregateRow Row(params (string Key, AggregateValue Value)[] fields) => new(fields.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    internal static AggregateValue V(AggregateValueType type, object value) => new(type, value);
    internal static async Task<AggregateResult> Evaluate(AggregateDefinition definition, params AggregateRow[] rows)
    {
        var source = new Source(rows);
        var engine = new AggregateEngine(source, new Authorization(), new AggregateDefinitionValidator(), new AggregateHostBounds(1000, 1000, 1000));
        return await engine.EvaluateAsync(new AggregateExecutionContext("tenant", "actor"), definition);
    }

    internal sealed class Source(IReadOnlyList<AggregateRow> rows) : IAggregateRowSource
    {
        internal int Opens { get; private set; }
        public ValueTask<AggregateRowSnapshot> OpenSnapshotAsync(AggregateExecutionContext context, string sourceRef, IReadOnlySet<string> requiredFields, AggregateBounds bounds, CancellationToken cancellationToken)
        { Opens++; return ValueTask.FromResult(new AggregateRowSnapshot("snapshot-1", Stream(rows, cancellationToken))); }
        private static async IAsyncEnumerable<AggregateRow> Stream(IReadOnlyList<AggregateRow> values, [EnumeratorCancellation] CancellationToken cancellationToken)
        { foreach (var row in values) { cancellationToken.ThrowIfCancellationRequested(); yield return row; await Task.Yield(); } }
    }

    internal sealed class Authorization(bool allowed = true, Action? observed = null) : IAggregateAuthorization
    {
        public ValueTask<bool> AuthorizeAsync(AggregateExecutionContext context, string sourceRef, IReadOnlySet<string> requiredFields, CancellationToken cancellationToken)
        { observed?.Invoke(); return ValueTask.FromResult(allowed); }
    }
}
