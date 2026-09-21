using System.Text;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsConstraintAdmissionTests
{
    [Fact]
    public async Task Distinct_complete_domain_sources_with_overlapping_members_admit()
    {
        var scheme = new TaxonomySchemeReference("workflow-state", "1.0.0");
        var runtime = RecordsTestDomains.CreateRuntime(
            new Dictionary<TaxonomySchemeReference, IReadOnlyList<Harborline.Foundation.FieldRuntime.FieldDomainMember>>
            {
                [scheme] = [RecordsTestDomains.Member("open"), RecordsTestDomains.Member("closed")],
            },
            new Dictionary<string, IReadOnlyList<Harborline.Foundation.FieldRuntime.FieldDomainMember>>
            {
                ["records.visible-state"] =
                [
                    RecordsTestDomains.Member("closed", "{\"active\":true}"),
                    RecordsTestDomains.Member("archived", "{\"active\":true}"),
                ],
            });
        var definition = Definition() with
        {
            Fields = [Field()],
            Traits =
            [
                new("taxonomy", "1.0.0", "Taxonomy",
                    [new("state", true, true, new(true, 1, 1, [], new(TaxonomyScheme: scheme)))]),
                new("query", "1.0.0", "Query",
                    [new("state", true, true, new(true, 1, 1, [], new(RecordQuery: new(
                        "records.visible-state", "{\"==\":[{\"var\":\"active\"},true]}"))))]),
            ],
            TraitBindings = [Binding("taxonomy"), Binding("query")],
        };

        var result = await new RecordsIntentValidator(runtime, RecordsTestKinds.Text)
            .ValidateAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.True(result.IsAdmitted);
    }

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
    public async Task Invalid_multiplicity_is_refused_during_intent_admission(int minimum, int maximum)
    {
        var definition = Definition() with
        {
            Fields = [Field() with { Constraints = new(false, minimum, maximum, [], null) }],
        };

        var result = await new RecordsIntentValidator(RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text)
            .ValidateAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal =>
            refusal.Code == "field.constraint_intersection_empty"
            && refusal.JsonPointer == "/fields/0/constraints");
    }

    [Fact]
    public async Task Malformed_nested_members_return_all_structural_refusals_without_throwing()
    {
        var validator = new RecordsIntentValidator(RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text);
        var malformed = Definition() with
        {
            Envelope = new(null!, "1.0.0", "tenant-a", "package-a", "test"),
            Fields = [Field() with { Kind = null! }, null!],
        };

        var typed = await validator.ValidateAsync(
            malformed, RecordsTestDomains.Scope, CancellationToken.None);

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
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: RecordsTestKinds.Text);
        var schema = await new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text)
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);
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
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: RecordsTestKinds.Text);
        var schema = await new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text)
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Equal(valid, (await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(json))).IsValid);
    }

    [Fact]
    public async Task Raw_intent_and_typed_intent_share_structural_admission()
    {
        var invalid = Definition() with { RecordTypeId = "" };
        var validator = new RecordsIntentValidator(RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text);
        var typed = await validator.ValidateAsync(invalid, RecordsTestDomains.Scope, CancellationToken.None);
        var raw = await validator.ValidateJsonAsync(
            RecordsDefinitionJson.SerializeCanonical(invalid), RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Equal(typed.Refusals.Select(item => (item.Code, item.JsonPointer)),
            raw.Refusals.Select(item => (item.Code, item.JsonPointer)));
        Assert.False(raw.IsAdmitted);
        Assert.False((await validator.ValidateJsonAsync(
            "{}", RecordsTestDomains.Scope, CancellationToken.None)).IsAdmitted);
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
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: RecordsTestKinds.Text);
        var schema = await new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text)
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);

        var allowed = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"state":"b"}"""));
        var missing = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("{}"));
        var outside = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"state":"a"}"""));

        Assert.True(allowed.IsValid);
        Assert.False(missing.IsValid);
        Assert.Contains(outside.Errors, error => error.Code == "enum" && error.JsonPointer == "/state");
    }

    [Fact]
    public async Task Three_pairwise_overlapping_slots_with_no_common_value_refuse_admission()
    {
        var definition = Definition() with
        {
            Fields = [Field()],
            Traits = [Trait("first", "a", "b"), Trait("second", "b", "c"), Trait("third", "a", "c")],
            TraitBindings = [Binding("first"), Binding("second"), Binding("third")],
        };

        var result = await new RecordsIntentValidator(RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text)
            .ValidateAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal =>
            refusal.Code == "field.constraint_intersection_empty"
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
