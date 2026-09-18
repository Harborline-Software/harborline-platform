using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class PackageSafetyFloorReattachmentTests
{
    [Fact]
    public void Lower_integer_is_clamped_to_the_seed_floor()
    {
        var result = Reattach("""{"safetyFloors":{"retention":3}}""", """{"safetyFloors":{"retention":2}}""");

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Content!["safetyFloors"]!["retention"]!.GetValue<int>());
    }

    [Fact]
    public void Missing_member_is_restored_from_the_seed_floor()
    {
        var result = Reattach("""{"safetyFloors":{"retention":3,"audit":4}}""", """{"safetyFloors":{"audit":5}}""");

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Content!["safetyFloors"]!["retention"]!.GetValue<int>());
        Assert.Equal(5, result.Content["safetyFloors"]!["audit"]!.GetValue<int>());
    }

    [Fact]
    public void Removed_floor_object_is_restored_from_the_seed()
    {
        var result = Reattach("""{"safetyFloors":{"retention":3},"name":"seed"}""", """{"name":"tenant"}""");

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Content!["safetyFloors"]!["retention"]!.GetValue<int>());
        Assert.Equal("tenant", result.Content["name"]!.GetValue<string>());
    }

    [Fact]
    public void Present_string_member_is_refused_with_the_member_named()
    {
        var result = Reattach("""{"safetyFloors":{"retention":3}}""", """{"safetyFloors":{"retention":"strict"}}""");

        Assert.False(result.Succeeded);
        Assert.Equal("platform-package-safety-floor-malformed", result.RefusalCode);
        Assert.Equal("retention", result.Member);
        Assert.Null(result.Content);
    }

    private static PackageSafetyFloorReattachmentResult Reattach(string seed, string merged)
        => PackageSafetyFloorReattachment.Apply(JsonNode.Parse(seed)!, JsonNode.Parse(merged)!);
}
