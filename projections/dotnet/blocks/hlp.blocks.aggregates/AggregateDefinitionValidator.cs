namespace Harborline.Blocks.Aggregates;

/// <summary>Fail-closed revision-1 aggregate-definition validator.</summary>
public sealed class AggregateDefinitionValidator
{
    /// <summary>Validates a definition against host policy.</summary><param name="definition">Definition to validate.</param><param name="hostBounds">Maximum host policy.</param><exception cref="AggregateException">The definition is invalid.</exception>
    public void Validate(AggregateDefinition definition, AggregateHostBounds hostBounds)
    {
        if (definition.SchemaVersion != 1 || string.IsNullOrWhiteSpace(definition.DefinitionId) || definition.Revision <= 0 || string.IsNullOrWhiteSpace(definition.Source.SourceRef)) Invalid("Identity, source, or schema version is invalid.");
        if (definition.Measures.Count == 0) Invalid("At least one measure is required.");
        if (definition.Bounds.MaxInputRows <= 0 || definition.Bounds.MaxGroups <= 0 || definition.Bounds.MaxResultCells <= 0 || definition.Bounds.MaxInputRows > hostBounds.MaxInputRows || definition.Bounds.MaxGroups > hostBounds.MaxGroups || definition.Bounds.MaxResultCells > hostBounds.MaxResultCells) Invalid("Bounds are invalid or exceed host policy.");
        Unique(definition.Grouping.Select(x => x.Key).Concat(definition.Measures.Select(x => x.Key)));
        foreach (var dimension in definition.Grouping)
        {
            if (!definition.Source.Fields.TryGetValue(dimension.Field, out var type) || type != dimension.Type) Invalid($"Dimension field '{dimension.Field}' is absent or mistyped.");
        }
        foreach (var measure in definition.Measures) ValidateMeasure(definition.Source.Fields, measure);
        foreach (var depth in definition.Totals.Subtotals)
            if (depth <= 0 || depth >= definition.Grouping.Count || definition.Totals.Subtotals.Count(x => x == depth) != 1) Invalid("Subtotal depths must be unique proper grouping prefixes.");
        if (definition.Filter is not null) ValidateFilter(definition.Source.Fields, definition.Filter);
    }

    private static void ValidateMeasure(IReadOnlyDictionary<string, AggregateValueType> fields, AggregateMeasure measure)
    {
        if (measure.Operator == AggregateOperator.Count && measure.Count == AggregateCountMode.Rows)
        {
            if (measure.Field is not null || measure.InputType is not null || measure.ResultType != AggregateValueType.Integer) Invalid("Row count has no input field and returns integer.");
            return;
        }
        if (measure.Field is null || measure.InputType is null || !fields.TryGetValue(measure.Field, out var actual) || actual != measure.InputType) Invalid($"Measure '{measure.Key}' has an absent or mistyped field.");
        if (measure.Operator == AggregateOperator.Count)
        {
            if (measure.Count != AggregateCountMode.Values || measure.ResultType != AggregateValueType.Integer) Invalid("Value count returns integer.");
            return;
        }
        if (measure.Count is not null) Invalid("Non-count measures cannot declare a count mode.");
        var expectedResult = measure.Operator == AggregateOperator.Average && measure.InputType == AggregateValueType.Integer ? AggregateValueType.Number : measure.InputType;
        if (measure.ResultType != expectedResult) Invalid("The measure result type is incompatible with its input and operator.");
        if (measure.Operator is AggregateOperator.Sum or AggregateOperator.Average && measure.InputType is not (AggregateValueType.Integer or AggregateValueType.Number or AggregateValueType.Decimal)) Invalid("Sum and average require numeric input.");
        if (measure.InputType == AggregateValueType.Boolean) Invalid("Boolean min/max is not in revision 1.");
    }

    private static void ValidateFilter(IReadOnlyDictionary<string, AggregateValueType> fields, AggregateFilter filter)
    {
        switch (filter)
        {
            case AggregateAllFilter all: foreach (var child in all.Filters) ValidateFilter(fields, child); break;
            case AggregateAnyFilter any: foreach (var child in any.Filters) ValidateFilter(fields, child); break;
            case AggregateNotFilter not: ValidateFilter(fields, not.Filter); break;
            case AggregateComparisonFilter comparison:
                if (!fields.TryGetValue(comparison.Field, out var type)) Invalid($"Filter field '{comparison.Field}' is absent.");
                if (comparison.Operator == AggregateComparisonOperator.In)
                {
                    if (comparison.Values is null || comparison.Value is not null || comparison.Values.Any(value => value.Type != type)) Invalid("Membership operands are invalid.");
                }
                else if (comparison.Operator is AggregateComparisonOperator.IsNull or AggregateComparisonOperator.IsNotNull)
                {
                    if (comparison.Value is not null || comparison.Values is not null) Invalid("Null tests have no operand.");
                }
                else if (comparison.Value is null || comparison.Values is not null || comparison.Value.Type != type || comparison.Value.State != AggregateCellState.Value) Invalid("Comparison operand is absent or mistyped.");
                break;
            default: Invalid("Unknown filter node."); break;
        }
    }

    private static void Unique(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys) if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) Invalid("Result keys must be non-empty and unique.");
    }

    private static void Invalid(string message) => throw new AggregateException("aggregates.definition.invalid", message);
}
