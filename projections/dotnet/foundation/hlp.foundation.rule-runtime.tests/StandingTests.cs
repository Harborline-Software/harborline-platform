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

    private static readonly DateTimeOffset At = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    private static StandingRecord Row(string id, string status, int amount)
        => new(id, "asset", new Dictionary<string, JsonNode?> { ["status"] = status, ["amount"] = amount, ["id"] = id });

    private static ValueTask<bool> Everyone(StandingRecord row, CancellationToken ct) => ValueTask.FromResult(true);

    [Fact(DisplayName = "rules-eng-20: 1,000 rows sharing one invariant predicate take one predicate evaluation and yield 1,000 classified outcomes; a distinct-record predicate takes 1,000 and classifies each row by its own value")]
    public async Task Standing_set_evaluation_is_sublinear_in_shared_inputs_and_exact_per_record()
    {
        // Workload stated before running: 1,000 asset rows, all status "open", amounts 0..999.
        var rows = Enumerable.Range(0, 1000).Select(i => Row("a" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), "open", i)).ToArray();
        var open = Rule("open", "open", """{"==":[{"var":"status"},"open"]}""", "status");
        var large = Rule("large", "large", """{">":[{"var":"amount"},500]}""", "amount");
        var evaluator = new StandingEvaluator();

        // Invariant predicate: expect exactly 1 evaluation and 1,000 classified outcomes.
        var shared = await evaluator.EvaluateSetAsync(Grants.All, rows, Everyone, [open], TestAdmission.Any, At, 0, 1000);
        Assert.Equal(1, shared.PredicateEvaluations);
        Assert.Equal(1000, shared.VisibleCount);
        Assert.Equal(1000, shared.Page.Count);
        Assert.All(shared.Page, row => Assert.Equal([new StandingReference("open")], row.Standings));
        Assert.Equal(1000, shared.Counts[new StandingReference("open")]);

        // Distinct-record predicate: a cache keyed on anything but the rule's inputs would misclassify here.
        var distinct = await evaluator.EvaluateSetAsync(Grants.All, rows, Everyone, [large], TestAdmission.Any, At, 0, 1000);
        Assert.Equal(1000, distinct.PredicateEvaluations);
        Assert.Equal(499, distinct.Counts[new StandingReference("large")]);
        Assert.Empty(distinct.Page.Single(row => row.RecordId == "a0500").Standings);
        Assert.Equal([new StandingReference("large")], distinct.Page.Single(row => row.RecordId == "a0501").Standings);

        var both = await evaluator.EvaluateSetAsync(Grants.All, rows, Everyone, [open, large], TestAdmission.Any, At, 0, 10);
        Assert.Equal(1001, both.PredicateEvaluations);
        Assert.Equal(2, both.Page[0].Decisions.Count);
    }

    [Fact(DisplayName = "rules-eng-20: interleaved unauthorized rows are removed before standing evaluation, counting and paging, so they never alter visible counts, pages or the evaluation count")]
    public async Task Unauthorized_rows_never_reach_standing_evaluation_counts_or_pages()
    {
        var authorized = Enumerable.Range(0, 1000).Select(i => Row("a" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), "open", i)).ToArray();
        // Each denied row carries the standing with a value no authorized row has.
        var interleaved = authorized.SelectMany((row, i) => new[] { Row("d" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), "open", 5000 + i), row }).ToArray();
        var large = Rule("large", "large", """{">":[{"var":"amount"},500]}""", "amount");
        var evaluator = new StandingEvaluator();
        ValueTask<bool> Access(StandingRecord row, CancellationToken ct) => ValueTask.FromResult(row.RecordId.StartsWith('a', StringComparison.Ordinal));

        var expected = await evaluator.EvaluateSetAsync(Grants.All, authorized, Everyone, [large], TestAdmission.Any, At, 490, 20);
        var actual = await evaluator.EvaluateSetAsync(Grants.All, interleaved, Access, [large], TestAdmission.Any, At, 490, 20);

        Assert.Equal(1000, actual.VisibleCount);
        Assert.Equal(expected.Counts[new StandingReference("large")], actual.Counts[new StandingReference("large")]);
        Assert.Equal(expected.Page.Select(row => row.RecordId), actual.Page.Select(row => row.RecordId));
        Assert.Equal(1000, actual.PredicateEvaluations);
    }

    [Fact(DisplayName = "rules-eng-26: standing set evaluation refuses without admission before reading any row")]
    public async Task Standing_set_evaluation_requires_admission()
    {
        var rule = Rule("open", "open", """{"==":[{"var":"status"},"open"]}""", "status");
        var result = await new StandingEvaluator().EvaluateSetAsync(Grants.All, [Row("a", "open", 1)], Everyone, [rule], null, At, 0, 10);
        var decision = Assert.Single(Assert.Single(result.Page).Decisions);
        Assert.False(decision.Carries);
        Assert.Equal(Environments.BorrowerEnvironmentAdmission.NotAdmitted, decision.RefusalCode);
    }
}

