using System.Text;
using Harborline.Contracts.Fields;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsValueDomainTests
{
    [Fact]
    public void Multiple_value_domain_sources_are_refused_at_the_authored_pointer()
    {
        var candidate = new RecordTypeDefinition("person",
        [
            new("status", "Status", new ValueDomainDefinition(
                ["A", "B"],
                new TaxonomySchemeReference("employment-status", "1"))),
        ]);

        var refusals = new RecordsIntentValidator().Validate(candidate, null);

        Assert.Contains(refusals, refusal => refusal is
        {
            Code: "records.value_domain.source_count",
            JsonPointer: "/fields/0/value_domain",
        });
    }

    [Fact]
    public void Declared_empty_value_domain_is_refused()
    {
        var candidate = new RecordTypeDefinition("person",
        [
            new("status", "Status", new ValueDomainDefinition()),
        ]);

        var refusals = new RecordsIntentValidator().Validate(candidate, null);

        Assert.Contains(refusals, refusal => refusal is
        {
            Code: "records.value_domain.source_count",
            JsonPointer: "/fields/0/value_domain",
        });
    }

    [Fact]
    public async Task Literal_value_domain_is_admitted_and_registers()
    {
        var registry = new InMemorySchemaRegistry();
        var candidate = new RecordTypeDefinition("person",
        [
            new("status", "Status", new ValueDomainDefinition(["A", "B"])),
        ]);

        var result = await new RecordTypeSchemaCompiler().CompileAndRegisterAsync(candidate, null, registry);

        Assert.Empty(result.Refusals);
        Assert.IsType<Schema>(result.Schema);
    }

    [Fact]
    public async Task Literal_value_domain_enforces_membership_through_the_schema_registry()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await CompileAsync(registry, new("status", "Status", new ValueDomainDefinition(["A", "B"])));

        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(
            """{"status":"A"}"""))).IsValid);
        Assert.False((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(
            """{"status":"C"}"""))).IsValid);
    }

    [Fact]
    public async Task Pattern_does_not_admit_a_value_outside_the_literal_value_domain()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await CompileAsync(registry, new(
            "status",
            "Status",
            new ValueDomainDefinition(["A"]),
            "^[A-Z]$"));

        Assert.False((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(
            """{"status":"B"}"""))).IsValid);
    }

    [Fact]
    public async Task Pattern_without_a_value_domain_constrains_shape_only()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await CompileAsync(registry, new("code", "Code", Pattern: "^[0-9]+$"));

        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(
            """{"code":"123"}"""))).IsValid);
        Assert.False((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(
            """{"code":"abc"}"""))).IsValid);
    }

    private static async Task<Schema> CompileAsync(InMemorySchemaRegistry registry, FieldDefinition field)
    {
        var result = await new RecordTypeSchemaCompiler().CompileAndRegisterAsync(
            new RecordTypeDefinition("person", [field]),
            null,
            registry);

        Assert.Empty(result.Refusals);
        return Assert.IsType<Schema>(result.Schema);
    }
}
