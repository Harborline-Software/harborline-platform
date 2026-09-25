using System.Text.Json.Nodes;

using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleAuthoring;
using VisibilityState = Harborline.Foundation.RuleEngine.Model.VisibilityState;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>T-590 slice 4: checkbox, preset, promotion and lineage over the shared rule store.</summary>
public sealed class FieldRuleBindingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rules-bindings-{Guid.NewGuid():N}");
    private readonly FileJournalDefinitionLifecycleStore _lifecycle;
    private readonly RuleDefinitionCatalog _catalog;

    public FieldRuleBindingTests()
    {
        _lifecycle = new(Path.Combine(_directory, "lifecycle.json"));
        _catalog = new(new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = RuleDefinitionCatalog.Admit,
        }), _lifecycle, RulesGrants.All);
    }

    private static RuleDefinitionEnvelope Envelope(string id, string version = "1.0.0")
        => new(id, version, "tenant-a", "domain-package", new JsonObject { ["kind"] = "package", ["id"] = "finance" }, []);

    private async Task<RuleDefinitionDocument> RoundTrip(RuleDefinitionDocument document, string versionId, long expected)
    {
        await _catalog.SaveDraftJsonAsync(RuleDefinitionCodec.SerializeCanonical(document), versionId, expected, $"{document.Envelope.Id}-{versionId}-{expected}");
        return (await _catalog.LoadAsync(new DefinitionKey("tenant-a", DefinitionKind.Rules, document.Envelope.Id)))!.Source;
    }

    [Fact(DisplayName = "rules-auth-16: a field-property checkbox writes an ordinary rule into the shared store the rule editor reads, and it is editable there")]
    public async Task Checkbox_writes_an_ordinary_editable_rule()
    {
        foreach (var property in Enum.GetValues<FieldCheckbox>())
        {
            var document = FieldRuleBindings.Checkbox(Envelope($"amount-{property}".ToLowerInvariant()), "amount", property);
            Assert.True(RuleIntentValidator.Validate(document, RuleIntentPhase.Publish).IsValid, property.ToString());
            var stored = await RoundTrip(document, "v1", 0);
            Assert.Equal(document.Draft, stored.Draft);

            // The stored rule evaluates to the property the checkbox names, and only that one.
            var visibility = Preview(stored, "{\"amount\":5}").Outcome!.Visibility!;
            Assert.Equal(property == FieldCheckbox.Hidden ? new VisibilityState(Visible: false) : property == FieldCheckbox.Required
                ? new VisibilityState(Required: true) : new VisibilityState(ReadOnly: true), visibility);
        }
        var listed = await _catalog.ListAsync("tenant-a");
        Assert.Equal(3, listed.Count);

        // Editing it is ordinary editing: the editor's formula shape, a new draft revision.
        var required = listed.Single(row => row.Source.Draft.OutputType == RuleActionKind.Required).Source;
        var edited = required with { Draft = ((FormulaDraft)required.Draft) with { Expression = new FormulaExpr.Literal("false", ColumnValueType.Boolean) } };
        var reloaded = await RoundTrip(edited, "v1", 1);
        Assert.Equal("false", Assert.IsType<FormulaExpr.Literal>(((FormulaDraft)reloaded.Draft).Expression).Value);
    }

    [Fact(DisplayName = "rules-auth-17: a preset applies in one click per field kind and creates an ordinary editable rule in the catalogue")]
    public async Task Preset_creates_an_ordinary_editable_rule()
    {
        foreach (var kind in Enum.GetValues<ColumnValueType>())
        {
            var preset = Assert.Single(FieldRuleBindings.PresetsFor(kind));
            var document = FieldRuleBindings.Preset(Envelope($"field-{kind}".ToLowerInvariant()), "field_" + kind.ToString().ToLowerInvariant(), kind, preset);
            var admitted = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);
            Assert.True(admitted.IsValid, $"{kind}: {string.Join(", ", admitted.Diagnostics.Select(d => d.Code))}");
            var stored = await RoundTrip(document, "v1", 0);
            Assert.Equal(RuleActionKind.Validate, stored.Draft.OutputType);

            // Exact check, evaluated either side of its boundary.
            var field = "field_" + kind.ToString().ToLowerInvariant();
            var (expression, pass, fail) = kind switch
            {
                ColumnValueType.Number => ("""{">=":[{"var":"field.field_number"},0]}""", "0", "-1"),
                ColumnValueType.Text => ("""{"!=":[{"var":"field.field_text"},""]}""", "\"a\"", "\"\""),
                _ => ("""{"==":[{"var":"field.field_boolean"},true]}""", "true", "false"),
            };
            var lowered = RuleIntentValidator.Validate(stored, RuleIntentPhase.Publish).Lowered;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expression), lowered), $"{kind}: {lowered?.ToJsonString()}");
            Assert.True(Preview(stored, $"{{\"{field}\":{pass}}}").Outcome!.Validity!.Ok, $"{kind} at {pass}");
            Assert.False(Preview(stored, $"{{\"{field}\":{fail}}}").Outcome!.Validity!.Ok, $"{kind} at {fail}");
        }
        Assert.Throws<ArgumentException>(() => FieldRuleBindings.Preset(Envelope("x"), "x", ColumnValueType.Text, FieldPreset.NonNegative));
        Assert.Equal(3, (await _catalog.ListAsync("tenant-a")).Count);
    }

    [Fact(DisplayName = "rules-auth-18: promotion replaces the field-scoped token with the field key and preserves field identity")]
    public void Promotion_preserves_field_identity()
    {
        var check = FieldRuleBindings.Preset(Envelope("amount-check"), "amount", ColumnValueType.Number, FieldPreset.NonNegative);
        var promoted = FieldRuleBindings.Promote(check);
        Assert.Equal("field.amount", Assert.Single(((FormulaDraft)promoted.Draft).Inputs).Ref);
        Assert.DoesNotContain(((FormulaDraft)promoted.Draft).Inputs, input => input.Ref == "self");
        Assert.DoesNotContain("\"self\"", RuleDefinitionCodec.SerializeCanonical(promoted), StringComparison.Ordinal);

        var before = RuleIntentValidator.Validate(check, RuleIntentPhase.Publish).Lowered;
        var promotedResult = RuleIntentValidator.Validate(promoted, RuleIntentPhase.Publish);
        Assert.True(promotedResult.IsValid, string.Join(", ", promotedResult.Diagnostics.Select(d => $"{d.Code}@{d.Location}")));
        var after = promotedResult.Lowered;
        Assert.True(JsonNode.DeepEquals(before, after), $"{before?.ToJsonString()} != {after?.ToJsonString()}");
        Assert.Contains("\"field.amount\"", after!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "rules-auth-19: lineage is navigable both ways with calculated consumer counts, and an unreferenced calculation is named dead weight")]
    public async Task Lineage_is_bidirectional_and_names_dead_weight()
    {
        static RuleDefinitionDocument Formula(string id, RuleActionKind action, string target, params string[] reads) => new(Envelope(id), id, RuleDefinitionTier.JsonLogic, new FormulaDraft
        {
            Scope = RuleScope.Field, ScopeTarget = target, OutputType = action,
            Inputs = [.. reads.Select(read => new FormulaInputDecl(read, "field." + read, ColumnValueType.Number))],
            Expression = reads.Length == 0 ? new FormulaExpr.Literal("1", ColumnValueType.Number)
                : reads.Skip(1).Aggregate((FormulaExpr)new FormulaExpr.Ref("field." + reads[0]), (left, read) => new FormulaExpr.Binary("+", left, new FormulaExpr.Ref("field." + read))),
        });
        RuleDefinitionDocument[] documents =
        [
            Formula("subtotal-calc", RuleActionKind.Compute, "subtotal", "price", "qty"),
            Formula("tax-calc", RuleActionKind.Compute, "tax", "subtotal"),
            Formula("total-calc", RuleActionKind.Compute, "total", "subtotal", "tax"),
            Formula("unused-calc", RuleActionKind.Compute, "unused", "price"),
        ];
        long revision = 0;
        foreach (var document in documents)
        {
            var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);
            Assert.True(result.IsValid, document.Envelope.Id + ": " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}@{d.Location}")));
            await RoundTrip(document, "v1", revision);
        }
        var lineage = RulesLineage.Build((await _catalog.ListAsync("tenant-a")).Select(row => row.Source));

        Assert.Equal(["tax-calc", "total-calc"], lineage.ConsumersOf["subtotal-calc"]);
        Assert.Equal(2, lineage.ConsumerCount("subtotal-calc"));
        Assert.Equal(1, lineage.ConsumerCount("tax-calc"));
        Assert.Equal(["subtotal-calc", "tax-calc"], lineage.CalculationsReadBy["total-calc"]);
        Assert.Empty(lineage.CalculationsReadBy["subtotal-calc"]);
        Assert.Equal(["total-calc", "unused-calc"], lineage.DeadWeight);
    }

    private static PreviewResult Preview(RuleDefinitionDocument document, string sample)
        => SkinLowering.EvaluatePreview(document.Draft, document.Envelope.Id, JsonNode.Parse(sample)!.AsObject(), TimeProvider.System);

    public void Dispose()
    {
        _lifecycle.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
