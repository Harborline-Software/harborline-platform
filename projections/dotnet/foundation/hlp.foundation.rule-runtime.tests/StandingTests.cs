using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Standings;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-591: the platform standing rule, its store, and authorized set evaluation.</summary>
public sealed class StandingTests
{
    internal static RuleDefinition Predicate(string id, string expression)
        => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, RuleScope.Schema, "", expression, RuleActionKind.Validate);

    internal static StandingRuleDefinition Rule(string id, string standing, string expression, params string[] inputs)
        => new(id, "1.0.0", new StandingReference(standing), "asset", inputs, Predicate(id, expression));

    [Fact(DisplayName = "rules-ck-21: a standing rule is a schema-scoped validation predicate whose declared inputs name exactly the fields it reads")]
    public void Standing_rule_declares_exactly_the_fields_it_reads()
    {
        var rule = Rule("overdue", "overdue", """{">":[{"var":"daysLate"},30]}""", "daysLate");
        Assert.Equal(["daysLate"], rule.InputFields);
        Assert.Equal("overdue", rule.Standing.Name);

        // An undeclared read, an unread declaration, a non-predicate action and a mismatched id each refuse.
        Assert.Throws<ArgumentException>(() => Rule("a", "s", """{">":[{"var":"daysLate"},{"var":"grace"}]}""", "daysLate"));
        Assert.Throws<ArgumentException>(() => Rule("b", "s", """{">":[{"var":"daysLate"},30]}""", "daysLate", "grace"));
        Assert.Throws<ArgumentException>(() => new StandingRuleDefinition("c", "1.0.0", new StandingReference("s"), "asset", ["x"],
            RuleDefinitionFactory.Create("c", RuleTier.JsonLogic, RuleScope.Schema, "", """{"var":"x"}""", RuleActionKind.Compute)));
        Assert.Throws<ArgumentException>(() => new StandingRuleDefinition("d", "1.0.0", new StandingReference("s"), "asset", ["x"],
            Predicate("other", """{"var":"x"}""")));
    }

    [Fact(DisplayName = "rules-ck-21: the standing store replays an identical version, refuses different content under the same version, and lists in identity order")]
    public async Task Standing_store_is_immutable_per_version()
    {
        var store = new InMemoryStandingRuleDefinitionStore();
        var overdue = Rule("overdue", "overdue", """{">":[{"var":"daysLate"},30]}""", "daysLate");
        await store.RegisterAsync(Rule("zeta", "z", """{"==":[{"var":"k"},1]}""", "k"));
        await store.RegisterAsync(overdue);
        await store.RegisterAsync(Rule("overdue", "overdue", """{">":[{"var":"daysLate"},30]}""", "daysLate"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.RegisterAsync(Rule("overdue", "overdue", """{">":[{"var":"daysLate"},60]}""", "daysLate")));

        Assert.Same(overdue, await store.GetAsync("overdue", "1.0.0"));
        var listed = new List<string>();
        await foreach (var row in store.ListAsync()) listed.Add(row.RuleId);
        Assert.Equal(["overdue", "zeta"], listed);
        Assert.True(await store.RemoveAsync("zeta", "1.0.0"));
        Assert.Null(await store.GetAsync("zeta", "1.0.0"));
    }
}
