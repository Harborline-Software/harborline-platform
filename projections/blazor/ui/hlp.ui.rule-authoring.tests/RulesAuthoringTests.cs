using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Bunit.Rendering;
using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Skins;
using Harborline.UIAdapters.Blazor.Components.RuleAuthoring;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class RulesAuthoringTests : BunitContext
{
    [Fact]
    [Trait("ModuleConformance", "hlp.ui.rule-authoring")]
    public void Replays_every_producer_lifecycle_and_preview_payload_from_the_exact_shared_fixture()
    {
        var sharedFixture = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (!string.IsNullOrWhiteSpace(sharedFixture))
        {
            using var fixtureDocument = JsonDocument.Parse(sharedFixture);
            Assert.StartsWith("rule-authoring.", fixtureDocument.RootElement.GetProperty("id").GetString());
        }

        using var document = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var previewFixture = document.RootElement.GetProperty("preview");
        var inputLabel = previewFixture.GetProperty("label").GetString()!;
        var clockUtc = previewFixture.GetProperty("clockUtc").GetString()!;
        var responses = document.RootElement.GetProperty("lifecycle").GetProperty("responses").EnumerateArray().ToArray();
        Assert.Equal(6, responses.Length);
        Assert.Equal(("Draft", 1, "amount-rule"), (responses[0].GetProperty("status").GetString(), responses[0].GetProperty("revision").GetInt32(), responses[0].GetProperty("identity").GetProperty("definitionId").GetString()));
        Assert.Equal(("Published", 2), (responses[1].GetProperty("status").GetString(), responses[1].GetProperty("revision").GetInt32()));
        Assert.Equal("definition.revision_conflict", responses[3].GetProperty("refusal").GetProperty("code").GetString());
        var materializedBinding = responses[4].GetProperty("materialization").GetProperty("bindings")[0];
        Assert.Equal(("fixture-v1", "1.0.0"), (materializedBinding.GetProperty("versionId").GetString(), materializedBinding.GetProperty("winningWatermark").GetString()));
        Assert.False(responses[5].GetProperty("listVisible").GetBoolean());

        foreach (var preview in previewFixture.GetProperty("cases").EnumerateArray())
        {
            var expected = preview.GetProperty("expected");
            var outcome = new RulesOutcome(expected.GetProperty("kind").GetString()!, inputLabel, clockUtc, Value: Read(expected, "value"), Code: Read(expected, "code"), RuleName: Read(expected, "ruleName"), MemberName: Read(expected, "memberName"), Validity: Read(expected, "validity"), Visibility: Read(expected, "visibility"), Presentation: Read(expected, "presentation"));
            var formula = (FormulaDraft)RulesDraft.Empty.Draft;
            var cut = Render<HarborlineRulesAuthoringEditor>(parameters => parameters
                .Add(component => component.Value, RulesDraft.Empty with { Identity = outcome.RuleName!, Draft = formula with { ScopeTarget = outcome.MemberName! } })
                .Add(component => component.Outcome, outcome));
            Assert.Contains(outcome.RuleName!, cut.Markup);
            Assert.Contains(outcome.Kind switch { "Value" => outcome.Value!, "Validity" => outcome.Validity!, "Visibility" => outcome.Visibility!, "Presentation" => outcome.Presentation!, "Pending" => "Preview pending.", _ => outcome.Code! }, cut.Markup);
        }
    }

    [Fact]
    public void Authors_producer_formula_and_table_types_from_empty()
    {
        RulesDraft? changed = null;
        var cut = RenderEditor(value => changed = value);
        cut.Find("input[aria-label='Rule literal value']").Change("ready");
        var formula = Assert.IsType<FormulaDraft>(changed!.Draft);
        Assert.Equal("ready", Assert.IsType<FormulaExpr.Literal>(formula.Expression).Value);

        cut.Find("input[aria-label='Decision table']").Change(true);
        cut.FindButton("Add column").Click();
        cut.Find("select[aria-label='Column 1 input']").Change("amount");
        cut.FindButton("Add row").Click();
        cut.Find("select[aria-label='Row 1 column-1 cell kind']").Change("Range");
        cut.Find("input[aria-label='Row 1 column-1 range lower bound']").Change("0");
        cut.Find("input[aria-label='Row 1 column-1 range upper bound']").Change("100");
        cut.Find("input[aria-label='Row 1 output']").Change("low");
        cut.Find("input[aria-label='Default output']").Change("high");
        var table = Assert.IsType<DecisionTableDraft>(changed!.Draft);
        Assert.Equal("amount", table.Columns[0].Input);
        Assert.Equal(("0", "100"), (Assert.IsType<TableCell.Range>(table.Rows[0].Cells["column-1"]).Lo, Assert.IsType<TableCell.Range>(table.Rows[0].Cells["column-1"]).Hi));
        Assert.Equal("low", table.Rows[0].Output);
        Assert.Equal("high", Assert.IsType<NoMatchPosture.Default>(table.NoMatch).Value);
    }

    [Fact]
    public void Authors_nested_ref_binary_if_and_call_nodes_that_pass_producer_admission()
    {
        RulesDraft? changed = null;
        var cut = RenderEditor(value => changed = value);
        cut.FindButton("Add declared input").Click();
        cut.Find("select[aria-label='Rule expression shape']").Change("If");
        cut.Find("select[aria-label='Rule condition left expression shape']").Change("Ref");
        cut.Find("select[aria-label='Rule then branch expression shape']").Change("Binary");
        cut.Find("select[aria-label='Rule then branch left operand expression shape']").Change("Ref");
        cut.Find("select[aria-label='Rule then branch right operand literal type']").Change("Number");
        cut.Find("input[aria-label='Rule then branch right operand literal value']").Change("1");
        cut.Find("select[aria-label='Rule else branch expression shape']").Change("Call");
        cut.Find("select[aria-label='Rule else branch formula operator']").Change("date.today");
        cut.FindButton("Remove Rule else branch argument 1").Click();

        var document = new RuleDefinitionDocument(
            new RuleDefinitionEnvelope("nested", "1.0.0", "tenant-a", "domain-package", new JsonObject { ["kind"] = "test" }, []),
            "Nested", RuleDefinitionTier.JsonLogic, changed!.Draft);
        Assert.True(RuleIntentValidator.Validate(document, RuleIntentPhase.Author).IsValid);
    }

    [Fact]
    public void Retains_or_discards_dirty_revisions_and_fences_out_of_order_responses_by_outstanding_request_id()
    {
        var changes = new List<RulesDraft>(); var requests = new List<RulesOperationRequest>();
        var initial = RulesDraft.Empty with { Identity = "amount-rule", ExpectedRevision = "1" };
        var cut = RenderEditor(changes.Add, requests.Add, initial);
        cut.Find("input[aria-label='Rule name']").Change("local");
        cut.Render(parameters => parameters.Add(component => component.Value, initial with { ExpectedRevision = "2", Name = "server" }));
        Assert.Contains("Revision changed.", cut.Markup);
        cut.FindButton("Keep edits").Click();
        Assert.Equal("local", cut.Find("input[aria-label='Rule name']").GetAttribute("value"));
        cut.Render(parameters => parameters.Add(component => component.Value, RulesDraft.Empty with { Identity = "other-rule", ExpectedRevision = "1", Name = "other" }));
        cut.FindButton("Discard edits").Click();
        Assert.Equal("other-rule", changes[^1].Identity);

        cut.FindButton("Preview").Click(); cut.FindButton("Preview").Click();
        Assert.Equal(requests[^2].RequestId, requests[^1].RequestId);
        var request = requests[^1];
        cut.Render(parameters => parameters.Add(component => component.Response, new RulesOperationResponse("wrong", request.Identity, request.ExpectedRevision, request.Generation, Outcome: new RulesOutcome("Value", "sample", "2026-06-30T00:00:00.0000000Z", Value: "stale"))));
        Assert.DoesNotContain("Value: stale", cut.Markup);
        var materialization = ReadMaterialization();
        cut.Render(parameters => parameters.Add(component => component.Response, new RulesOperationResponse(request.RequestId, request.Identity, request.ExpectedRevision, request.Generation, new("amount-rule", "2", "Published"), new RulesOutcome("Value", "sample", "2026-06-30T00:00:00.0000000Z", Value: "accepted"), materialization)));
        Assert.Contains("Value: accepted", cut.Markup);
        Assert.Contains("amount-rule@fixture-v1", cut.Markup);
        Assert.Equal(("2", "Pinned", "fixture-v1"), (changes[^1].ExpectedRevision, changes[^1].VersionSelection, changes[^1].PinnedVersionId));
        Assert.Equal("fixture-v1", changes[^1].Materialization?.Bindings[0].VersionId);
    }

    [Fact]
    public void Clears_an_old_preview_when_a_clean_external_identity_replaces_the_draft()
    {
        var initial = RulesDraft.Empty with { Identity = "amount-rule", ExpectedRevision = "1" };
        var cut = Render<HarborlineRulesAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, initial)
            .Add(component => component.Outcome, new RulesOutcome("Value", "sample", "2026-06-30T00:00:00.0000000Z", Value: "old")));
        Assert.Contains("Value: old", cut.Markup);

        cut.Render(parameters => parameters.Add(component => component.Value, RulesDraft.Empty with { Identity = "other-rule", ExpectedRevision = "1" }));

        Assert.Contains("No preview has run.", cut.Markup);
        Assert.DoesNotContain("Value: old", cut.Markup);
    }

    [Fact]
    public void Acknowledges_a_correlated_outcome_before_allocating_the_next_intent()
    {
        var requests = new List<RulesOperationRequest>();
        var initial = RulesDraft.Empty with { Identity = "amount-rule", ExpectedRevision = "1" };
        var cut = RenderEditor(_ => { }, requests.Add, initial);
        cut.FindButton("Preview").Click();
        var first = requests[0];
        cut.Render(parameters => parameters.Add(component => component.Outcome,
            new RulesOutcome("Value", "sample", "2026-06-30T00:00:00.0000000Z", Value: "ready", RequestId: first.RequestId, Identity: first.Identity, ExpectedRevision: first.ExpectedRevision, Generation: first.Generation)));
        cut.FindButton("Preview").Click();
        Assert.NotEqual(first.RequestId, requests[1].RequestId);
        Assert.Contains("Preview (sample input)", cut.Markup);
    }

    // T-712: DES-0018 rules-auth-1, -2, -5 and -6 at the Blazor editor, matching the React lane's tests.
    [Fact]
    public void Rules_auth_1_holds_exactly_the_one_action_the_author_picks_for_every_action()
    {
        var requests = new List<RulesOperationRequest>();
        var cut = RenderEditor(_ => { }, requests.Add);
        var offered = cut.FindAll("select[aria-label='Rule action'] option").Select(option => option.TextContent).ToArray();
        Assert.Equal(new[] { "Visibility", "Required", "ReadOnly", "Validate", "Compute", "Presentation", "Options" }.Order(), offered.Order());
        foreach (var action in offered)
        {
            cut.Find("select[aria-label='Rule action']").Change(action);
            cut.FindButton("Publish").Click();
            Assert.Equal(action, requests[^1].Draft.Draft.OutputType.ToString());
            Assert.Equal(action, cut.Find("select[aria-label='Rule action']").GetAttribute("value"));
        }
    }

    [Fact]
    public void Rules_auth_2_shares_one_guided_editor_across_accept_when_default_value_and_rule()
    {
        foreach (var (site, label) in new[] { ("accept-when", "Accept when"), ("default", "Default value"), ("rule", "Rule") })
        {
            FormulaExpr? changed = null;
            var cut = Render<HarborlineGuidedExpressionEditor>(parameters => parameters
                .Add(component => component.Site, site)
                .Add(component => component.Value, new FormulaExpr.Call("cat", [new FormulaExpr.Literal("A", ColumnValueType.Text)]))
                .Add(component => component.Contract, Contracts[0])
                .Add(component => component.ValueChanged, EventCallback.Factory.Create<FormulaExpr>(this, value => changed = value)));
            cut.Find($"input[aria-label='{label} argument 1 literal value']").Change("B");
            Assert.Equal("B", Assert.IsType<FormulaExpr.Literal>(Assert.IsType<FormulaExpr.Call>(changed).Args[0]).Value);
        }
    }

    [Fact]
    public void Rules_auth_3_carries_every_reference_form_through_producer_admission_and_receives_the_lowered_reference()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var forms = fixture.RootElement.GetProperty("referenceForms");
        RulesExpressionContract[] contracts = [new("rule", "typed value", "preview", [.. forms.GetProperty("palette").EnumerateArray().Select(item =>
            new RulesPaletteItem(item.GetProperty("id").GetString()!, item.GetProperty("label").GetString()!, Enum.Parse<ColumnValueType>(item.GetProperty("valueType").GetString()!)))])];
        foreach (var item in forms.GetProperty("cases").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var reference = item.GetProperty("ref").GetString()!;
            var requests = new List<RulesOperationRequest>();
            var cut = Render<HarborlineRulesAuthoringEditor>(parameters => parameters
                .Add(component => component.Value, RulesDraft.Empty)
                .Add(component => component.ExpressionContracts, contracts)
                .Add(component => component.OperationRequested, EventCallback.Factory.Create<RulesOperationRequest>(this, requests.Add)));
            cut.Find("select[aria-label='Rule action']").Change(item.GetProperty("action").GetString()!);
            cut.Find("select[aria-label='Rule scope']").Change(item.GetProperty("scope").GetString()!);
            cut.Find("input[aria-label='Target']").Change(item.GetProperty("scopeTarget").GetString()!);
            // Producer admission refuses an undeclared reference, so the author declares it first.
            cut.FindButton("Add declared input").Click();
            cut.Find("select[aria-label='Input 1 reference']").Change(reference);
            cut.Find("select[aria-label='Rule expression shape']").Change("Ref");
            cut.Find("select[aria-label='Rule reference']").Change(reference);
            cut.FindButton("Publish").Click();

            var draft = requests[^1].Draft.Draft;
            Assert.Equal(reference, Assert.IsType<FormulaExpr.Ref>(Assert.IsType<FormulaDraft>(draft).Expression).Name);
            var admitted = RuleIntentValidator.Validate(new RuleDefinitionDocument(
                new RuleDefinitionEnvelope(id, "1.0.0", "tenant-a", "domain-package", new JsonObject { ["kind"] = "test" }, []),
                id, RuleDefinitionTier.JsonLogic, draft), RuleIntentPhase.Author);
            Assert.True(admitted.IsValid, $"{id}: {string.Join(", ", admitted.Diagnostics.Select(diagnostic => diagnostic.Code))}");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(item.GetProperty("lowered").GetRawText()), admitted.Lowered), $"{id}: lowered {admitted.Lowered?.ToJsonString()}");
        }
    }

    [Fact]
    public void Rules_auth_5_offers_exactly_first_match_and_priority_and_round_trips_the_choice()
    {
        var requests = new List<RulesOperationRequest>();
        var cut = RenderEditor(_ => { }, requests.Add);
        cut.Find("input[aria-label='Decision table']").Change(true);
        // Fails if a third policy becomes selectable.
        Assert.Equal(["FirstMatch", "Priority"], cut.FindAll("select[aria-label='Hit policy'] option").Select(option => option.TextContent).ToArray());
        foreach (var policy in new[] { HitPolicy.Priority, HitPolicy.FirstMatch })
        {
            cut.Find("select[aria-label='Hit policy']").Change(policy.ToString());
            cut.FindButton("Publish").Click();
            Assert.Equal(policy, Assert.IsType<DecisionTableDraft>(requests[^1].Draft.Draft).HitPolicy);
        }
    }

    [Fact]
    public void Rules_auth_6_authors_an_explicit_default_or_a_real_any_catch_all_row()
    {
        RulesDraft? changed = null;
        var cut = RenderEditor(value => changed = value);
        cut.Find("input[aria-label='Decision table']").Change(true);
        cut.FindButton("Add column").Click();
        cut.FindButton("Add row").Click();
        cut.Find("input[aria-label='Default output']").Change("fallback");
        Assert.Equal("fallback", Assert.IsType<NoMatchPosture.Default>(Assert.IsType<DecisionTableDraft>(changed!.Draft).NoMatch).Value);
        cut.FindButton("Add catch-all row").Click();
        var table = Assert.IsType<DecisionTableDraft>(changed!.Draft);
        Assert.IsType<NoMatchPosture.CatchAll>(table.NoMatch);
        Assert.IsType<TableCell.Any>(table.Rows[^1].Cells["column-1"]);
        Assert.NotNull(cut.Find("tr[data-catch-all]"));
    }

    private static readonly RulesExpressionContract[] Contracts = [new("rule", "typed value", "preview", [new("amount", "Amount", ColumnValueType.Number)])];
    private IRenderedComponent<HarborlineRulesAuthoringEditor> RenderEditor(Action<RulesDraft> changed, Action<RulesOperationRequest>? requested = null, RulesDraft? value = null) => Render<HarborlineRulesAuthoringEditor>(parameters => parameters
        .Add(component => component.Value, value ?? RulesDraft.Empty)
        .Add(component => component.ExpressionContracts, Contracts)
        .Add(component => component.ValueChanged, EventCallback.Factory.Create(this, changed))
        .Add(component => component.OperationRequested, EventCallback.Factory.Create(this, requested ?? (_ => { }))));
    private static string? Read(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static RulesMaterialization ReadMaterialization()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var value = document.RootElement.GetProperty("lifecycle").GetProperty("responses")[4].GetProperty("materialization");
        return JsonSerializer.Deserialize<RulesMaterialization>(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static string FindFixture()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "conformance", "hlp.blocks.builder-definitions", "rules-editor-contract-fixtures.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("rules-editor-contract-fixtures.json was not found from the test working directory.");
    }
}

internal static class RulesAuthoringTestExtensions
{
    internal static AngleSharp.Dom.IElement FindButton(this IRenderedComponent<HarborlineRulesAuthoringEditor> cut, string text) => cut.FindAll("button").Single(button => button.TextContent == text);
}
