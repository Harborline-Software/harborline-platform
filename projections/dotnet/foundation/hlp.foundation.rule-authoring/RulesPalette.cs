using Harborline.Foundation.RuleEngine.Functions;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>A field as Records owns it. The palette reads these facts and stores no copy of the schema.</summary>
public sealed record RecordFieldFact(string Key, string Label, ColumnValueType ValueType, string? Section = null);

/// <summary>A child collection as Records owns it: the only thing an <c>agg</c> fold may range over.</summary>
public sealed record RecordTableFact(string Key, IReadOnlyList<RecordFieldFact> Columns);

/// <summary>The Records-owned fields of one record type.</summary>
public sealed record RecordFieldSet(IReadOnlyList<RecordFieldFact> Fields, IReadOnlyList<RecordTableFact> Tables);

/// <summary>One pickable reference, in its authored <c>rules-ck-7</c> form.</summary>
public sealed record RulesPaletteReference(string Id, string Label, ColumnValueType ValueType);

/// <summary>The generated function-and-field palette (DES-0018 <c>rules-auth-20</c>).</summary>
public sealed record RulesPalette(IReadOnlyList<BuiltInFunctionDefinition> Functions, IReadOnlyList<RulesPaletteReference> References);

/// <summary>
/// Generates the palette from the R1 built-in register and the Records fields, never from a hand
/// list. References follow the scope-addressing grammar for the rule's scope, so an author is only
/// offered forms the compiler admits there.
/// </summary>
public static class RulesPaletteGenerator
{
    /// <summary>Builds the palette for a rule of <paramref name="scope"/> targeting <paramref name="scopeTarget"/>.</summary>
    public static RulesPalette Generate(RecordFieldSet records, RuleScope scope, string scopeTarget)
    {
        ArgumentNullException.ThrowIfNull(records);
        var references = new List<RulesPaletteReference>();
        var top = records.Fields;
        if (scope == RuleScope.Row)
        {
            var parts = (scopeTarget ?? "").Split('/');
            var table = parts.Length == 2 ? records.Tables.FirstOrDefault(t => t.Key == parts[0]) : null;
            var self = table?.Columns.FirstOrDefault(c => c.Key == parts[1]);
            if (self is not null) references.Add(new("self", "This field", self.ValueType));
            foreach (var column in table?.Columns ?? []) references.Add(new("row." + column.Key, "Row field " + column.Label, column.ValueType));
            foreach (var field in top) references.Add(new("parent." + field.Key, "Parent field " + field.Label, field.ValueType));
        }
        else
        {
            var self = scope == RuleScope.Field ? top.FirstOrDefault(f => f.Key == scopeTarget) : null;
            if (self is not null) references.Add(new("self", "This field", self.ValueType));
            foreach (var field in top)
                references.Add(field.Section is null
                    ? new("field." + field.Key, "Field " + field.Label, field.ValueType)
                    : new($"section.{field.Section}.{field.Key}", $"Section {field.Section} field {field.Label}", field.ValueType));
        }
        foreach (var table in records.Tables)
            foreach (var column in table.Columns)
                foreach (var fold in BuiltInFunctionRegister.AggregateFolds.Where(fold => Folds(fold, column.ValueType)))
                    references.Add(new($"table.{fold}({table.Key}.{column.Key})",
                        $"{char.ToUpperInvariant(fold[0])}{fold[1..]} of {table.Key} {column.Label}",
                        fold is "any" or "all" ? ColumnValueType.Boolean : ColumnValueType.Number));
        return new([.. BuiltInFunctionRegister.Functions.Where(f => f.Authorable)], references);
    }

    private static bool Folds(string fold, ColumnValueType type) => fold switch
    {
        "count" => true,
        "any" or "all" => type == ColumnValueType.Boolean,
        _ => type == ColumnValueType.Number,
    };
}
