using System.Text.Json.Serialization;

namespace Harborline.Blocks.Aggregates;

/// <summary>Lifecycle state of an immutable aggregate-definition revision.</summary>
public enum AggregateDefinitionStatus
{
    /// <summary>A revision that may be validated but is not eligible for by-id evaluation.</summary>
    Draft,
    /// <summary>An immutable revision eligible for evaluation.</summary>
    Published,
}

/// <summary>Canonical field and cell value types supported by interface revision 1.</summary>
public enum AggregateValueType
{
    /// <summary>Ordinal, case-sensitive text.</summary>
    String,
    /// <summary>A Boolean value.</summary>
    Boolean,
    /// <summary>A signed 64-bit integer.</summary>
    Integer,
    /// <summary>A finite IEEE-754 binary64 number.</summary>
    Number,
    /// <summary>An arbitrary-precision base-10 value serialized as a canonical string.</summary>
    Decimal,
    /// <summary>An ISO 8601 calendar date.</summary>
    Date,
    /// <summary>An ISO 8601 UTC instant.</summary>
    DateTime,
}

/// <summary>Direction used to order a grouping dimension.</summary>
public enum AggregateSortDirection
{
    /// <summary>Ascending canonical order.</summary>
    Asc,
    /// <summary>Descending canonical order.</summary>
    Desc,
}

/// <summary>Policy for null dimension values.</summary>
public enum AggregateDimensionNullPolicy
{
    /// <summary>Emit a typed null key.</summary>
    Bucket,
    /// <summary>Exclude the row.</summary>
    Exclude,
}

/// <summary>Scalar reduction operator.</summary>
public enum AggregateOperator
{
    /// <summary>Sum values.</summary>
    Sum,
    /// <summary>Count rows or values.</summary>
    Count,
    /// <summary>Average values.</summary>
    Average,
    /// <summary>Minimum value.</summary>
    Min,
    /// <summary>Maximum value.</summary>
    Max,
}

/// <summary>Null-input policy for a measure.</summary>
public enum AggregateNullPolicy
{
    /// <summary>Ignore null inputs.</summary>
    Ignore,
    /// <summary>Propagate a null cell.</summary>
    Propagate,
}

/// <summary>Unavailable-input policy for a measure.</summary>
public enum AggregateUnavailablePolicy
{
    /// <summary>Fail the evaluation.</summary>
    Fail,
    /// <summary>Propagate an unavailable cell.</summary>
    Propagate,
}

/// <summary>Count target.</summary>
public enum AggregateCountMode
{
    /// <summary>Count every row.</summary>
    Rows,
    /// <summary>Count non-null field values.</summary>
    Values,
}

/// <summary>Closed comparison operator vocabulary.</summary>
public enum AggregateComparisonOperator
{
    /// <summary>Equality.</summary>
    Eq,
    /// <summary>Inequality.</summary>
    Neq,
    /// <summary>Less than.</summary>
    Lt,
    /// <summary>Less than or equal.</summary>
    Lte,
    /// <summary>Greater than.</summary>
    Gt,
    /// <summary>Greater than or equal.</summary>
    Gte,
    /// <summary>Membership.</summary>
    In,
    /// <summary>Null test.</summary>
    IsNull,
    /// <summary>Non-null test.</summary>
    IsNotNull,
}

/// <summary>An immutable aggregate-definition revision.</summary>
/// <param name="SchemaVersion">Wire schema version; revision 1 requires value 1.</param>
/// <param name="DefinitionId">Stable definition identifier within a tenant.</param>
/// <param name="Revision">Positive immutable revision number.</param>
/// <param name="Status">Revision lifecycle state.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="Source">Logical source contract.</param>
/// <param name="Filter">Optional closed predicate tree.</param>
/// <param name="Grouping">Ordered dimensions.</param>
/// <param name="Measures">Ordered measures.</param>
/// <param name="Totals">Subtotal and grand-total policy.</param>
/// <param name="Bounds">Required fail-closed limits.</param>
public sealed record AggregateDefinition(int SchemaVersion, string DefinitionId, long Revision, AggregateDefinitionStatus Status, string Title, AggregateSourceDefinition Source, AggregateFilter? Filter, IReadOnlyList<AggregateDimension> Grouping, IReadOnlyList<AggregateMeasure> Measures, AggregateTotals Totals, AggregateBounds Bounds);

