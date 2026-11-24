using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Foundation.Definitions.Tests;

// Oracle: T-491/solver-refusal-detail acceptance 1 and DES-0014 C3 at control 4d55e2e9.
public sealed class DefinitionRefusalDetailTests
{
    [Fact]
    public void DetailIsOptionalAndPreservesLegacySignature()
    {
        var refusal = new DefinitionRefusal("code", "/solver", "target");
        var (code, pointer, target) = refusal;
        Assert.Equal(("code", "/solver", "target"), (code, pointer, target));
        var property = typeof(DefinitionRefusal).GetProperty("Detail");
        Assert.NotNull(property);
        Assert.Null(property.GetValue(refusal));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(refusal));
        Assert.False(wire.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public void DetailHasStructuralValueEquality()
    {
        var first = Read("""{"observed":"missing","required":["a","b"]}""");
        var equal = Read("""{"required":["a","b"],"observed":"missing"}""");
        Assert.Equal(first, equal);
        Assert.Equal(equal, first);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, Read("""{"observed":"other","required":["a","b"]}"""));
        Assert.NotEqual(first, Read("""{"observed":"missing","required":["b","a"]}"""));
        Assert.NotEqual(first, Read("""{"observed":"missing"}"""));
        Assert.NotEqual(first, new DefinitionRefusal("code", "/solver"));
        Assert.NotEqual(first, first with { Code = "other" });
        Assert.NotEqual(first, first with { Pointer = "/other" });
        Assert.NotEqual(first, first with { Target = "other" });
        Assert.False(first.Equals((DefinitionRefusal?)null));
    }

    [Fact]
    public void DetailWritesMachineReadableWireObject()
    {
        var refusal = Read("""{"observed":11,"minimum":0,"maximum":10}""");
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(refusal));
        var detail = wire.RootElement.GetProperty("detail");
        Assert.Equal(JsonValueKind.Object, detail.ValueKind);
        Assert.Equal(11, detail.GetProperty("observed").GetInt32());
        Assert.Equal(0, detail.GetProperty("minimum").GetInt32());
        Assert.Equal(10, detail.GetProperty("maximum").GetInt32());
    }

    private static DefinitionRefusal Read(string detail)
        => JsonSerializer.Deserialize<DefinitionRefusal>("{\"Code\":\"code\",\"Pointer\":\"/solver\",\"detail\":" + detail + "}")!;
}
