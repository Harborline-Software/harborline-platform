using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

public sealed class RuleDefinitionIntentConformanceTests
{
    public static IEnumerable<object?[]> Cases()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "definition-intent-cases.json")));
        foreach (var row in corpus.RootElement.GetProperty("cases").EnumerateArray())
        {
            var expected = row.GetProperty("expected");
            foreach (var phase in new[] { RuleIntentPhase.Author, RuleIntentPhase.Publish, RuleIntentPhase.Persisted })
                yield return new object?[]
                {
                    row.GetProperty("id").GetString(), row.GetProperty("sourceJson").GetString(), phase,
                    expected.GetProperty("valid").GetBoolean(), expected.GetProperty("code").GetString(),
                    expected.GetProperty("location").GetString(),
                    expected.TryGetProperty("canonicalJson", out var canonical) ? canonical.GetString() : null,
                };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BothProjectionsConsumeTheSameLocatedIntentContract(
        string id, string source, RuleIntentPhase phase, bool valid, string? code, string? location,
        string? expectedCanonical)
    {
        var result = RuleIntentValidator.ValidateJson(source, phase);

        Assert.True(result.IsValid == valid, $"{id}/{phase}: unexpected validity");
        if (valid)
        {
            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Document);
            string canonical = RuleDefinitionCodec.SerializeCanonical(result.Document);
            if (expectedCanonical is not null) Assert.Equal(expectedCanonical, canonical);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(canonical)),
                $"{id}/{phase}: round-trip changed the authored source");
        }
        else
        {
            Assert.Null(result.Document);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(code, diagnostic.Code);
            Assert.Equal(location, diagnostic.Location);
            Assert.Equal(phase, diagnostic.Phase);
        }
    }
}
