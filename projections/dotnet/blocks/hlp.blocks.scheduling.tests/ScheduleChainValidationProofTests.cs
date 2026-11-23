using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleChainValidationProofTests
{
    private static RelativeChainDefinition Valid() => new("visits", 1, "visit",
        [new("a", null, "PT0S", new("PT1H", "PT1H"), null),
         new("b", "a", "PT1H", new("PT1H", "PT1H"), null)]);

    private static void Accepted(RelativeChainDefinition definition)
    {
        var nodes = RelativeChainValidator.Validate(definition, out var failures);
        Assert.Empty(failures);
        Assert.Equal(2, nodes.Count);
        Assert.Equal("a", nodes[0].NodeId);
        Assert.Null(nodes[0].AfterNodeId);
        Assert.Equal("PT0S", nodes[0].Offset);
        Assert.Equal("b", nodes[1].NodeId);
        Assert.Equal("a", nodes[1].AfterNodeId);
        Assert.Equal("PT1H", nodes[1].Offset);
    }

    private static void Refused(RelativeChainDefinition definition, string pointer)
    {
        var nodes = RelativeChainValidator.Validate(definition, out var failures);
        Assert.Empty(nodes);
        var failure = Assert.Single(failures);
        Assert.Equal(RelativeChainFailureCode.UnsupportedDefinition, failure.Code);
        Assert.Equal(new[] { "scheduling.definition.basis_invalid", pointer }, failure.ExplanationRefs);
    }

    [Fact(DisplayName = "scheduling-eng-3 / scheduling-run-2 validation-bridge preserves authored order")]
    public void ValidOrder() => Accepted(Valid());

    [Theory(DisplayName = "scheduling-eng-3 / scheduling-run-2 validation-bridge carries structural refusals")]
    [InlineData(false, "/basis/nodes/1/after_node_id")]
    [InlineData(true, "/basis/nodes/1/node_id")]
    public void StructuralRefusal(bool duplicate, string pointer)
    {
        var valid = Valid();
        var second = valid.Nodes[1];
        var invalid = valid with { Nodes = [valid.Nodes[0], duplicate ? second with { NodeId = "a" } : second with { AfterNodeId = "missing" }] };
        Refused(invalid, pointer);
        Accepted(invalid with { Nodes = [invalid.Nodes[0], duplicate ? invalid.Nodes[1] with { NodeId = "b" } : invalid.Nodes[1] with { AfterNodeId = "a" }] });
    }

    [Theory(DisplayName = "scheduling-eng-3 / scheduling-run-2 validation-bridge refuses invalid revision")]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidRevision(int revision)
    {
        var invalid = Valid() with { DefinitionRevision = revision };
        Refused(invalid, "/definition");
        Accepted(invalid with { DefinitionRevision = 1 });
    }

    [Theory(DisplayName = "scheduling-eng-3 / scheduling-run-2 validation-bridge refuses blank id")]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankId(string id)
    {
        var invalid = Valid() with { ChainDefinitionId = id };
        Refused(invalid, "/definition");
        Accepted(invalid with { ChainDefinitionId = "visits" });
    }
}
