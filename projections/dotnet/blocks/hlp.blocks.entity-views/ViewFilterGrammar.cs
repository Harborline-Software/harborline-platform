using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Blocks.EntityViews;

/// <summary>The closed comparison vocabulary available to view authors.</summary>
public enum ViewComparisonOperator
{
    /// <summary>Field equals the value.</summary>
    Equal,
    /// <summary>Field differs from the value.</summary>
    NotEqual,
    /// <summary>Field is strictly less than the value.</summary>
    LessThan,
    /// <summary>Field is less than or equal to the value.</summary>
    LessThanOrEqual,
    /// <summary>Field is strictly greater than the value.</summary>
    GreaterThan,
    /// <summary>Field is greater than or equal to the value.</summary>
    GreaterThanOrEqual,
    /// <summary>Field equals any element of the array value; a non-array value never matches.</summary>
    In,
}

/// <summary>Whether a collection predicate must match at least one or every item.</summary>
public enum ViewCollectionQuantifier
{
    /// <summary>The predicate must hold for at least one item.</summary>
    Any,
    /// <summary>The predicate must hold for every item.</summary>
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
    /// <summary>Builds a comparison that the field equals the value, serialized to JSON.</summary>
    public static ViewFilter Equal(string field, object? value) =>
        Compare(field, ViewComparisonOperator.Equal, value);

    /// <summary>Builds a comparison of the field to the value using the given operator; the value is captured as a JSON element.</summary>
    public static ViewFilter Compare(string field, ViewComparisonOperator comparison, object? value) =>
        new ViewComparisonFilter(field, comparison, JsonSerializer.SerializeToElement(value));

    /// <summary>Builds a filter that matches when every supplied filter matches.</summary>
    public static ViewFilter All(params ViewFilter[] filters) => new ViewAllFilter(filters);

    /// <summary>Builds a filter that matches when at least one supplied filter matches.</summary>
    public static ViewFilter AnyOf(params ViewFilter[] filters) => new ViewAnyOfFilter(filters);

    /// <summary>Builds a filter that matches when the supplied filter does not.</summary>
    public static ViewFilter Not(ViewFilter filter) => new ViewNotFilter(filter);

    /// <summary>Builds a call to a registered filter function with the given operands.</summary>
    public static ViewFilter Call(string function, params ViewFilterOperand[] arguments) =>
        new ViewFunctionFilter(function, arguments);

    /// <summary>Builds a collection filter that matches when at least one item in the field satisfies the predicate.</summary>
    public static ViewFilter Any(string field, ViewFilter predicate) =>
        new ViewCollectionFilter(field, ViewCollectionQuantifier.Any, predicate);

    /// <summary>Builds a collection filter that matches when every item in the field satisfies the predicate.</summary>
    public static ViewFilter Every(string field, ViewFilter predicate) =>
        new ViewCollectionFilter(field, ViewCollectionQuantifier.All, predicate);

    /// <summary>Builds an operand that reads the named field of the row.</summary>
    public static ViewFilterOperand FieldValue(string field) => new ViewFieldOperand(field);

    /// <summary>Builds an operand holding a constant, serialized to JSON.</summary>
    public static ViewFilterOperand Literal(object? value) =>
        new ViewLiteralOperand(JsonSerializer.SerializeToElement(value));
}

/// <summary>Compares a row field to a JSON value with a <see cref="ViewComparisonOperator"/>.</summary>
public sealed record ViewComparisonFilter(
    string Field,
    ViewComparisonOperator Operator,
    JsonElement Value) : ViewFilter;

/// <summary>Matches when all of the child filters match.</summary>
public sealed record ViewAllFilter(IReadOnlyList<ViewFilter> Filters) : ViewFilter;

/// <summary>Matches when any of the child filters matches.</summary>
public sealed record ViewAnyOfFilter(IReadOnlyList<ViewFilter> Filters) : ViewFilter;

/// <summary>Matches when the child filter does not.</summary>
public sealed record ViewNotFilter(ViewFilter Filter) : ViewFilter;

/// <summary>Calls a registered function, by name and arity, on the operand arguments.</summary>
public sealed record ViewFunctionFilter(
    string Function,
    IReadOnlyList<ViewFilterOperand> Arguments) : ViewFilter;

/// <summary>Applies the predicate to the items of a collection field, requiring any or all of them to match.</summary>
public sealed record ViewCollectionFilter(
    string Field,
    ViewCollectionQuantifier Quantifier,
    ViewFilter Predicate) : ViewFilter;

/// <summary>An argument of a filter function call, serialized with a <c>kind</c> discriminator of field or literal.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ViewFieldOperand), "field")]
[JsonDerivedType(typeof(ViewLiteralOperand), "literal")]
public abstract record ViewFilterOperand;

/// <summary>Operand that reads the named field of the row.</summary>
public sealed record ViewFieldOperand(string Field) : ViewFilterOperand;

/// <summary>Operand holding a constant JSON value.</summary>
public sealed record ViewLiteralOperand(JsonElement Value) : ViewFilterOperand;

/// <summary>One kernel- or pack-supplied function callable by the filter grammar.</summary>
public sealed record ViewExpressionFunction(
    string Name,
    int Arity,
    Func<IReadOnlyList<object?>, bool> Evaluate);

/// <summary>The bound function library used by authoring admission and query execution.</summary>
public interface IViewExpressionFunctionRegistry
{
    /// <summary>True when a function with this name and exactly this number of arguments is registered.</summary>
    bool IsRegistered(string name, int arity);

    /// <summary>Evaluates the named function on the arguments; throws <see cref="ViewQueryException"/> with view.filter.function_unknown when the name or arity is not registered.</summary>
    bool Evaluate(string name, IReadOnlyList<object?> arguments);
}

/// <summary>Composes the canonical kernel functions with developer-supplied pack functions.</summary>
public sealed class ViewExpressionFunctionRegistry : IViewExpressionFunctionRegistry
{
    private readonly IReadOnlyDictionary<string, ViewExpressionFunction> _functions;

    /// <summary>Registers the built-in text functions contains, startsWith and endsWith (ordinal, two arguments each) plus any pack functions; throws <see cref="ArgumentException"/> when a pack function reuses a registered name.</summary>
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

    /// <summary>True when a function with this name and exactly this arity is registered.</summary>
    public bool IsRegistered(string name, int arity) =>
        _functions.TryGetValue(name, out var function) && function.Arity == arity;

    /// <summary>Runs the function's delegate; throws <see cref="ViewQueryException"/> with view.filter.function_unknown for an unregistered name or an argument count that differs from its arity.</summary>
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
