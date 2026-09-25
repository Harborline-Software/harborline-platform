using System.Text.Json.Nodes;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>A field-property checkbox (DES-0018 <c>rules-auth-16</c>).</summary>
public enum FieldCheckbox
{
    Required,
    ReadOnly,
    Hidden,
}

/// <summary>A one-click preset per field kind (DES-0018 <c>rules-auth-17</c>).</summary>
public enum FieldPreset
{
    NonNegative,
    NotBlank,
    MustBeTrue,
}

/// <summary>
/// The narrowed and derived bindings over the shared rule store. Each produces an ordinary
/// <see cref="RuleDefinitionDocument"/>: the same shape the rule editor reads and edits, stored in the
/// same catalogue, with no second representation of a field property.
/// </summary>
public static class FieldRuleBindings
{
    private const string Self = "self";

    /// <summary>The rule a field-property checkbox writes (<c>rules-auth-16</c>).</summary>
    public static RuleDefinitionDocument Checkbox(RuleDefinitionEnvelope envelope, string fieldKey, FieldCheckbox property)
    {
        var (action, value) = property switch
        {
            FieldCheckbox.Required => (RuleActionKind.Required, "true"),
            FieldCheckbox.ReadOnly => (RuleActionKind.ReadOnly, "true"),
            _ => (RuleActionKind.Visibility, "false"),
        };
        return Document(envelope, $"{fieldKey} {property}", new FormulaDraft
        {
            Scope = RuleScope.Field, ScopeTarget = fieldKey, OutputType = action,
            Inputs = [], Expression = new FormulaExpr.Literal(value, ColumnValueType.Boolean),
        });
    }

    /// <summary>The presets a field of <paramref name="kind"/> offers.</summary>
    public static IReadOnlyList<FieldPreset> PresetsFor(ColumnValueType kind) => kind switch
    {
        ColumnValueType.Number => [FieldPreset.NonNegative],
        ColumnValueType.Text => [FieldPreset.NotBlank],
        _ => [FieldPreset.MustBeTrue],
    };

    /// <summary>The ordinary, editable validation rule a preset creates (<c>rules-auth-17</c>).</summary>
    public static RuleDefinitionDocument Preset(RuleDefinitionEnvelope envelope, string fieldKey, ColumnValueType kind, FieldPreset preset)
    {
        if (!PresetsFor(kind).Contains(preset)) throw new ArgumentException($"{preset} is not a {kind} preset", nameof(preset));
        FormulaExpr check = preset switch
        {
            FieldPreset.NonNegative => new FormulaExpr.Call(">=", [new FormulaExpr.Ref(Self), new FormulaExpr.Literal("0", ColumnValueType.Number)]),
            FieldPreset.NotBlank => new FormulaExpr.Call("!=", [new FormulaExpr.Ref(Self), new FormulaExpr.Literal("", ColumnValueType.Text)]),
            _ => new FormulaExpr.Call("==", [new FormulaExpr.Ref(Self), new FormulaExpr.Literal("true", ColumnValueType.Boolean)]),
        };
        return Document(envelope, $"{fieldKey} {preset}", new FormulaDraft
        {
            Scope = RuleScope.Field, ScopeTarget = fieldKey, OutputType = RuleActionKind.Validate,
            Inputs = [new FormulaInputDecl(Self, Self, kind)], Expression = check,
        });
    }

