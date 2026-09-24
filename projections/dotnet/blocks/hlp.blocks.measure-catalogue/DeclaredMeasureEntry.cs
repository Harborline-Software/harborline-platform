using System.Runtime.CompilerServices;
using Harborline.Blocks.Aggregates;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.MeasureCatalogue;

/// <summary>
/// A configurer-authored entry. It adds no math: it hands the caller's narrowed rows and the
/// composed filter to the shipped aggregates evaluator and returns its typed groups unchanged.
/// </summary>
public sealed class DeclaredMeasureEntry : IMeasureEntry
{
    private readonly AggregateDefinition _definition;
    private readonly AggregateDefinitionValidator _validator;
    private readonly AggregateHostBounds _bounds;

    /// <summary>Binds one published definition revision to one catalogue address.</summary>
    /// <param name="reference">The address consumers hold.</param>
    /// <param name="definition">A published definition revision.</param>
    /// <param name="operation">The Access operation for this entry's rows.</param>
    /// <param name="recordKind">The Access record kind for this entry's rows.</param>
    /// <param name="validator">The shipped definition validator.</param>
    /// <param name="bounds">Effective host bounds.</param>
    public DeclaredMeasureEntry(MeasureRef reference, AggregateDefinition definition, string operation,
        string recordKind, AggregateDefinitionValidator validator, AggregateHostBounds bounds)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        RecordKind = recordKind ?? throw new ArgumentNullException(nameof(recordKind));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
    }

    /// <inheritdoc />
    public MeasureRef Reference { get; }
    /// <inheritdoc />
    public string Operation { get; }
    /// <inheritdoc />
    public string RecordKind { get; }
    /// <inheritdoc />
    public AggregateFilter? AuthoredFilter => _definition.Filter;
    /// <inheritdoc />
    public IReadOnlyList<string> ParameterNames { get; } = Array.Empty<string>();

    /// <inheritdoc />
    public async ValueTask<MeasureResult> EvaluateAsync(MeasureEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.Basis is not null)
        {
            throw new MeasureException(MeasureCodes.BasisUnsupported, $"/measures/{Reference.Value}/rows",
                "A declared entry reads the rows the caller supplied, not a foreign basis.");
        }
        var engine = new AggregateEngine(
            new SuppliedRowSource(evaluation.Rows, evaluation.BasisToken),
            new BoundFieldAuthorization(evaluation),
            _validator,
            _bounds);
        var result = await engine
            .EvaluateAsync(new AggregateExecutionContext(evaluation.Tenant, evaluation.Principal),
                _definition with { Filter = evaluation.Filtering }, cancellationToken)
            .ConfigureAwait(false);
        return new MeasureResult(Reference, evaluation.BasisToken, result.Groups);
    }

    // The rows are the caller's; the catalogue already narrowed them through the Access filter, so
    // this source only streams them. It opens nothing of its own.
    private sealed class SuppliedRowSource(IReadOnlyList<MeasureRow> rows, string token) : IAggregateRowSource
    {
        public ValueTask<AggregateRowSnapshot> OpenSnapshotAsync(AggregateExecutionContext context, string sourceRef,
            IReadOnlySet<string> requiredFields, AggregateBounds bounds, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AggregateRowSnapshot(token, Stream(cancellationToken)));

        private async IAsyncEnumerable<AggregateRow> Stream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new AggregateRow(row.Fields);
                await Task.Yield();
            }
        }
    }

    // Source-field admission is not row visibility, which the catalogue has already applied. It
    // still goes through the same bound Access predicate rather than an allow-all double, so a
    // principal who may not read the source is refused before enumeration.
    private sealed class BoundFieldAuthorization(MeasureEvaluation evaluation) : IAggregateAuthorization
    {
        public async ValueTask<bool> AuthorizeAsync(AggregateExecutionContext context, string sourceRef,
            IReadOnlySet<string> requiredFields, CancellationToken cancellationToken)
        {
            // The predicate refuses a record whose kind differs from the kind it was bound to, so
            // the source admission check reuses that same kind with the source as the record.
            var record = new AccessRecord(evaluation.Tenant, evaluation.RecordKind, sourceRef,
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.Ordinal));
            return (await evaluation.Filter.CheckAsync(record, cancellationToken).ConfigureAwait(false)).Allowed;
        }
    }
}
