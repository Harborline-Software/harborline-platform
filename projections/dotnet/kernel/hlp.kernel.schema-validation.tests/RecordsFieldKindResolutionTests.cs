using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsFieldKindResolutionTests
{
    [Theory]
    [InlineData("unregistered-kind", "1.0.0")]
    [InlineData("text", "2.0.0")]
    public async Task Compiler_refuses_an_unregistered_kind_instead_of_guessing_a_string(string kindId, string version)
    {
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: RecordsTestKinds.Text);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), RecordsTestKinds.Text);
        var definition = new RecordTypeDefinition
        {
            Envelope = new("definition.example", "1.0.0", "tenant-a", "package-a", "test"),
            RecordTypeId = "records.example",
            Name = "Example",
            Key = "example",
            ClassId = "class.example",
            Fields =
            [
                new RecordFieldDefinition
                {
                    Name = "Value",
                    Key = "value",
                    Kind = new(kindId, version, new Dictionary<string, string>()),
                },
            ],
        };

        var error = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() =>
            compiler.CompileAndRegisterAsync(
                definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask());

        Assert.Contains(error.Refusals, refusal =>
            refusal.Code == "field.kind_unresolved" && refusal.JsonPointer == "/fields/0/kind");
        await foreach (var schema in registry.ListAsync())
            Assert.Fail($"A refused definition registered schema {schema.Id}.");
    }

    [Fact]
    public async Task Compiler_aggregates_unknown_kinds_and_invalid_parameters_before_registration()
    {
        var runtime = RecordsTestKinds.Text;
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), runtime);
        var definition = new RecordTypeDefinition
        {
            Envelope = new("definition.example", "1.0.0", "tenant-a", "package-a", "test"),
            RecordTypeId = "records.example",
            Name = "Example",
            Key = "example",
            ClassId = "class.example",
            Fields =
            [
                new RecordFieldDefinition
                {
                    Name = "Missing",
                    Key = "missing",
                    Kind = new("unknown", "1.0.0", new Dictionary<string, string>()),
                },
                new RecordFieldDefinition
                {
                    Name = "Invalid",
                    Key = "invalid",
                    Kind = new("text", "1.0.0", new Dictionary<string, string>
                    {
                        ["total_digits"] = "2",
                    }),
                },
            ],
        };

        var error = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() =>
            compiler.CompileAndRegisterAsync(
                definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask());

        Assert.Contains(error.Refusals, refusal =>
            refusal is { Code: "field.kind_unresolved", JsonPointer: "/fields/0/kind" });
        Assert.Contains(error.Refusals, refusal =>
            refusal is { Code: "field.kind_parameter_type_mismatch", JsonPointer: "/fields/1/kind/parameters" });
        await foreach (var schema in registry.ListAsync())
            Assert.Fail($"A refused definition registered schema {schema.Id}.");
    }

}
