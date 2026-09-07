using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class ValidationParityCorpusTests
{
    private static readonly string CorpusPath = Path.Combine(AppContext.BaseDirectory, "ValidationParityCorpus", "cases.json");

    public static IEnumerable<object[]> Cases()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(CorpusPath));
        foreach (var row in corpus.RootElement.GetProperty("cases").EnumerateArray())
        {
            var expected = row.GetProperty("expected");
            var codes = expected.TryGetProperty("errorCodesServer", out var serverCodes)
                ? serverCodes.EnumerateArray().Select(value => value.GetString()!).ToArray()
                : expected.GetProperty("errorCodes").EnumerateArray().Select(value => value.GetString()!).ToArray();
            yield return
            [
                row.GetProperty("name").GetString()!,
                row.GetProperty("candidate").GetRawText(),
                row.GetProperty("rules").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                codes,
                expected.GetProperty("prunedKeys").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            ];
        }
    }

    [Fact]
    public void Corpus_is_non_trivial()
    {
        var cases = Cases().ToArray();
        Assert.Equal(10, cases.Length);
        Assert.Contains(cases, row => ((string)row[0]).Contains("HIDDEN SECTION", StringComparison.Ordinal));
        Assert.Contains(cases, row => ((string)row[0]).Contains("read-only required", StringComparison.Ordinal));
        Assert.Contains(cases, row => ((string)row[0]).Contains("clean pass", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case_matches_expected_codes_and_pruned_keys(
        string name,
        string candidateJson,
        string[] ruleIds,
        string[] expectedCodes,
        string[] expectedPrunedKeys)
    {
        _ = name;
        using var corpus = JsonDocument.Parse(File.ReadAllText(CorpusPath));
        var schemas = new InMemorySchemaRegistry();
        var schema = await schemas.RegisterAsync(corpus.RootElement.GetProperty("schema").GetRawText());
        var definition = BuildDefinition(corpus.RootElement, schema.Id.Value, ruleIds);
        using var candidate = JsonDocument.Parse(candidateJson);
        var admin = RoleReference.Domain("admin");
        var vocabulary = RoleVocabulary.FromApi([new(
            Guid.Parse("bbbd7893-58b7-41ec-96dd-1953def39fe1"), admin, "Admin",
            new(RoleOwnerKind.Package, "forms-engine-tests"), false)]);
        using var result = await FormCandidateEvaluator.EvaluateAsync(
            new(new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice", ["admin"], vocabulary, new HeldRoleSet([admin])),
            definition,
            candidate,
            schemas,
            FormEngineOptions.DefaultMaximumCandidateBytes,
            CancellationToken.None);

        var actualCodes = result.Errors.Select(row => row.Code.HasValue ? row.Code.Value! : "").Order(StringComparer.Ordinal).ToArray();
        var accepted = result.AcceptedCandidate.RootElement.EnumerateObject().Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var actualPruned = candidate.RootElement.EnumerateObject().Select(row => row.Name).Where(name => !accepted.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedCodes.Order(StringComparer.Ordinal), actualCodes);
        Assert.Equal(expectedPrunedKeys.Order(StringComparer.Ordinal), actualPruned);
    }

    private static FormDefinition BuildDefinition(JsonElement corpus, string schemaId, IReadOnlyCollection<string> selectedRules)
    {
        var fields = corpus.GetProperty("schema").GetProperty("properties").EnumerateObject()
            .ToDictionary(row => row.Name, row => new FieldOverlay(InternationalizedText.FromInvariant(row.Name)), StringComparer.Ordinal);
        var sections = corpus.GetProperty("sections").EnumerateArray().Select(section => new FormSection(
            section.GetProperty("id").GetString()!,
            InternationalizedText.FromInvariant(section.GetProperty("id").GetString()!),
            section.GetProperty("fields").EnumerateArray().Select(row => row.GetString()!).ToArray(),
            new([Harborline.Contracts.Authorization.RoleReference.Domain("admin")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]),
            Items: section.TryGetProperty("items", out var items) ? items.EnumerateArray().Select(ToItem).ToArray() : null)).ToArray();
        var catalog = corpus.GetProperty("ruleCatalog");
        var rules = selectedRules.Select(id => ToRule(catalog.GetProperty(id))).ToArray();
        var pages = corpus.GetProperty("pages").EnumerateArray().Select(page => new FormPage(
            page.GetProperty("id").GetString()!,
            InternationalizedText.FromInvariant(page.GetProperty("id").GetString()!),
            page.GetProperty("sections").EnumerateArray().Select(row => row.GetString()!).ToArray(),
            page.TryGetProperty("visibleWhen", out var guard) ? guard.GetString() : null,
            page.TryGetProperty("checks", out var checks) ? checks.EnumerateArray().Select(row => row.GetString()!).ToArray() : null)).ToArray();
        var now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
        return new(new("parity"), new(1, 0, 0), FormDefinitionStatus.Published, new("tenant-engine"), IdentityRef.System, new(schemaId),
            new(fields, sections, rules, Pages: pages), null, now, now);
    }

    private static FormItem ToItem(JsonElement item)
    {
        var kind = item.GetProperty("kind").GetString();
        return kind switch
        {
            "field" => FormItem.OfField(item.GetProperty("key").GetString()!),
            "group" => FormItem.OfGroup(item.GetProperty("key").GetString()!, item.GetProperty("items").EnumerateArray().Select(ToItem).ToArray()),
            _ => throw new InvalidOperationException($"Unsupported corpus item kind '{kind}'."),
        };
    }

    private static RuleDefinition ToRule(JsonElement rule) => new(
        rule.GetProperty("id").GetString()!,
        Enum.Parse<RuleTier>(rule.GetProperty("tier").GetString()!),
        Enum.Parse<RuleScope>(rule.GetProperty("scope").GetString()!),
        rule.GetProperty("scopeTarget").GetString()!,
        rule.GetProperty("expression").GetString()!,
        Enum.Parse<RuleActionKind>(rule.GetProperty("action").GetString()!));
}
