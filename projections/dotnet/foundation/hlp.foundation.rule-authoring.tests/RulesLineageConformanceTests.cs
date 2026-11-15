using System.Text.Json;
using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

public sealed class RulesLineageConformanceTests
{
    [Fact(DisplayName = "rules-auth-19: shared lineage matches the producer")]
    public void SharedLineageMatchesProducer()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "lineage.json")));
        var root = fixture.RootElement;
        var documents = root.GetProperty("documents").EnumerateArray().Select(row => RuleIntentValidator.ValidateJson(row.GetRawText(), RuleIntentPhase.Author).Document ?? throw new InvalidOperationException("Fixture document refused"));
        var actual = RulesLineage.Build(documents);
        var expected = root.GetProperty("lineage");
        AssertMap(expected.GetProperty("consumersOf"), actual.ConsumersOf);
        AssertMap(expected.GetProperty("calculationsReadBy"), actual.CalculationsReadBy);
        Assert.Equal(expected.GetProperty("deadWeight").EnumerateArray().Select(x => x.GetString()), actual.DeadWeight);
    }
    private static void AssertMap(JsonElement expected, IReadOnlyDictionary<string, IReadOnlyList<string>> actual)
    {
        Assert.Equal(expected.EnumerateObject().Count(), actual.Count);
        foreach (var property in expected.EnumerateObject())
            Assert.Equal(property.Value.EnumerateArray().Select(x => x.GetString()), actual[property.Name]);
    }
}
