using Harborline.Contracts.Fields;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsTraitBindingTests
{
    [Fact]
    public void records_ck_37_and_39_resolve_an_exact_trait_version_and_bind_its_required_slot()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, true)])));
        var candidate = new RecordTypeDefinition(
            "location",
            [new("street_address", "Street")],
            [new("addressable", "1.0.0", [new("street", "street_address")])]);

        Assert.Empty(validator.Validate(candidate, null));
    }

    [Fact]
    public void records_ck_39_unresolved_trait_version_refuses_at_the_authored_trait_pointer()
    {
        var validator = new RecordsIntentValidator(new TraitSource());
        var candidate = new RecordTypeDefinition("location", [], [new("addressable", "1.0.0", [])]);

        var refusal = Assert.Single(validator.Validate(candidate, null));

        Assert.Equal("records.trait.version_unresolved", refusal.Code);
        Assert.Equal("/traits/0", refusal.JsonPointer);
    }

    [Fact]
    public void records_auth_35_blocking_required_slot_refuses_before_registration()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, true)])));
        var candidate = new RecordTypeDefinition("location", [], [new("addressable", "1.0.0", [])]);

        var refusals = validator.Validate(candidate, null);

        Assert.Contains(refusals, refusal => refusal is
        {
            Code: "records.trait.blocking_slot_unbound",
            JsonPointer: "/traits/0/slot_bindings",
        });
    }

    [Fact]
    public void records_ck_37_duplicate_bindings_for_one_qualified_slot_refuse_loudly()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, false)])));
        var candidate = new RecordTypeDefinition(
            "location",
            [new("street_address", "Street")],
            [new("addressable", "1.0.0", [new("street", "street_address"), new("street", "street_address")])]);

        var refusal = Assert.Single(validator.Validate(candidate, null));

        Assert.Equal("records.trait.slot_binding_ambiguous", refusal.Code);
        Assert.Equal("/traits/0/slot_bindings/1/slot_key", refusal.JsonPointer);
    }

    [Fact]
    public void records_ck_37_two_references_binding_one_qualified_slot_to_different_fields_refuse_loudly()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, false)])));
        var candidate = new RecordTypeDefinition(
            "location",
            [new("street_address", "Street"), new("mailing_street", "Mailing street")],
            [
                new("addressable", "1.0.0", [new("street", "street_address")]),
                new("addressable", "1.0.0", [new("street", "mailing_street")]),
            ]);

        var refusal = Assert.Single(validator.Validate(candidate, null));

        Assert.Equal("records.trait.slot_binding_ambiguous", refusal.Code);
        Assert.Equal("/traits/1/slot_bindings/0/slot_key", refusal.JsonPointer);
    }

    [Fact]
    public async Task Trait_refusals_prevent_schema_registry_mutation()
    {
        var validator = new RecordsIntentValidator(new TraitSource());
        var registry = new InMemorySchemaRegistry();
        var candidate = new RecordTypeDefinition("location", [], [new("addressable", "1.0.0", [])]);

        var result = await new RecordTypeSchemaCompiler(validator).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal.Code == "records.trait.version_unresolved");
        var registered = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) registered.Add(schema);
        Assert.Empty(registered);
    }

    private static FieldConstraintDefinition Constraints()
        => new(false, 0, 1, [], null);

    private sealed class TraitSource(params TraitDefinition[] traits) : IRecordTraitSource
    {
        public TraitDefinition? Resolve(string traitId, string version)
            => traits.SingleOrDefault(trait => trait.TraitId == traitId && trait.Version == version);
    }
}
