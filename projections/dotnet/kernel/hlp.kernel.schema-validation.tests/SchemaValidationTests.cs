using System.Diagnostics;
using System.Text;
using Harborline.Kernel.SchemaValidation;
using Xunit;
using SchemaRecord = Harborline.Kernel.SchemaValidation.Schema;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class SchemaValidationTests
{
    private const string PersonSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "age": { "type": "integer", "minimum": 0 }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    [Fact]
    public async Task EquivalentSchemasHaveOneContentAddress()
    {
        var registry = new InMemorySchemaRegistry();
        var first = await registry.RegisterAsync("""{"type":"string","maxLength":100}""");
        var second = await registry.RegisterAsync("""{ "maxLength": 100, "type": "string" }""");
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ContentAddress, second.ContentAddress);
    }

    [Fact]
    public async Task RegistrationRoundTripsAndTagFilterIsExact()
    {
        var registry = new InMemorySchemaRegistry();
        var person = await registry.RegisterAsync(PersonSchema, tags: ["forms", "person"]);
        await registry.RegisterAsync("""{"type":"boolean"}""", tags: ["other"]);
        Assert.Equal(person, await registry.GetAsync(person.Id));
        Assert.Equal([person], await Collect(registry.ListAsync("forms")));
    }

    [Fact]
    public async Task MatchingPayloadIsValid()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await registry.RegisterAsync(PersonSchema);
        var result = await registry.ValidateAsync(schema.Id, Utf8("""{"name":"Ada","age":36}"""));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task RequiredAndMinimumErrorsAreFieldAddressableAndLocalizable()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await registry.RegisterAsync(PersonSchema);
        var result = await registry.ValidateAsync(schema.Id, Utf8("""{"age":-5}"""));
        Assert.False(result.IsValid);
        var required = Assert.Single(result.Errors, error => error is { Code: "required", JsonPointer: "/name" });
        Assert.Equal("name", required.Params!["field"]);
        var minimum = Assert.Single(result.Errors, error => error is { Code: "minimum", JsonPointer: "/age" });
        Assert.Equal("0", minimum.Params!["min"]);
    }

    [Fact]
    public async Task InvalidPayloadJsonFailsClosed()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await registry.RegisterAsync(PersonSchema);
        var result = await registry.ValidateAsync(schema.Id, Utf8("{"));
        Assert.False(result.IsValid);
        Assert.Equal("invalid-json", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task UnknownSchemaThrows()
    {
        var registry = new InMemorySchemaRegistry();
        await Assert.ThrowsAsync<SchemaNotFoundException>(
            () => registry.ValidateAsync(new SchemaId("schema:missing"), Utf8("{}" )).AsTask());
    }

    [Fact]
    public async Task MalformedAndForeignDialectSchemasAreRejected()
    {
        var registry = new InMemorySchemaRegistry();
        await Assert.ThrowsAsync<InvalidSchemaException>(() => registry.RegisterAsync("{").AsTask());
        await Assert.ThrowsAsync<InvalidSchemaException>(() => registry.RegisterAsync(
            """{"$schema":"http://json-schema.org/draft-07/schema#","type":"string"}""").AsTask());
    }

    [Fact]
    public async Task SchemaSizeAndDepthAreBounded()
    {
        var sizeBound = new InMemorySchemaRegistry(new SchemaRegistryOptions { MaxSchemaBytes = 64 });
        await Assert.ThrowsAsync<InvalidSchemaException>(() => sizeBound.RegisterAsync(
            $$"""{"type":"object","description":"{{new string('x', 256)}}"}""").AsTask());
        var depthBound = new InMemorySchemaRegistry(new SchemaRegistryOptions { MaxNestingDepth = 5 });
        await Assert.ThrowsAsync<InvalidSchemaException>(() => depthBound.RegisterAsync(Deep(20)).AsTask());
    }

    [Fact]
    public async Task CatastrophicPatternAbortsFailClosed()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await registry.RegisterAsync(
            """{"type":"object","properties":{"x":{"type":"string","pattern":"^(a+)+$"}}}""");
        var stopwatch = Stopwatch.StartNew();
        var result = await registry.ValidateAsync(schema.Id, Utf8($$"""{"x":"{{new string('a', 32)}}!"}"""));
        Assert.False(result.IsValid);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Contains(result.Errors, error => error.Code is "pattern-timeout");
    }

    [Fact]
    public async Task CancellationIsObserved()
    {
        var registry = new InMemorySchemaRegistry();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => registry.RegisterAsync(PersonSchema, cancellationToken: cancellation.Token).AsTask());
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static async Task<IReadOnlyList<SchemaRecord>> Collect(IAsyncEnumerable<SchemaRecord> source)
    {
        var result = new List<SchemaRecord>();
        await foreach (var item in source) result.Add(item);
        return result;
    }

    private static string Deep(int depth)
        => string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);
}
