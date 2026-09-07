using System.Globalization;

namespace Harborline.Blocks.Aggregates;

/// <summary>Standalone deterministic aggregate evaluator.</summary>
public sealed class AggregateEngine
{
    private readonly IAggregateRowSource rowSource;
    private readonly IAggregateAuthorization authorization;
    private readonly AggregateDefinitionValidator validator;
    private readonly AggregateHostBounds hostBounds;

    /// <summary>Creates an evaluator.</summary><param name="rowSource">Logical row source.</param><param name="authorization">Field authorization policy.</param><param name="validator">Definition validator.</param><param name="hostBounds">Host maximum limits.</param>
    public AggregateEngine(IAggregateRowSource rowSource, IAggregateAuthorization authorization, AggregateDefinitionValidator validator, AggregateHostBounds hostBounds)
    {
        this.rowSource = rowSource ?? throw new ArgumentNullException(nameof(rowSource));
        this.authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.hostBounds = hostBounds ?? throw new ArgumentNullException(nameof(hostBounds));
    }

    /// <summary>Evaluates one published definition against one authorized snapshot.</summary><param name="context">Trusted execution context.</param><param name="definition">Published definition revision.</param><param name="cancellationToken">Cancellation token.</param><returns>Deterministically ordered result.</returns>
    public async ValueTask<AggregateResult> EvaluateAsync(AggregateExecutionContext context, AggregateDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Status != AggregateDefinitionStatus.Published) throw new AggregateException("aggregates.definition.invalid", "Only published revisions may be evaluated.");
        validator.Validate(definition, hostBounds);
        var required = RequiredFields(definition);
        if (!await authorization.AuthorizeAsync(context, definition.Source.SourceRef, required, cancellationToken).ConfigureAwait(false)) throw new AggregateException("aggregates.source.forbidden", "The actor may not evaluate the required source fields.");
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await rowSource.OpenSnapshotAsync(context, definition.Source.SourceRef, required, definition.Bounds, cancellationToken).ConfigureAwait(false);
        var rows = new List<IReadOnlyDictionary<string, AggregateValue>>();
        await foreach (var sourceRow in snapshot.Rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Count == definition.Bounds.MaxInputRows) throw new AggregateException("aggregates.evaluation.input_limit", "Input row limit exceeded.");
            var row = CanonicalizeRow(definition.Source.Fields, required, sourceRow);
            if (definition.Filter is null || Matches(definition.Filter, row)) rows.Add(row);
        }

        var plans = BuildPlans(definition, rows);
        if (plans.Count > definition.Bounds.MaxGroups) throw new AggregateException("aggregates.evaluation.group_limit", "Group limit exceeded.");
        if ((long)plans.Count * definition.Measures.Count > definition.Bounds.MaxResultCells) throw new AggregateException("aggregates.evaluation.cell_limit", "Result cell limit exceeded.");
        plans.Sort(new GroupPlanComparer(definition.Grouping));
        var groups = plans.Select(plan => new AggregateGroup(plan.Kind, plan.Level, Keys(definition, plan), FoldAll(definition.Measures, plan.Rows))).ToArray();
        return new AggregateResult(definition.DefinitionId, definition.Revision, snapshot.Token, groups);
    }

    private static IReadOnlySet<string> RequiredFields(AggregateDefinition definition)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in definition.Grouping) result.Add(item.Field);
        foreach (var item in definition.Measures) if (item.Field is not null) result.Add(item.Field);
        if (definition.Filter is not null) AddFilterFields(definition.Filter, result);
        return result;
    }

    private static void AddFilterFields(AggregateFilter filter, HashSet<string> fields)
    {
        switch (filter)
        {
            case AggregateAllFilter all: foreach (var child in all.Filters) AddFilterFields(child, fields); break;
            case AggregateAnyFilter any: foreach (var child in any.Filters) AddFilterFields(child, fields); break;
            case AggregateNotFilter not: AddFilterFields(not.Filter, fields); break;
            case AggregateComparisonFilter comparison: fields.Add(comparison.Field); break;
        }
    }

    private static IReadOnlyDictionary<string, AggregateValue> CanonicalizeRow(IReadOnlyDictionary<string, AggregateValueType> contract, IReadOnlySet<string> required, AggregateRow row)
    {
        var result = new Dictionary<string, AggregateValue>(StringComparer.Ordinal);
        foreach (var field in required)
        {
            if (!contract.TryGetValue(field, out var type) || !row.Fields.TryGetValue(field, out var value)) throw new AggregateException("aggregates.source.contract_mismatch", $"Required field '{field}' is missing.");
            if (value.Type != type) throw new AggregateException("aggregates.measure.type_mismatch", $"Field '{field}' has the wrong declared type.");
            result[field] = value.State == AggregateCellState.Value ? value with { Value = Canonicalize(type, value.Value) } : value with { Value = null };
        }
        return result;
    }

    private static object Canonicalize(AggregateValueType type, object? value)
    {
        if (value is null) throw new AggregateException("aggregates.source.contract_mismatch", "A value-state field cannot contain null.");
        try
        {
            return type switch
            {
                AggregateValueType.String when value is string text => text,
                AggregateValueType.Boolean when value is bool boolean => boolean,
                AggregateValueType.Integer when value is long integer => integer,
                AggregateValueType.Integer when value is int integer => (long)integer,
                AggregateValueType.Number when value is double number && double.IsFinite(number) => number,
                AggregateValueType.Number when value is float number && float.IsFinite(number) => (double)number,
                AggregateValueType.Decimal when value is CanonicalDecimal number => number,
                AggregateValueType.Decimal when value is string text => CanonicalDecimal.Parse(text),
                AggregateValueType.Date when value is DateOnly date => date,
                AggregateValueType.Date when value is string text => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                AggregateValueType.DateTime when value is DateTimeOffset instant && instant.Offset == TimeSpan.Zero => instant,
                AggregateValueType.DateTime when value is string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                _ => throw new FormatException(),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new AggregateException("aggregates.source.contract_mismatch", $"Value does not match declared type '{type}'.");
        }
    }

    private static bool Matches(AggregateFilter filter, IReadOnlyDictionary<string, AggregateValue> row) => filter switch
    {
        AggregateAllFilter all => all.Filters.All(child => Matches(child, row)),
        AggregateAnyFilter any => any.Filters.Any(child => Matches(child, row)),
        AggregateNotFilter not => !Matches(not.Filter, row),
        AggregateComparisonFilter comparison => Compare(comparison, row[comparison.Field]),
        _ => false,
    };

    private static bool Compare(AggregateComparisonFilter filter, AggregateValue actual)
    {
        if (filter.Operator == AggregateComparisonOperator.IsNull) return actual.State == AggregateCellState.Null;
        if (filter.Operator == AggregateComparisonOperator.IsNotNull) return actual.State != AggregateCellState.Null;
        if (actual.State != AggregateCellState.Value) return false;
        if (filter.Operator == AggregateComparisonOperator.In) return filter.Values!.Any(value => CompareValues(actual.Type, actual.Value!, Canonicalize(value.Type, value.Value!)) == 0);
        var comparison = CompareValues(actual.Type, actual.Value!, Canonicalize(filter.Value!.Type, filter.Value.Value!));
        return filter.Operator switch { AggregateComparisonOperator.Eq => comparison == 0, AggregateComparisonOperator.Neq => comparison != 0, AggregateComparisonOperator.Lt => comparison < 0, AggregateComparisonOperator.Lte => comparison <= 0, AggregateComparisonOperator.Gt => comparison > 0, AggregateComparisonOperator.Gte => comparison >= 0, _ => false };
    }

    private static List<GroupPlan> BuildPlans(AggregateDefinition definition, IReadOnlyList<IReadOnlyDictionary<string, AggregateValue>> rows)
    {
        var result = new List<GroupPlan>();
        if (definition.Grouping.Count == 0)
        {
            if (rows.Count > 0) result.Add(new GroupPlan(AggregateGroupKind.Detail, 0, Array.Empty<AggregateValue>(), rows));
        }
        else result.AddRange(GroupAt(definition, rows, definition.Grouping.Count, AggregateGroupKind.Detail));
        foreach (var depth in definition.Totals.Subtotals.OrderBy(x => x)) result.AddRange(GroupAt(definition, rows, depth, AggregateGroupKind.Subtotal));
        if (definition.Totals.GrandTotal) result.Add(new GroupPlan(AggregateGroupKind.GrandTotal, 0, Array.Empty<AggregateValue>(), rows));
        return result;
    }

    private static IEnumerable<GroupPlan> GroupAt(AggregateDefinition definition, IReadOnlyList<IReadOnlyDictionary<string, AggregateValue>> rows, int depth, AggregateGroupKind kind)
    {
        var groups = new Dictionary<string, (AggregateValue[] Keys, List<IReadOnlyDictionary<string, AggregateValue>> Rows)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var keys = new AggregateValue[depth];
            var excluded = false;
            for (var index = 0; index < depth; index++)
            {
                var dimension = definition.Grouping[index];
                var value = row[dimension.Field];
                if (value.State == AggregateCellState.Unavailable) throw new AggregateException("aggregates.source.contract_mismatch", "A grouping key is unavailable.");
                if (value.State == AggregateCellState.Null && dimension.Nulls == AggregateDimensionNullPolicy.Exclude) { excluded = true; break; }
                keys[index] = value;
            }
            if (excluded) continue;
            var id = string.Join("|", keys.Select(KeyBytes));
            if (!groups.TryGetValue(id, out var group)) groups[id] = group = (keys, new());
            group.Rows.Add(row);
        }
        return groups.Values.Select(group => new GroupPlan(kind, depth, group.Keys, group.Rows));
    }

    private static IReadOnlyList<AggregateKey> Keys(AggregateDefinition definition, GroupPlan plan) => plan.Keys.Select((value, index) => new AggregateKey(definition.Grouping[index].Key, value.Type, value.State == AggregateCellState.Null ? null : WireValue(value))).ToArray();

    private static IReadOnlyList<AggregateCell> FoldAll(IReadOnlyList<AggregateMeasure> measures, IReadOnlyList<IReadOnlyDictionary<string, AggregateValue>> rows) => measures.Select(measure => Fold(measure, rows)).ToArray();

    private static AggregateCell Fold(AggregateMeasure measure, IReadOnlyList<IReadOnlyDictionary<string, AggregateValue>> rows)
    {
        if (measure.Operator == AggregateOperator.Count && measure.Count == AggregateCountMode.Rows) return new AggregateCell(measure.Key, AggregateValueType.Integer, AggregateCellState.Value, (long)rows.Count);
        var values = rows.Select(row => row[measure.Field!]).ToArray();
        if (values.Any(value => value.State == AggregateCellState.Unavailable))
        {
            if (measure.Unavailable == AggregateUnavailablePolicy.Propagate) return new AggregateCell(measure.Key, measure.ResultType, AggregateCellState.Unavailable, null);
            throw new AggregateException("aggregates.source.contract_mismatch", $"Measure '{measure.Key}' received an unavailable value.");
        }
        if (measure.Nulls == AggregateNullPolicy.Propagate && values.Any(value => value.State == AggregateCellState.Null)) return new AggregateCell(measure.Key, measure.ResultType, AggregateCellState.Null, null);
        var concrete = values.Where(value => value.State == AggregateCellState.Value).Select(value => value.Value!).ToArray();
        if (measure.Operator == AggregateOperator.Count) return new AggregateCell(measure.Key, AggregateValueType.Integer, AggregateCellState.Value, (long)concrete.Length);
        if (concrete.Length == 0)
        {
            if (measure.Operator == AggregateOperator.Sum) return new AggregateCell(measure.Key, measure.ResultType, AggregateCellState.Value, Zero(measure.ResultType));
            return new AggregateCell(measure.Key, measure.ResultType, AggregateCellState.Null, null);
        }
        object result = measure.Operator switch
        {
            AggregateOperator.Sum => Sum(measure.ResultType, concrete),
            AggregateOperator.Average => Average(measure.InputType!.Value, concrete),
            AggregateOperator.Min => concrete.MinBy(value => value, new TypedComparer(measure.ResultType))!,
            AggregateOperator.Max => concrete.MaxBy(value => value, new TypedComparer(measure.ResultType))!,
            _ => throw new AggregateException("aggregates.definition.invalid", "Unknown measure operator."),
        };
        return new AggregateCell(measure.Key, measure.ResultType, AggregateCellState.Value, WireValue(new AggregateValue(measure.ResultType, result)));
    }

    private static object Sum(AggregateValueType type, object[] values) => type switch
    {
        AggregateValueType.Integer => values.Cast<long>().Aggregate(0L, checked((left, right) => left + right)),
        AggregateValueType.Number => values.Cast<double>().Sum(),
        AggregateValueType.Decimal => values.Cast<CanonicalDecimal>().Aggregate(new CanonicalDecimal(0, 0), (left, right) => left + right),
        _ => throw new AggregateException("aggregates.measure.type_mismatch", "Sum requires numeric values."),
    };

    private static object Average(AggregateValueType type, object[] values) => type switch
    {
        AggregateValueType.Integer => values.Cast<long>().Average(),
        AggregateValueType.Number => values.Cast<double>().Average(),
        AggregateValueType.Decimal => ((CanonicalDecimal)Sum(type, values)).DivideExactly(values.Length),
        _ => throw new AggregateException("aggregates.measure.type_mismatch", "Average requires numeric values."),
    };

    private static object Zero(AggregateValueType type) => type switch { AggregateValueType.Integer => 0L, AggregateValueType.Number => 0d, AggregateValueType.Decimal => new CanonicalDecimal(0, 0), _ => throw new AggregateException("aggregates.measure.type_mismatch", "Sum requires numeric values.") };
    private static object? WireValue(AggregateValue value) => value.State != AggregateCellState.Value ? null : value.Type switch { AggregateValueType.Decimal => ((CanonicalDecimal)value.Value!).ToString(), AggregateValueType.Date => ((DateOnly)value.Value!).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), AggregateValueType.DateTime => ((DateTimeOffset)value.Value!).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture), _ => value.Value };
    private static string KeyBytes(AggregateValue value) => value.State == AggregateCellState.Null ? $"{value.Type}:null" : $"{value.Type}:{WireValue(value)}";

    private static int CompareValues(AggregateValueType type, object left, object right) => type switch
    {
        AggregateValueType.String => StringComparer.Ordinal.Compare((string)left, (string)right), AggregateValueType.Boolean => ((bool)left).CompareTo((bool)right), AggregateValueType.Integer => ((long)left).CompareTo((long)right), AggregateValueType.Number => ((double)left).CompareTo((double)right), AggregateValueType.Decimal => ((CanonicalDecimal)left).CompareTo((CanonicalDecimal)right), AggregateValueType.Date => ((DateOnly)left).CompareTo((DateOnly)right), AggregateValueType.DateTime => ((DateTimeOffset)left).CompareTo((DateTimeOffset)right), _ => 0,
    };

    private sealed record GroupPlan(AggregateGroupKind Kind, int Level, IReadOnlyList<AggregateValue> Keys, IReadOnlyList<IReadOnlyDictionary<string, AggregateValue>> Rows);
    private sealed class TypedComparer(AggregateValueType type) : IComparer<object> { public int Compare(object? x, object? y) => CompareValues(type, x!, y!); }
    private sealed class GroupPlanComparer(IReadOnlyList<AggregateDimension> dimensions) : IComparer<GroupPlan>
    {
        public int Compare(GroupPlan? x, GroupPlan? y)
        {
            if (x!.Kind == AggregateGroupKind.GrandTotal) return y!.Kind == AggregateGroupKind.GrandTotal ? 0 : 1;
            if (y!.Kind == AggregateGroupKind.GrandTotal) return -1;
            var common = Math.Min(x.Keys.Count, y.Keys.Count);
            for (var index = 0; index < common; index++)
            {
                var left = x.Keys[index]; var right = y.Keys[index];
                var comparison = left.State == AggregateCellState.Null ? (right.State == AggregateCellState.Null ? 0 : -1) : right.State == AggregateCellState.Null ? 1 : CompareValues(left.Type, left.Value!, right.Value!);
                if (comparison != 0) return dimensions[index].Direction == AggregateSortDirection.Asc ? comparison : -comparison;
            }
            if (x.Keys.Count != y.Keys.Count) return y.Keys.Count.CompareTo(x.Keys.Count);
            return StringComparer.Ordinal.Compare(string.Join("|", x.Keys.Select(KeyBytes)), string.Join("|", y.Keys.Select(KeyBytes)));
        }
    }
}
