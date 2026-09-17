namespace Harborline.Blocks.EntityViews;

/// <summary>
/// Reference row-source adapter that preserves the plan's security order before counting or paging.
/// Hosts may translate the same plan into their durable query substrate.
/// </summary>
public sealed class InMemoryViewRowSource : IViewRowSource
{
    private readonly IReadOnlyList<ViewRow> _rows;
    private readonly IViewExpressionFunctionRegistry _functions;

    public InMemoryViewRowSource(IEnumerable<ViewRow> rows)
        : this(rows, new ViewExpressionFunctionRegistry())
    {
    }

    public InMemoryViewRowSource(
        IEnumerable<ViewRow> rows,
        IViewExpressionFunctionRegistry functions)
    {
        _rows = rows?.ToArray() ?? throw new ArgumentNullException(nameof(rows));
        _functions = functions ?? throw new ArgumentNullException(nameof(functions));
    }

    public ValueTask<ViewRowPage> QueryAsync(
        ViewQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<ViewRow> query = _rows;
        foreach (var predicate in plan.Predicates)
        {
            query = query.Where(row => Matches(row, predicate.Filter, plan.FieldKinds));
        }

        IOrderedEnumerable<ViewRow>? ordered = null;
        foreach (var sort in plan.Sort)
        {
            Func<ViewRow, object?> key = row => Read(row, sort.Field);
            var comparer = new ViewValueComparer(FieldKind(plan.FieldKinds, sort.Field));
            ordered = (ordered, sort.Direction) switch
            {
                (null, ViewSortDirection.Ascending) => query.OrderBy(key, comparer),
                (null, ViewSortDirection.Descending) => query.OrderByDescending(key, comparer),
                (_, ViewSortDirection.Ascending) => ordered.ThenBy(key, comparer),
                (_, ViewSortDirection.Descending) => ordered.ThenByDescending(key, comparer),
                _ => ordered,
            };
        }

        var filtered = (ordered ?? query).ToArray();
        var groups = plan.GroupBy is { } groupBy
            ? filtered
                .GroupBy(row => Value(row, groupBy), StringComparer.Ordinal)
                .Select(group => new ViewGroup(group.Key, group.Count()))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray()
            : [];
        var page = filtered.Skip(plan.Page.Offset).Take(plan.Page.Limit).ToArray();
        return ValueTask.FromResult(new ViewRowPage(page, filtered.Length, groups, filtered));
    }

    private bool Matches(
        ViewRow row,
        ViewFilter filter,
        IReadOnlyDictionary<string, ViewRecordFieldKind> fieldKinds) => filter switch
    {
        ViewComparisonFilter comparison => Compare(
            Read(row, comparison.Field),
            comparison.Operator,
            FromJson(comparison.Value),
            FieldKind(fieldKinds, comparison.Field)),
        ViewAllFilter all => all.Filters.All(item => Matches(row, item, fieldKinds)),
        ViewAnyOfFilter any => any.Filters.Any(item => Matches(row, item, fieldKinds)),
        ViewNotFilter not => !Matches(row, not.Filter, fieldKinds),
        ViewFunctionFilter function => Call(row, function),
        ViewCollectionFilter collection => Quantify(row, collection, fieldKinds),
        _ => throw new ViewQueryException("view.filter.kind_unknown", "The view filter kind is not registered."),
    };

    private bool Quantify(
        ViewRow row,
        ViewCollectionFilter collection,
        IReadOnlyDictionary<string, ViewRecordFieldKind> fieldKinds)
    {
        if (Read(row, collection.Field) is not System.Collections.IEnumerable values
            || values is string)
        {
            return false;
        }
        var matches = values.Cast<object?>()
            .Select(value => Matches(Element(value), collection.Predicate, fieldKinds));
        return collection.Quantifier == ViewCollectionQuantifier.Any
            ? matches.Any(value => value)
            : matches.All(value => value);
    }

    private bool Call(ViewRow row, ViewFunctionFilter function)
    {
        var arguments = function.Arguments.Select(argument => Operand(row, argument)).ToArray();
        return _functions.Evaluate(function.Function, arguments);
    }