/// <summary>Host-supplied Access verdicts over capability names (test double for the host gate).</summary>
internal static class Grants
{
    public static RulesCapabilityCheck All { get; } = (_, _) => ValueTask.FromResult(true);

    public static RulesCapabilityCheck Only(params string[] granted)
        => (permission, _) => ValueTask.FromResult(granted.Contains(permission, StringComparer.Ordinal));
}

/// <summary>T-591 (owner ruling Q14): the Rules capability names and the evaluate/explain boundary.</summary>
public sealed class RulesPermissionTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "rules-auth-34: Rules declares exactly four permissions, rules:author, rules:publish, rules:author-floor and rules:evaluate-explain, and seeds no role grants")]
    public void Rules_declares_its_four_capabilities()
        => Assert.Equal(["rules:author", "rules:publish", "rules:author-floor", "rules:evaluate-explain"], RulesPermissions.All);

    [Theory(DisplayName = "rules-eng-20: standing set evaluation needs both rules:evaluate-explain and records:read at the operation boundary, and refuses before any row is authorized or read")]
    [InlineData("rules:evaluate-explain")]
    [InlineData("records:read")]
    public async Task Standing_evaluation_needs_evaluate_explain_and_records_read(string only)
    {
        var rule = StandingTests.Rule("open", "open", """{"==":[{"var":"status"},"open"]}""", "status");
        var row = new StandingRecord("a", "asset", new Dictionary<string, JsonNode?> { ["status"] = "open" });
        int rowChecks = 0;
        ValueTask<bool> Count(StandingRecord record, CancellationToken ct) { rowChecks++; return ValueTask.FromResult(true); }
        var evaluator = new StandingEvaluator();

        var refused = await Assert.ThrowsAsync<RulesPermissionException>(async () =>
            await evaluator.EvaluateSetAsync(Grants.Only(only), [row], Count, [rule], TestAdmission.Any, At, 0, 10));
        Assert.Equal(RulesPermissions.DeniedCode, refused.Code);
        Assert.Equal(0, rowChecks);

        var allowed = await evaluator.EvaluateSetAsync(Grants.Only(RulesPermissions.EvaluateExplain, RulesPermissions.RecordsRead), [row], Count, [rule], TestAdmission.Any, At, 0, 10);
        Assert.Equal(1, allowed.VisibleCount);
    }

    [Theory(DisplayName = "rules-eng-19: the standing evidence read needs both rules:evaluate-explain and records:read before its own evidence-read check")]
    [InlineData("rules:evaluate-explain")]
    [InlineData("records:read")]
    public async Task Evidence_read_needs_evaluate_explain_and_records_read(string only)
    {
        var rule = StandingTests.Rule("open", "open", """{"==":[{"var":"status"},"open"]}""", "status");
        var row = new StandingRecord("a", "asset", new Dictionary<string, JsonNode?> { ["status"] = "open" });
        static ValueTask<bool> Allow(StandingRecord record, CancellationToken ct) => ValueTask.FromResult(true);
        var evaluator = new StandingEvaluator();

        Assert.Equal(StandingEvidenceAvailability.Refused,
            (await evaluator.ReadEvidenceAsync(Grants.Only(only), row, rule, Allow, TestAdmission.Any, At)).Availability);
        Assert.Equal(StandingEvidenceAvailability.Available,
            (await evaluator.ReadEvidenceAsync(Grants.Only(RulesPermissions.EvaluateExplain, RulesPermissions.RecordsRead), row, rule, Allow, TestAdmission.Any, At)).Availability);
    }
}
