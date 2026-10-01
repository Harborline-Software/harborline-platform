using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Conformance;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-1018: deterministic reactive edits produce the same public result as fresh evaluation.</summary>
public sealed class ReactiveDifferentialTests
{
    private static readonly DateTimeOffset Clock = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reevaluate_matches_fresh_evaluation_after_200_deterministic_field_and_table_edit_sequences()
    {
        var rules = new[]
        {
            Compute("field.adjusted", "adjusted", "{\"*\":[{\"var\":\"amount\"},{\"var\":\"multiplier\"}]}"),
            Compute("field.total", "total", "{\"var\":\"table.sum(items.line)\"}"),
            Compute("row.line", "items/line", "{\"*\":[{\"var\":\"row.qty\"},{\"var\":\"row.price\"}]}", RuleScope.Row),
            Compute("table.lineTotal", "items/sum/lineTotal", "{\"var\":\"table.sum(items.line)\"}", RuleScope.Table),
            RuleDefinitionFactory.Create("validate.amount", RuleTier.JsonLogic, RuleScope.Field, "amount", "{\">=\":[{\"var\":\"amount\"},0]}", RuleActionKind.Validate),
            RuleDefinitionFactory.Create("visible.summary", RuleTier.JsonLogic, RuleScope.Field, "summary", "{\">\":[{\"var\":\"table.sum(items.line)\"},0]}", RuleActionKind.Visibility),
            RuleDefinitionFactory.Create("options.mode", RuleTier.JsonLogic, RuleScope.Field, "mode", "{\"if\":[{\"var\":\"mode\"},[\"on\"],[\"off\"]]}", RuleActionKind.Options),
        };
        var compiled = RuleCompiler.Compile(rules);
        for (var seed = 1; seed <= 200; seed++)
        {
            var random = new Random(seed);
            var data = JsonNode.Parse("{\"amount\":1,\"multiplier\":2,\"mode\":false,\"items\":[{\"_id\":\"r0\",\"qty\":1,\"price\":2}]}")!.AsObject();
            var graph = Graph(compiled);
            graph.EvaluateInstance(RuleInstance.FromJson(data.DeepClone().AsObject()));
            for (var step = 0; step < 12; step++)
            {
                var kind = random.Next(4);
                RuleEvaluationResult actual;
                var rows = data["items"]!.AsArray();
                if (kind == 0)
                {
                    var field = random.Next(3) switch { 0 => "amount", 1 => "multiplier", _ => "mode" };
                    JsonNode? value = field == "mode" ? JsonValue.Create(random.Next(2) == 0) : JsonValue.Create(random.Next(-2, 7));
                    data[field] = value;
                    actual = graph.Reevaluate(field, RuleInputValue.FromJsonText(value!.ToJsonString()));
                }
                else if (kind == 1 || rows.Count == 0)
                {
                    var id = $"s{seed}-{step}";
                    var qty = 1 + random.Next(4);
                    var price = random.Next(9);
                    rows.Add(new JsonObject { ["_id"] = id, ["qty"] = qty, ["price"] = price });
                    actual = graph.AddRow("items", Row(id, qty, price));
                }
                else if (kind == 2)
                {
                    var index = random.Next(rows.Count);
                    var removed = rows[index]!.AsObject();
                    rows.RemoveAt(index);
                    actual = graph.RemoveRow("items", removed["_id"]!.GetValue<string>());
                }
                else
                {
                    var index = random.Next(rows.Count);
                    var id = rows[index]!["_id"]!.GetValue<string>();
                    var qty = 1 + random.Next(4);
                    var price = random.Next(9);
                    rows[index] = new JsonObject { ["_id"] = id, ["qty"] = qty, ["price"] = price };
                    graph.RemoveRow("items", id);
                    actual = graph.AddRow("items", Row(id, qty, price));
                }
                var expected = Graph(compiled).EvaluateInstance(RuleInstance.FromJson(data.DeepClone().AsObject()));
                Assert.Equal(Projection(expected), Projection(actual));
            }
        }
    }

    [Fact]
    public void Reevaluate_tracks_field_prefixed_dynamic_reads_independently_of_the_key_list()
    {
        var graph = Graph(RuleCompiler.Compile([Compute("dynamic.missing", "missing", "{\"missing\":[{\"var\":\"keys\"}]}")]));
        graph.EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse("{\"keys\":[\"field.amount\"],\"amount\":1}")!.AsObject()));

        var next = graph.Reevaluate("amount", RuleInputValue.FromJsonText("null"));

        // Oracle: JsonLogic missing reports the requested path when its value is null.
        Assert.Contains("field.amount", CanonicalJson.SerializeComputedValue(next.Values["field:missing"]), StringComparison.Ordinal);
    }

    private static RuleDefinition Compute(string id, string target, string expression, RuleScope scope = RuleScope.Field)
        => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, scope, target, expression, RuleActionKind.Compute);

    private static FormRuleGraph Graph(CompiledGraph compiled)
        => new(compiled, new FixedClock(Clock), TestAdmission.Any);

    private static RuleRow Row(string id, int qty, int price)
        => new(id, new Dictionary<string, JsonNode?> { ["qty"] = JsonValue.Create(qty), ["price"] = JsonValue.Create(price) });

    private static string Projection(RuleEvaluationResult result)
    {
        var outcomes = string.Join("|", result.ByRule.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + CanonicalJson.SerializeOutcome(pair.Value)));
        var values = string.Join("|", result.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + CanonicalJson.SerializeComputedValue(pair.Value)));
        var visibility = string.Join("|", result.Visibility.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value.Visible}:{pair.Value.Required}:{pair.Value.ReadOnly}"));
        var validations = string.Join("|", result.Validations.Select(CanonicalJson.SerializeOutcome).OrderBy(value => value, StringComparer.Ordinal));
        var options = string.Join("|", result.Options.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value.State}"));
        return $"{outcomes};{values};{visibility};{validations};{options};{result.HasPending};{result.IsSaveBlocked}";
    }
}
