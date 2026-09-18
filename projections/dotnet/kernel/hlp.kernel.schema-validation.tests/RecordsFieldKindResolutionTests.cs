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
        var registry = new InMemorySchemaRegistry();
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestKinds.Text);
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
            compiler.CompileAndRegisterAsync(definition).AsTask());

        Assert.Contains(error.Refusals, refusal =>
            refusal.Code == "records.field.kind_unresolved" && refusal.JsonPointer == "/fields/0/kind");
        await foreach (var schema in registry.ListAsync())
            Assert.Fail($"A refused definition registered schema {schema.Id}.");
    }

}
