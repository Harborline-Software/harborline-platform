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
            query = query.Where(row => Matches(row, predicate.Filter));
        }

        IOrderedEnumerable<ViewRow>? ordered = null;
        foreach (var sort in plan.Sort)
        {
            Func<ViewRow, string> key = row => Value(row, sort.Field);
            ordered = (ordered, sort.Direction) switch
            {
                (null, ViewSortDirection.Ascending) => query.OrderBy(key, StringComparer.Ordinal),
                (null, ViewSortDirection.Descending) => query.OrderByDescending(key, StringComparer.Ordinal),
                (_, ViewSortDirection.Ascending) => ordered.ThenBy(key, StringComparer.Ordinal),
                (_, ViewSortDirection.Descending) => ordered.ThenByDescending(key, StringComparer.Ordinal),
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

    private bool Matches(ViewRow row, ViewFilter filter) => filter switch
    {
        ViewComparisonFilter comparison => Compare(
            Read(row, comparison.Field),
            comparison.Operator,
            FromJson(comparison.Value)),
        ViewAllFilter all => all.Filters.All(item => Matches(row, item)),
        ViewAnyOfFilter any => any.Filters.Any(item => Matches(row, item)),
        ViewNotFilter not => !Matches(row, not.Filter),
        ViewFunctionFilter function => Call(row, function),
        ViewCollectionFilter collection => Quantify(row, collection),
        _ => throw new ViewQueryException("view.filter.kind_unknown", "The view filter kind is not registered."),
    };

    private bool Quantify(ViewRow row, ViewCollectionFilter collection)
    {
        if (Read(row, collection.Field) is not System.Collections.IEnumerable values
            || values is string)
        {
            return false;
        }
        var matches = values.Cast<object?>().Select(value => Matches(Element(value), collection.Predicate));
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

    private static bool Compare(object? left, ViewComparisonOperator comparison, object? right)
    {
        if (comparison == ViewComparisonOperator.In && right is object?[] values)
        {
            return values.Any(value => Compare(left, ViewComparisonOperator.Equal, value));
        }
        var order = Order(left, right);
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

    private static int Order(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null ? right is null ? 0 : -1 : 1;
        }
        if (TryDecimal(left, out var leftNumber) && TryDecimal(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }
        if (left is bool leftBoolean && right is bool rightBoolean)
        {
            return leftBoolean.CompareTo(rightBoolean);
        }
        return string.CompareOrdinal(
            Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool TryDecimal(object value, out decimal number) =>
        decimal.TryParse(
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);

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
}
