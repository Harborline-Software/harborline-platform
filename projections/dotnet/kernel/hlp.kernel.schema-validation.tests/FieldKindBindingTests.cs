using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class FieldKindBindingTests
{
    [Fact]
    public async Task A_schema_with_executable_kind_limits_cannot_register_without_the_field_runtime()
    {
        // Silently treating this binding as an annotation would lose total_digits.
        var registry = new InMemorySchemaRegistry();
        const string schema = """
            {
              "type": "object",
              "properties": {
                "amount": {
                  "type": "number",
                  "multipleOf": 0.1,
                  "x-harborline-field-kind": {
                    "kind_id": "amount",
                    "version": "1.0.0",
                    "parameters": { "total_digits": "3", "fraction_digits": "1" }
                  }
                }
              }
            }
            """;

        await Assert.ThrowsAsync<InvalidSchemaException>(async () => await registry.RegisterAsync(schema));
    }
}