/// <summary>A provider-neutral logical source and declared field contract.</summary>
/// <param name="SourceRef">Opaque logical source reference.</param><param name="Fields">Exact declared field types.</param>
public sealed record AggregateSourceDefinition(string SourceRef, IReadOnlyDictionary<string, AggregateValueType> Fields);

/// <summary>An ordered grouping dimension.</summary>
/// <param name="Key">Result key.</param><param name="Field">Source field.</param><param name="Type">Declared type.</param><param name="Nulls">Null policy.</param><param name="Direction">Sort direction.</param>
public sealed record AggregateDimension(string Key, string Field, AggregateValueType Type, AggregateDimensionNullPolicy Nulls, AggregateSortDirection Direction);

/// <summary>A named scalar reduction.</summary>
/// <param name="Key">Result key.</param><param name="Operator">Required operator.</param><param name="Field">Input field, null only for row count.</param><param name="InputType">Input type, null only for row count.</param><param name="ResultType">Result type.</param><param name="Nulls">Null policy.</param><param name="Unavailable">Unavailable policy.</param><param name="Count">Count target for count measures.</param>
public sealed record AggregateMeasure(string Key, [property: JsonRequired] AggregateOperator Operator, string? Field, AggregateValueType? InputType, AggregateValueType ResultType, AggregateNullPolicy Nulls = AggregateNullPolicy.Ignore, AggregateUnavailablePolicy Unavailable = AggregateUnavailablePolicy.Fail, AggregateCountMode? Count = null);

/// <summary>Requested total levels.</summary>
/// <param name="Subtotals">Unique grouping-prefix depths.</param><param name="GrandTotal">Whether to append a grand total.</param>
public sealed record AggregateTotals(IReadOnlyList<int> Subtotals, bool GrandTotal);

/// <summary>Required evaluation limits.</summary>
/// <param name="MaxInputRows">Maximum enumerated source rows.</param><param name="MaxGroups">Maximum result groups.</param><param name="MaxResultCells">Maximum measure cells.</param>
public sealed record AggregateBounds(int MaxInputRows, int MaxGroups, int MaxResultCells);

/// <summary>Base type for the closed predicate tree.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AggregateAllFilter), "all")]
[JsonDerivedType(typeof(AggregateAnyFilter), "any")]
[JsonDerivedType(typeof(AggregateNotFilter), "not")]
[JsonDerivedType(typeof(AggregateComparisonFilter), "comparison")]
public abstract record AggregateFilter;

/// <summary>Conjunction filter.</summary><param name="Filters">Child predicates.</param>
public sealed record AggregateAllFilter(IReadOnlyList<AggregateFilter> Filters) : AggregateFilter;
/// <summary>Disjunction filter.</summary><param name="Filters">Child predicates.</param>
public sealed record AggregateAnyFilter(IReadOnlyList<AggregateFilter> Filters) : AggregateFilter;
/// <summary>Negation filter.</summary><param name="Filter">Child predicate.</param>
public sealed record AggregateNotFilter(AggregateFilter Filter) : AggregateFilter;
/// <summary>Typed field comparison.</summary><param name="Field">Source field.</param><param name="Operator">Comparison.</param><param name="Value">Single operand.</param><param name="Values">Membership operands.</param>
public sealed record AggregateComparisonFilter(string Field, AggregateComparisonOperator Operator, AggregateValue? Value = null, IReadOnlyList<AggregateValue>? Values = null) : AggregateFilter;

/// <summary>Discriminated source value.</summary>
/// <param name="Type">Declared type.</param><param name="Value">Canonical CLR value.</param><param name="State">Availability state.</param>
public sealed record AggregateValue(AggregateValueType Type, object? Value, AggregateCellState State = AggregateCellState.Value)
{
    /// <summary>Creates a typed null value.</summary><param name="type">Declared type.</param><returns>The null value.</returns>
    public static AggregateValue Null(AggregateValueType type) => new(type, null, AggregateCellState.Null);
    /// <summary>Creates an unavailable value.</summary><param name="type">Declared type.</param><returns>The unavailable value.</returns>
    public static AggregateValue Unavailable(AggregateValueType type) => new(type, null, AggregateCellState.Unavailable);
}
