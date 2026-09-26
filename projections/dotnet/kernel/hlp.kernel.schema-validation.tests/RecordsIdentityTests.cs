using System.Text;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsIdentityTests
{
    [Fact]
    public void Missing_record_type_id_is_refused_at_its_authored_pointer()
    {
        var refusals = new RecordsIntentValidator().Validate(new("", []), null);

        Assert.Contains(refusals, refusal => refusal is
        {
            Code: "records.identity.record_type_id_required",
            JsonPointer: "/record_type_id",
        });
    }

    [Fact]
    public void Changing_record_type_id_within_one_definition_version_is_refused()
    {
        var previous = new RecordTypeDefinition("person", []);
        var candidate = new RecordTypeDefinition("employee", []);

        var refusals = new RecordsIntentValidator().Validate(candidate, previous);

        Assert.Contains(refusals, refusal => refusal is
        {
            Code: "records.identity.record_type_id_immutable",
            JsonPointer: "/record_type_id",
        });
    }

    [Fact]
    public void Duplicate_field_key_is_refused_at_each_noninitial_field()
    {
        var candidate = new RecordTypeDefinition("person",
        [
            new("name", "Name"),
            new("name", "Legal name"),
        ]);

        var refusals = new RecordsIntentValidator().Validate(candidate, null);

        Assert.Contains(refusals, refusal => refusal is
        {
            Code: "records.identity.duplicate_field_key",
            JsonPointer: "/fields/1/field_key",
        });
    }

    [Fact]
    public void Missing_field_key_is_refused_at_its_authored_pointer()
    {
        var candidate = new RecordTypeDefinition("person", [new("", "Name")]);

        var refusals = new RecordsIntentValidator().Validate(candidate, null);

        Assert.Contains(refusals, refusal => refusal is
        {
            Code: "records.identity.field_key_required",
            JsonPointer: "/fields/0/field_key",
        });
    }

    [Fact]
    public void Duplicate_display_names_are_admitted()
    {
        var candidate = new RecordTypeDefinition("person",
        [
            new("given_name", "Name"),
            new("family_name", "Name"),
        ]);

        Assert.Empty(new RecordsIntentValidator().Validate(candidate, null));
    }

    [Fact]
    public async Task Admitted_definition_compiles_registers_and_validates_through_the_schema_registry()
    {
        var registry = new InMemorySchemaRegistry();
        var candidate = new RecordTypeDefinition("person",
        [
            new("given_name", "Given name"),
            new("family_name", "Family name"),
        ]);

        var result = await new RecordTypeSchemaCompiler(new RecordsIntentValidator())
            .CompileAndRegisterAsync(candidate, null, registry);

        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(
            """{"given_name":"Ada","family_name":"Lovelace"}"""))).IsValid);
        Assert.False((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(
            """{"given_name":12,"family_name":"Lovelace"}"""))).IsValid);
    }
}
