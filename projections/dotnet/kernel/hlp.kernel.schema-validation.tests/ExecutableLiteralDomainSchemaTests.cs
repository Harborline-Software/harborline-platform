using System.Text;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class ExecutableLiteralDomainSchemaTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"values\":null}")]
    [InlineData("{\"values\":\"12.30\"}")]
    [InlineData("{\"values\":[12.30]}")]
    [InlineData("{\"values\":[\"12.30\"],\"extra\":true}")]
    [InlineData("{\"values\":[\"12.30\"],\"values\":[\"12.3\"]}")]
    public async Task Malformed_literal_domain_metadata_refuses_before_registration(string binding)
    {
        var registry = new InMemorySchemaRegistry(fieldDomainRuntime: Runtime());

        await Assert.ThrowsAsync<InvalidSchemaException>(async () =>
            await registry.RegisterAsync($$"""{"x-harborline-literal-domain":{{binding}}}"""));

        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public async Task Duplicate_literal_domain_keywords_refuse_before_registration()
    {
        var registry = new InMemorySchemaRegistry(fieldDomainRuntime: Runtime());
        const string binding = """{"values":["12.30"]}""";

        await Assert.ThrowsAsync<InvalidSchemaException>(async () => await registry.RegisterAsync($$"""
            {"x-harborline-literal-domain":{{binding}},"x-harborline-literal-domain":{{binding}}}
            """));

        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public async Task Executable_literal_domain_requires_the_shared_runtime_before_registration()
    {
        var registry = new InMemorySchemaRegistry();

        await Assert.ThrowsAsync<InvalidSchemaException>(async () => await registry.RegisterAsync(
            """{"x-harborline-literal-domain":{"values":["12.30"]}}"""));

        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public async Task Static_literal_validation_performs_no_domain_source_or_authority_io()
    {
        var runtime = Runtime();
        var registry = new InMemorySchemaRegistry(fieldDomainRuntime: runtime);
        var schema = await registry.RegisterAsync(
            """{"x-harborline-literal-domain":{"values":["12.30"]}}""");

        var result = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("12.30"));

        Assert.True(result.IsValid);
    }

    private static ValueDomainRuntime Runtime()
        => new ValueDomainRuntime(new RefusingBoundary(), new RefusingBoundary(), TimeProvider.System);

    private static async Task<IReadOnlyList<Schema>> RegisteredSchemas(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }

    private sealed class RefusingBoundary : IFieldDomainSource, IFieldDomainReadAuthority
    {
        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(
            TenantId tenant,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Static literal validation must not open a source snapshot.");

        public ValueTask<bool> CanReadAsync(
            FieldDomainScope scope,
            ValueDomainDefinition domain,
            FieldDomainMember member,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Static literal validation must not consult authority.");
    }
}