    private static object? Operand(ViewRow row, ViewFilterOperand operand) => operand switch
    {
        ViewFieldOperand field => Read(row, field.Field),
        ViewLiteralOperand literal => FromJson(literal.Value),
        _ => throw new ViewQueryException("view.filter.operand_unknown", "The view filter operand is not registered."),
    };

    private static bool Compare(
        object? left,
        ViewComparisonOperator comparison,
        object? right,
        ViewRecordFieldKind? fieldKind)
    {
        if (comparison == ViewComparisonOperator.In && right is object?[] values)
        {
            return values.Any(value => Compare(left, ViewComparisonOperator.Equal, value, fieldKind));
        }
        var order = Order(left, right, fieldKind);
        return comparison switch
        {
            ViewComparisonOperator.Equal => order == 0,
            ViewComparisonOperator.NotEqual => order != 0,
            ViewComparisonOperator.LessThan => order < 0,
            ViewComparisonOperator.LessThanOrEqual => order <= 0,
            ViewComparisonOperator.GreaterThan => order > 0,
            ViewComparisonOperator.GreaterThanOrEqual => order >= 0,
            ViewComparisonOperator.In => false,
            _ => false,
        };
    }

    private static int Order(object? left, object? right, ViewRecordFieldKind? fieldKind)
    {
        if (left is null || right is null)
        {
            return left is null ? right is null ? 0 : -1 : 1;
        }
        if (fieldKind == ViewRecordFieldKind.Text)
        {
            return string.CompareOrdinal(Text(left), Text(right));
        }
        if (fieldKind == ViewRecordFieldKind.DateTime
            && TryDateTime(left, out var leftInstant)
            && TryDateTime(right, out var rightInstant))
        {
            return leftInstant.CompareTo(rightInstant);
        }
        if (fieldKind is null or ViewRecordFieldKind.Ordered or ViewRecordFieldKind.Scalar
            && TryDecimal(left, out var leftNumber)
            && TryDecimal(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }
        if (left is bool leftBoolean && right is bool rightBoolean)
        {
            return leftBoolean.CompareTo(rightBoolean);
        }
        if (left.GetType() == right.GetType() && left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }
        return string.CompareOrdinal(Text(left), Text(right));
    }

    private static bool TryDateTime(object value, out DateTimeOffset instant) => value switch
    {
        DateTimeOffset dateTimeOffset => Return(dateTimeOffset, out instant),
        DateTime dateTime => Return(new DateTimeOffset(dateTime), out instant),
        _ => DateTimeOffset.TryParse(
            Text(value),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out instant),
    };

    private static bool Return(DateTimeOffset value, out DateTimeOffset result)
    {
        result = value;
        return true;
    }

    private static bool TryDecimal(object value, out decimal number) =>
        decimal.TryParse(
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);

    private static string Text(object value) =>
        Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static ViewRecordFieldKind? FieldKind(
        IReadOnlyDictionary<string, ViewRecordFieldKind> fieldKinds,
        string field) =>
        fieldKinds.TryGetValue(field, out var kind) ? kind : null;

    private static object? FromJson(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Null => null,
        System.Text.Json.JsonValueKind.String => value.GetString(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        System.Text.Json.JsonValueKind.Array => value.EnumerateArray().Select(FromJson).ToArray(),
        _ => value.GetRawText(),
    };

    private static object? Read(ViewRow row, string field) =>
        row.Values.TryGetValue(field, out var value) ? value : null;

    private static ViewRow Element(object? value) => value switch
    {
        IReadOnlyDictionary<string, object?> fields => new("$", fields),
        _ => new("$", new Dictionary<string, object?> { ["$"] = value }),
    };

    private static string Value(ViewRow row, string field) =>
        row.Values.TryGetValue(field, out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    private sealed class ViewValueComparer(ViewRecordFieldKind? fieldKind) : IComparer<object?>
    {
        public int Compare(object? left, object? right) => Order(left, right, fieldKind);
    }
}
