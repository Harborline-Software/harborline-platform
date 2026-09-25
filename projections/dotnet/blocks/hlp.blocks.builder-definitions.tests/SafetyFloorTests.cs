using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>T-591: raise-only safety floors at reattachment and at authoring.</summary>
public sealed class SafetyFloorTests
{
    private const string Seed = """{"safetyFloors":{"retention":3,"audit":4,"review":2}}""";

    private static readonly string[] Members = ["retention", "audit", "review"];

    private static JsonNode Json(string text) => JsonNode.Parse(text)!;

    [Fact(DisplayName = "rules-auth-26: reattachment preserves every seeded floor against a lower integer, a missing key and a removed object, reports each clamp, refuses a malformed member by name, and keeps a valid raise")]
    public void Reattachment_clamps_reports_and_refuses()
    {
        var lowered = PackageSafetyFloorReattachment.Apply(Json(Seed), Json("""{"safetyFloors":{"retention":1,"audit":9}}"""));
        Assert.True(lowered.Succeeded);
        Assert.Equal(3, lowered.Content!["safetyFloors"]!["retention"]!.GetValue<int>());
        Assert.Equal(9, lowered.Content["safetyFloors"]!["audit"]!.GetValue<int>());
        Assert.Equal(2, lowered.Content["safetyFloors"]!["review"]!.GetValue<int>());
        Assert.Equal(
            [new PackageSafetyFloorClamp("retention", 3, 1), new PackageSafetyFloorClamp("review", 2, null)],
            lowered.Clamps);

        var removed = PackageSafetyFloorReattachment.Apply(Json(Seed), Json("""{"name":"tenant"}"""));
        Assert.Equal([3, 4, 2], Members.Select(m => removed.Content!["safetyFloors"]![m]!.GetValue<int>()));
        Assert.Equal(["retention", "audit", "review"], removed.Clamps.Select(clamp => clamp.Member));

        var malformed = PackageSafetyFloorReattachment.Apply(Json(Seed), Json("""{"safetyFloors":{"retention":"strict","audit":4,"review":2}}"""));
        Assert.Equal((false, "platform-package-safety-floor-malformed", "retention"), (malformed.Succeeded, malformed.RefusalCode, malformed.Member));

        var raised = PackageSafetyFloorReattachment.Apply(Json(Seed), Json("""{"safetyFloors":{"retention":5,"audit":4,"review":2}}"""));
        Assert.Equal(5, raised.Content!["safetyFloors"]!["retention"]!.GetValue<int>());
        Assert.Empty(raised.Clamps);
    }

    [Fact(DisplayName = "rules-auth-8: a sealed floor read reports its writability and reason before any write; without the author-floor grant it is not writable and authoring refuses without mutating")]
    public void Sealed_floor_writability_is_reported_on_read()
    {
        Assert.Equal(
            [new SafetyFloorMemberState("retention", 3, true, SafetyFloorAuthoring.RaiseOnly),
             new SafetyFloorMemberState("audit", 4, true, SafetyFloorAuthoring.RaiseOnly),
             new SafetyFloorMemberState("review", 2, true, SafetyFloorAuthoring.RaiseOnly)],
            SafetyFloorAuthoring.Describe(Json(Seed), authorFloorAllowed: true));
        Assert.All(SafetyFloorAuthoring.Describe(Json(Seed), authorFloorAllowed: false),
            state => Assert.Equal((false, SafetyFloorAuthoring.AuthorFloorDenied), (state.Writable, state.Reason)));

        var candidate = Json("""{"safetyFloors":{"retention":5,"audit":4,"review":2}}""");
        var before = candidate.ToJsonString();
        var denied = SafetyFloorAuthoring.Author(Json(Seed), candidate, authorFloorAllowed: false);
        Assert.Equal([new DefinitionRefusal(SafetyFloorAuthoring.AuthorFloorDenied, "/safetyFloors")], denied.Refusals);
        Assert.Null(denied.Content);
        Assert.Equal(before, candidate.ToJsonString());
    }

    [Fact(DisplayName = "rules-auth-8: authoring a floor may raise and never lower: three independent invalid members return exactly three code/pointer refusals and no content, and the valid counterpart has none")]
    public void Authoring_refuses_every_lowering_by_pointer()
    {
        var invalid = SafetyFloorAuthoring.Author(Json(Seed),
            Json("""{"safetyFloors":{"retention":1,"audit":"strict"}}"""), authorFloorAllowed: true);
        Assert.Equal(
            [new DefinitionRefusal(SafetyFloorAuthoring.Lowered, "/safetyFloors/retention"),
             new DefinitionRefusal(SafetyFloorAuthoring.Malformed, "/safetyFloors/audit"),
             new DefinitionRefusal(SafetyFloorAuthoring.Lowered, "/safetyFloors/review")],
            invalid.Refusals);
        Assert.Null(invalid.Content);

        Assert.Equal([new DefinitionRefusal(SafetyFloorAuthoring.Lowered, "/safetyFloors/retention"),
                      new DefinitionRefusal(SafetyFloorAuthoring.Lowered, "/safetyFloors/audit"),
                      new DefinitionRefusal(SafetyFloorAuthoring.Lowered, "/safetyFloors/review")],
            SafetyFloorAuthoring.Author(Json(Seed), Json("""{"name":"x"}"""), authorFloorAllowed: true).Refusals);

        var valid = SafetyFloorAuthoring.Author(Json(Seed),
            Json("""{"safetyFloors":{"retention":3,"audit":6,"review":2,"extra":1}}"""), authorFloorAllowed: true);
        Assert.Empty(valid.Refusals);
        Assert.Equal(6, valid.Content!["safetyFloors"]!["audit"]!.GetValue<int>());
    }
}