    /// <summary>
    /// Promotes a field check (<c>rules-auth-18</c>): its field-scoped <c>self</c> token becomes the field's
    /// stable key, so the rule no longer depends on where it is attached and addresses the same field.
    /// </summary>
    public static RuleDefinitionDocument Promote(RuleDefinitionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Draft is not FormulaDraft { Scope: RuleScope.Field } formula) return document;
        var key = "field." + formula.ScopeTarget;
        return document with
        {
            Draft = formula with
            {
                Inputs = [.. formula.Inputs.Select(input => input.Ref == Self ? input with { Id = key, Ref = key } : input)],
                Expression = formula.Expression is null ? null : Rename(formula.Expression, key),
            },
        };
    }

    private static FormulaExpr Rename(FormulaExpr expression, string key) => expression switch
    {
        FormulaExpr.Ref { Name: Self } => new FormulaExpr.Ref(key),
        FormulaExpr.Binary binary => binary with { Left = Rename(binary.Left, key), Right = Rename(binary.Right, key) },
        FormulaExpr.Call call => call with { Args = [.. call.Args.Select(arg => Rename(arg, key))] },
        FormulaExpr.If conditional => conditional with
        {
            When = conditional.When with { Left = Rename(conditional.When.Left, key), Right = Rename(conditional.When.Right, key) },
            Then = Rename(conditional.Then, key),
            Else = Rename(conditional.Else, key),
        },
        _ => expression,
    };

    private static RuleDefinitionDocument Document(RuleDefinitionEnvelope envelope, string name, RuleDraft draft)
        => new(envelope, name, RuleDefinitionTier.JsonLogic, draft);
}

/// <summary>
/// Bidirectional lineage over authored rules (<c>rules-auth-19</c>): a calculation is a <c>Compute</c>
/// rule producing a field; its consumers are the rules that read that field. Counts are calculated from
/// the lowered programs, and a calculation nothing reads is named as dead weight.
/// </summary>
public sealed record RulesLineage(
    IReadOnlyDictionary<string, IReadOnlyList<string>> ConsumersOf,
    IReadOnlyDictionary<string, IReadOnlyList<string>> CalculationsReadBy,
    IReadOnlyList<string> DeadWeight)
{
    /// <summary>How many rules read the calculation.</summary>
    public int ConsumerCount(string calculationId) => ConsumersOf.TryGetValue(calculationId, out var consumers) ? consumers.Count : 0;

    /// <summary>Builds lineage from admitted documents; a document the compiler refuses has no lineage.</summary>
    public static RulesLineage Build(IEnumerable<RuleDefinitionDocument> documents)
    {
        var admitted = documents
            .Select(document => (Id: document.Envelope.Id, Draft: document.Draft, Result: RuleIntentValidator.Validate(document, RuleIntentPhase.Author)))
            .Where(row => row.Result.IsValid)
            .Select(row => (row.Id, row.Draft, Reads: Reads(row.Result.Lowered)))
            .ToArray();
        var calculations = admitted.Where(row => row.Draft is { OutputType: RuleActionKind.Compute, Scope: RuleScope.Field })
            .ToDictionary(row => row.Id, row => "field." + row.Draft.ScopeTarget, StringComparer.Ordinal);
        var consumers = calculations.ToDictionary(calc => calc.Key,
            calc => (IReadOnlyList<string>)[.. admitted.Where(row => row.Id != calc.Key && row.Reads.Contains(calc.Value)).Select(row => row.Id).Order(StringComparer.Ordinal)],
            StringComparer.Ordinal);
        var readBy = admitted.ToDictionary(row => row.Id,
            row => (IReadOnlyList<string>)[.. calculations.Where(calc => calc.Key != row.Id && row.Reads.Contains(calc.Value)).Select(calc => calc.Key).Order(StringComparer.Ordinal)],
            StringComparer.Ordinal);
        return new(consumers, readBy, [.. consumers.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).Order(StringComparer.Ordinal)]);
    }

    private static HashSet<string> Reads(JsonNode? node)
    {
        var reads = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonNode? current)
        {
            switch (current)
            {
                case JsonObject { Count: 1 } call when call.First().Key == "var":
                    var path = call.First().Value is JsonArray args ? args.FirstOrDefault() : call.First().Value;
                    if (path is JsonValue text && text.TryGetValue<string>(out var s)) reads.Add(s);
                    break;
                case JsonObject obj:
                    foreach (var (_, value) in obj) Walk(value);
                    break;
                case JsonArray array:
                    foreach (var item in array) Walk(item);
                    break;
            }
        }
        Walk(node);
        return reads;
    }
}
