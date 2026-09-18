using System.Text;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsConstraintAdmissionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("records.target", "class.target")]
    public void Reference_picker_and_write_refuse_an_ambiguous_or_missing_target(string? type, string? @class)
    {
        var reference = new FieldReferenceDefinition(type, @class, null,
            ReferenceCardinality.One, ReferenceDeleteBehavior.Block);
        var target = new RecordReferenceTargetFacts("records.target", "class.target", new HashSet<string>());

        Assert.False(RecordsReferenceAdmission.TargetMatches(reference, target));
        var refusal = Assert.Single(RecordsReferenceAdmission.Validate(reference, target, "/fields/0/reference").Refusals);
        Assert.Equal("records.reference.target_mismatch", refusal.Code);
        Assert.Equal("/fields/0/reference", refusal.JsonPointer);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(3, 2)]
    public void Invalid_multiplicity_is_refused_during_intent_admission(int minimum, int maximum)
    {
        var definition = Definition() with
        {
            Fields = [Field() with { Constraints = new(false, minimum, maximum, [], null) }],
        };

        var result = new RecordsIntentValidator().Validate(definition);

        Assert.Contains(result.Refusals, refusal =>
            refusal.Code == "records.field.multiplicity_invalid"
            && refusal.JsonPointer == "/fields/0/constraints");
    }

    [Fact]
    public void Malformed_nested_members_return_all_structural_refusals_without_throwing()
    {
        var validator = new RecordsIntentValidator();
        var malformed = Definition() with
        {
            Envelope = new(null!, "1.0.0", "tenant-a", "package-a", "test"),
            Fields = [Field() with { Kind = null! }, null!],
        };

        var typed = validator.Validate(malformed);

        Assert.Equal(
            new[] { "/envelope/definition_id", "/fields/0/kind", "/fields/1" },
            typed.Refusals.Select(refusal => refusal.JsonPointer).Order(StringComparer.Ordinal));
        Assert.All(typed.Refusals, refusal => Assert.Equal("records.definition.null_forbidden", refusal.Code));
    }

    [Theory]
    [InlineData("{\"state\":{\"en\":\"Open\",\"fr\":\"Ouvert\"}}", true)]
    [InlineData("{\"state\":\"Open\"}", false)]
    [InlineData("{\"state\":{\"en\":42}}", false)]
    public async Task Translatable_field_values_are_locale_to_string_maps(string json, bool valid)
    {
        var definition = Definition() with { Fields = [Field() with { IsTranslatable = true }] };
        var registry = new InMemorySchemaRegistry();
        var schema = await new RecordsDefinitionCompiler(registry, RecordsTestKinds.Text).CompileAndRegisterAsync(definition);
        Assert.Equal(valid, (await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(json))).IsValid);
    }

    [Theory]
    [InlineData("{\"state\":[\"record-a\",\"record-b\"]}", true)]
    [InlineData("{\"state\":\"record-a\"}", false)]
    [InlineData("{\"state\":[\"record-a\"]}", false)]
    [InlineData("{\"state\":[\"a\",\"b\",\"c\",\"d\"]}", false)]
    public async Task Reference_multiplicity_is_enforced_by_the_compiled_schema(string json, bool valid)
    {
        var definition = Definition() with
        {
            Fields = [Field() with
            {
                Reference = new("records.target", null, null, ReferenceCardinality.Many, ReferenceDeleteBehavior.Block),
                Constraints = new(true, 2, 3, [], null),
            }],
        };
        var registry = new InMemorySchemaRegistry();
        var schema = await new RecordsDefinitionCompiler(registry, RecordsTestKinds.Text).CompileAndRegisterAsync(definition);

        Assert.Equal(valid, (await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(json))).IsValid);
    }

    [Fact]
    public void Raw_intent_and_typed_intent_share_structural_admission()
    {
        var invalid = Definition() with { RecordTypeId = "" };
        var validator = new RecordsIntentValidator();
        var typed = validator.Validate(invalid);
        var raw = validator.ValidateJson(RecordsDefinitionJson.SerializeCanonical(invalid));

        Assert.Equal(typed.Refusals.Select(item => (item.Code, item.JsonPointer)),
            raw.Refusals.Select(item => (item.Code, item.JsonPointer)));
        Assert.False(raw.IsAdmitted);
        Assert.False(validator.ValidateJson("{}").IsAdmitted);
    }

    [Fact]
    public async Task Compiled_field_inherits_the_full_intersection_of_its_bound_slots()
    {
        var definition = Definition() with
        {
            Fields = [Field() with { Pattern = "^[a-z]+$" }],
            Traits = [Trait("first", "a", "b"), Trait("second", "b", "c")],
            TraitBindings = [Binding("first"), Binding("second")],
        };
        var registry = new InMemorySchemaRegistry();
        var schema = await new RecordsDefinitionCompiler(registry, RecordsTestKinds.Text).CompileAndRegisterAsync(definition);

        var allowed = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"state":"b"}"""));
        var missing = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("{}"));
        var outside = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"state":"a"}"""));

        Assert.True(allowed.IsValid);
        Assert.False(missing.IsValid);
        Assert.Contains(outside.Errors, error => error.Code == "enum" && error.JsonPointer == "/state");
    }

    [Fact]
    public void Three_pairwise_overlapping_slots_with_no_common_value_refuse_admission()
    {
        var definition = Definition() with
        {
            Fields = [Field()],
            Traits = [Trait("first", "a", "b"), Trait("second", "b", "c"), Trait("third", "a", "c")],
            TraitBindings = [Binding("first"), Binding("second"), Binding("third")],
        };

        var result = new RecordsIntentValidator().Validate(definition);

        Assert.Contains(result.Refusals, refusal =>
            refusal.Code == "records.trait.constraint_intersection_empty"
            && refusal.JsonPointer == "/fields/0/constraints");
    }

    private static RecordTypeDefinition Definition() => new()
    {
        Envelope = new("definition.example", "1.0.0", "tenant-a", "package-a", "test"),
        RecordTypeId = "records.example",
        Name = "Example",
        Key = "example",
        ClassId = "class.example",
    };

    private static RecordFieldDefinition Field() => new()
    {
        Name = "State",
        Key = "state",
        Kind = new("text", "1.0.0", new Dictionary<string, string>()),
    };

    private static TraitDefinition Trait(string id, params string[] values) => new(
        id, "1.0.0", id,
        [new("state", true, true, new(true, 1, 1, [], new(LiteralValues: values)))]);

    private static TraitSlotBinding Binding(string id) => new(id, "1.0.0", "state", "state");
}
