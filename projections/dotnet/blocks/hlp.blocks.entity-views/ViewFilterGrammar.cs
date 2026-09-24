using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Blocks.EntityViews;

/// <summary>The closed comparison vocabulary available to view authors.</summary>
public enum ViewComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    In,
}

/// <summary>Whether a collection predicate must match at least one or every item.</summary>
public enum ViewCollectionQuantifier
{
    Any,
    All,
}

/// <summary>The closed, typed filter tree stored in a view definition.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ViewComparisonFilter), "comparison")]
[JsonDerivedType(typeof(ViewAllFilter), "all")]
[JsonDerivedType(typeof(ViewAnyOfFilter), "anyOf")]
[JsonDerivedType(typeof(ViewNotFilter), "not")]
[JsonDerivedType(typeof(ViewFunctionFilter), "function")]
[JsonDerivedType(typeof(ViewCollectionFilter), "collection")]
public abstract record ViewFilter
{
    public static ViewFilter Equal(string field, object? value) =>
        Compare(field, ViewComparisonOperator.Equal, value);

    public static ViewFilter Compare(string field, ViewComparisonOperator comparison, object? value) =>
        new ViewComparisonFilter(field, comparison, JsonSerializer.SerializeToElement(value));

    public static ViewFilter All(params ViewFilter[] filters) => new ViewAllFilter(filters);

    public static ViewFilter AnyOf(params ViewFilter[] filters) => new ViewAnyOfFilter(filters);

    public static ViewFilter Not(ViewFilter filter) => new ViewNotFilter(filter);

    public static ViewFilter Call(string function, params ViewFilterOperand[] arguments) =>
        new ViewFunctionFilter(function, arguments);

    public static ViewFilter Any(string field, ViewFilter predicate) =>
        new ViewCollectionFilter(field, ViewCollectionQuantifier.Any, predicate);

    public static ViewFilter Every(string field, ViewFilter predicate) =>
        new ViewCollectionFilter(field, ViewCollectionQuantifier.All, predicate);

    public static ViewFilterOperand FieldValue(string field) => new ViewFieldOperand(field);

    public static ViewFilterOperand Literal(object? value) =>
        new ViewLiteralOperand(JsonSerializer.SerializeToElement(value));
}

public sealed record ViewComparisonFilter(
    string Field,
    ViewComparisonOperator Operator,
    JsonElement Value) : ViewFilter;

public sealed record ViewAllFilter(IReadOnlyList<ViewFilter> Filters) : ViewFilter;

public sealed record ViewAnyOfFilter(IReadOnlyList<ViewFilter> Filters) : ViewFilter;

public sealed record ViewNotFilter(ViewFilter Filter) : ViewFilter;

public sealed record ViewFunctionFilter(
    string Function,
    IReadOnlyList<ViewFilterOperand> Arguments) : ViewFilter;

public sealed record ViewCollectionFilter(
    string Field,
    ViewCollectionQuantifier Quantifier,
    ViewFilter Predicate) : ViewFilter;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ViewFieldOperand), "field")]
[JsonDerivedType(typeof(ViewLiteralOperand), "literal")]
public abstract record ViewFilterOperand;

public sealed record ViewFieldOperand(string Field) : ViewFilterOperand;

public sealed record ViewLiteralOperand(JsonElement Value) : ViewFilterOperand;

/// <summary>One kernel- or pack-supplied function callable by the filter grammar.</summary>
public sealed record ViewExpressionFunction(
    string Name,
    int Arity,
    Func<IReadOnlyList<object?>, bool> Evaluate);

/// <summary>The bound function library used by authoring admission and query execution.</summary>
public interface IViewExpressionFunctionRegistry
{
    bool IsRegistered(string name, int arity);

    bool Evaluate(string name, IReadOnlyList<object?> arguments);
}

/// <summary>Composes the canonical kernel functions with developer-supplied pack functions.</summary>
public sealed class ViewExpressionFunctionRegistry : IViewExpressionFunctionRegistry
{
    private readonly IReadOnlyDictionary<string, ViewExpressionFunction> _functions;

    public ViewExpressionFunctionRegistry(IEnumerable<ViewExpressionFunction>? packFunctions = null)
    {
        var functions = new Dictionary<string, ViewExpressionFunction>(StringComparer.Ordinal)
        {
            ["contains"] = new("contains", 2, arguments => Text(arguments[0]).Contains(Text(arguments[1]), StringComparison.Ordinal)),
            ["startsWith"] = new("startsWith", 2, arguments => Text(arguments[0]).StartsWith(Text(arguments[1]), StringComparison.Ordinal)),
            ["endsWith"] = new("endsWith", 2, arguments => Text(arguments[0]).EndsWith(Text(arguments[1]), StringComparison.Ordinal)),
        };
        foreach (var function in packFunctions ?? [])
        {
            ArgumentNullException.ThrowIfNull(function);
            if (!functions.TryAdd(function.Name, function))
            {
                throw new ArgumentException(
                    $"The view expression function '{function.Name}' is already registered.",
                    nameof(packFunctions));
            }
        }
        _functions = functions;
    }

    public bool IsRegistered(string name, int arity) =>
        _functions.TryGetValue(name, out var function) && function.Arity == arity;

    public bool Evaluate(string name, IReadOnlyList<object?> arguments)
    {
        if (!_functions.TryGetValue(name, out var function) || function.Arity != arguments.Count)
        {
            throw new ViewQueryException("view.filter.function_unknown", "The view filter function is not registered.");
        }
        return function.Evaluate(arguments);
    }

    private static string Text(object? value) =>
        Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}
