using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Workflow;
using Xunit;

namespace Harborline.Contracts.Tests;

public sealed class WorkflowFixtureConformanceTests
{
    [Fact]
    public void Frozen_workflow_fixtures_pass_the_dotnet_projection_and_admission_fence()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var root = fixture.RootElement;
        var canonical = root.GetProperty("canonical");
        var authority = root.GetProperty("authority").EnumerateObject().ToDictionary(
            row => row.Name,
            row => Enum.Parse<ActionClassification>(row.Value.GetString()!),
            StringComparer.Ordinal);
        var resolver = new ImmutableWorkflowAuthorityResolver(authority);
        var roleDefinitions = JsonSerializer.Deserialize<RoleDefinition[]>(root.GetProperty("roleDefinitions").GetRawText())!;
        var roleVocabulary = RoleVocabulary.FromApi(roleDefinitions);
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();

        foreach (var row in cases)
        {
            var operation = row.GetProperty("operation").GetString();
            if (operation == "surface.exports")
            {
                Assert.Equal(row.GetProperty("expected").GetProperty("declarations").GetInt32(), WorkflowContractSurface.Exports.Count);
                continue;
            }
            if (operation == "closed.values")
            {
                foreach (var expected in row.GetProperty("expected").EnumerateObject())
                    Assert.Equal(expected.Value.EnumerateArray().Select(value => value.GetString()), WorkflowContractSurface.ClosedValues[expected.Name]);
                continue;
            }
            var input = row.GetProperty("input").ValueKind == JsonValueKind.String ? canonical.GetRawText() : row.GetProperty("input").GetRawText();
            if (operation!.StartsWith("wire.", StringComparison.Ordinal))
            {
                AssertWire(row, input);
                continue;
            }

            var node = JsonNode.Parse(input)!.AsObject();
            ApplyMutation(node, row, root);
            var definition = (WorkflowDefinition)WorkflowWire.Deserialize("WorkflowDefinition", node.ToJsonString());
            if (row.TryGetProperty("mutation", out var mutation) && mutation.TryGetProperty("stateDeclarations", out var declarations))
            {
                var state = definition.States.First(s => s.Kind == WorkflowStateKind.Normal);
                var actual = JsonNode.Parse(WorkflowWire.Serialize("WorkflowState", state))!;
                foreach (var member in declarations.EnumerateObject())
                    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(member.Value.GetRawText()), actual[member.Name]), $"{row.GetProperty("id").GetString()}: {member.Name}");
            }
            var window = row.TryGetProperty("window", out _) ? JsonSerializer.Deserialize<WorkflowContractWindow>(root.GetProperty("contractWindow").GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
            var result = WorkflowAdmissionValidator.Validate(definition, resolver, roleVocabulary, window);
            var expectedResult = row.GetProperty("expected");
            Assert.Equal(expectedResult.GetProperty("isValid").GetBoolean(), result.IsValid);
            // The exact distinct code set, as the TypeScript projection asserts: an extra violation fails too.
            var expectedCodes = expectedResult.GetProperty("codes").EnumerateArray().Select(value => value.GetString()!)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var actualCodes = result.Violations.Select(violation => violation.Code)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Assert.True(expectedCodes.SequenceEqual(actualCodes, StringComparer.Ordinal),
                $"{row.GetProperty("id").GetString()}: expected [{string.Join(", ", expectedCodes)}], got [{string.Join(", ", actualCodes)}]");
        }

        Assert.Equal(92, cases.Length);
        Assert.Equal(ActionClassification.CP, resolver.AuthorityOf("unregistered.capability"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImmutableWorkflowAuthorityResolver(
            new Dictionary<string, ActionClassification> { ["invalid"] = (ActionClassification)99 }));

        var duplicateNode = JsonNode.Parse(canonical.GetRawText())!.AsObject();
        duplicateNode["actions"]!.AsArray().Add(duplicateNode["actions"]!.AsArray()[0]!.DeepClone());
        var duplicate = (WorkflowDefinition)WorkflowWire.Deserialize("WorkflowDefinition", duplicateNode.ToJsonString());
        Assert.Contains(WorkflowAdmissionValidator.Validate(duplicate, resolver).Violations,
            violation => violation.Code == WorkflowAdmissionCodes.DuplicateId);
    }

    [Fact(DisplayName = "workflows-ck-4 workflows-auth-22: Normal A reaches Terminal B")]
    public void Normal_to_terminal_graph_is_valid()
    {
        var result = ValidateGraph(GraphDefinition());
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact(DisplayName = "workflows-ck-4 workflows-auth-22: reached Normal B is a dead end")]
    public void Reached_normal_dead_end_names_B()
    {
        var graph = GraphDefinition();
        graph["states"]![1]!["kind"] = "Normal";
        var result = ValidateGraph(graph);
        Assert.False(result.IsValid);
        var violation = Assert.Single(result.Violations);
        Assert.Equal(("workflow.admission.dead_end_state", "B"), (violation.Code, violation.Locator));
    }

    [Fact(DisplayName = "workflows-ck-4 workflows-auth-22: Terminal A cannot have an outgoing edge")]
    public void Terminal_outgoing_edge_names_A()
    {
        var graph = GraphDefinition();
        graph["states"]![0]!["kind"] = "Terminal";
        var result = ValidateGraph(graph);
        Assert.False(result.IsValid);
        var violation = Assert.Single(result.Violations);
        Assert.Equal(("workflow.admission.terminal_has_outgoing", "A"), (violation.Code, violation.Locator));
    }

    [Fact(DisplayName = "workflows-ck-4 workflows-auth-22: both graph refusals are reported exactly")]
    public void Combined_graph_defects_name_both_states()
    {
        var graph = GraphDefinition();
        graph["states"]![0]!["kind"] = "Terminal";
        graph["states"]![1]!["kind"] = "Normal";
        var result = ValidateGraph(graph);
        Assert.False(result.IsValid);
        // Compare the set in ordinal code/locator order; admission ordering is not part of the contract.
        var actual = result.Violations.OrderBy(v => v.Code, StringComparer.Ordinal)
            .ThenBy(v => v.Locator, StringComparer.Ordinal).Select(v => (v.Code, v.Locator)).ToArray();
        Assert.Equal(new[] { ("workflow.admission.dead_end_state", "B"), ("workflow.admission.terminal_has_outgoing", "A") }, actual);
    }

    [Fact(DisplayName = "workflows-ck-4 workflows-auth-22: lone Terminal A admits an effect-free graph")]
    public void Lone_terminal_without_transitions_is_valid()
    {
        var graph = GraphDefinition();
        graph["states"]![0]!["kind"] = "Terminal";
        graph["states"]!.AsArray().RemoveAt(1);
        graph["transitions"] = new JsonArray();
        var result = ValidateGraph(graph);
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    private static WorkflowAdmissionResult ValidateGraph(JsonObject graph) => WorkflowAdmissionValidator.Validate(
        (WorkflowDefinition)WorkflowWire.Deserialize("WorkflowDefinition", graph.ToJsonString()),
        new ImmutableWorkflowAuthorityResolver(new Dictionary<string, ActionClassification>()));

    // Owned literal from control 221d9269, T-488/admission-state-graph-proof; independent of the corpus.
    private static JsonObject GraphDefinition() => JsonNode.Parse("""
        {
          "key":"graph-proof","version":"1.0.0","status":"Draft","tenant":"tenant:graph",
          "owner":{"scheme":"user","value":"author"},
          "title":{"defaultLocale":"en","values":{"en":"Graph proof"}},
          "subject":{"record":{"recordType":"invoice"}},"mutability":"Locked","initialState":"A",
          "states":[
            {"id":"A","label":{"defaultLocale":"en","values":{"en":"A"}},"kind":"Normal"},
            {"id":"B","label":{"defaultLocale":"en","values":{"en":"B"}},"kind":"Terminal"}
          ],
          "triggers":[{"id":"issued","kind":"Event","eventType":"Issued"}],
          "transitions":[{"id":"t-ab","from":"A","on":"issued","to":"B"}],
          "actions":[],"guards":[],
          "createdAt":"2026-11-21T00:00:00.000Z","updatedAt":"2026-11-21T00:00:00.000Z"
        }
        """)!.AsObject();

    private static void AssertWire(JsonElement row, string input)
    {
        var type = row.GetProperty("type").GetString()!;
        if (row.TryGetProperty("expectedError", out var expectedError))
        {
            var error = Assert.Throws<WorkflowWireException>(() => WorkflowWire.Deserialize(type, input));
            Assert.Equal(expectedError.GetString(), error.Code);
            return;
        }
        var value = WorkflowWire.Deserialize(type, input);
        var actual = JsonNode.Parse(WorkflowWire.Serialize(type, value));
        var expected = row.GetProperty("expected").ValueKind == JsonValueKind.String
            ? JsonNode.Parse(input)
            : JsonNode.Parse(row.GetProperty("expected").GetRawText());
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{row.GetProperty("id").GetString()} expected {expected} but received {actual}");
    }

    private static void ApplyMutation(JsonObject definition, JsonElement row, JsonElement root)
    {
        if (!row.TryGetProperty("mutation", out var mutation)) return;
        var action = definition["actions"]!.AsArray()[0]!.AsObject();
        var transition = definition["transitions"]!.AsArray()[0]!.AsObject();
        // Role and standing mutations target the first HumanAction transition: since T-1176 Q1 a gate on an
        // autonomous transition is its own refusal (gate_on_autonomous_step), tested by the autonomous* keys.
        var humanTriggers = definition["triggers"]!.AsArray().Where(t => (string?)t!["kind"] == "HumanAction").Select(t => (string?)t!["id"]).ToHashSet();
        var gated = definition["transitions"]!.AsArray().First(t => humanTriggers.Contains((string?)t!["on"]))!.AsObject();
        if (mutation.TryGetProperty("stateDeclarations", out var declarations))
        {
            var state = definition["states"]!.AsArray().First(s => (string?)s!["kind"] == "Normal")!.AsObject();
            foreach (var member in declarations.EnumerateObject()) state[member.Name] = JsonNode.Parse(member.Value.GetRawText());
        }
        if (mutation.TryGetProperty("actionTransition", out var actionTransition))
            action["on"] = new JsonObject { ["transition"] = actionTransition.GetString() };
        if (mutation.TryGetProperty("actionClassification", out var classification)) action["classification"] = classification.GetString();
        if (mutation.TryGetProperty("transitionGuard", out var guard)) transition["guard"] = guard.GetString();
        if (mutation.TryGetProperty("terminalOutgoing", out _))
            definition["transitions"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "t-terminal", ["from"] = "Posted", ["on"] = "approve", ["to"] = "Rejected",
            });
        if (mutation.TryGetProperty("unknownActionRole", out _))
            action["requiredRoles"] = new JsonArray(new JsonObject { ["vocabulary"] = "tax.roles", ["name"] = "Missing" });
        if (mutation.TryGetProperty("unknownTransitionRole", out _))
            gated["requiredRoles"] = new JsonArray(new JsonObject { ["vocabulary"] = "tax.roles", ["name"] = "Missing" });
        if (mutation.TryGetProperty("envelope", out _)) definition["envelope"] = JsonNode.Parse(root.GetProperty("envelope").GetRawText());
        var envelope = definition["envelope"]?.AsObject();
        if (mutation.TryGetProperty("envelopeContract", out var contract))
        {
            if (contract.ValueKind == JsonValueKind.Null) envelope!.Remove("contract");
            else envelope!["contract"] = JsonNode.Parse(contract.GetRawText());
        }
        if (mutation.TryGetProperty("envelopeIdentity", out var identity)) envelope!["identity"] = identity.GetString();
        if (mutation.TryGetProperty("envelopeRetentionClass", out var retention)) envelope!["retentionClass"] = retention.GetString();
        if (mutation.TryGetProperty("envelopeRequirement", out var requirement)) envelope!["requires"] = new JsonArray(new JsonObject { ["capability"] = requirement.GetString() });
        if (mutation.TryGetProperty("transitionRole", out var transitionRole)) gated["requiredRoles"] = new JsonArray(JsonNode.Parse(transitionRole.GetRawText()));
        if (mutation.TryGetProperty("actionRole", out var actionRole)) action["requiredRoles"] = new JsonArray(JsonNode.Parse(actionRole.GetRawText()));
        if (mutation.TryGetProperty("transitionStanding", out var transitionStanding)) gated["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = transitionStanding.GetString() });
        // T-1176 mixed reachability: an action firing on entry to a state, and an autonomous edge into that state.
        if (mutation.TryGetProperty("actionState", out var actionState)) action["on"] = new JsonObject { ["state"] = actionState.GetString() };
        if (mutation.TryGetProperty("autonomousEdgeInto", out var edgeInto))
        {
            var autonomousTrigger = definition["triggers"]!.AsArray().First(t => (string?)t!["kind"] != "HumanAction")!["id"]!.GetValue<string>();
            var from = (string?)gated["from"];
            definition["transitions"]!.AsArray().Add(new JsonObject { ["id"] = "t-autonomous-into", ["from"] = from, ["on"] = autonomousTrigger, ["to"] = edgeInto.GetString() });
        }
        if (mutation.TryGetProperty("autonomousTransitionRole", out var autonomousRole)) transition["requiredRoles"] = new JsonArray(JsonNode.Parse(autonomousRole.GetRawText()));
        if (mutation.TryGetProperty("autonomousTransitionStanding", out var autonomousStanding)) transition["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = autonomousStanding.GetString() });
        if (mutation.TryGetProperty("actionStanding", out var actionStanding)) action["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = actionStanding.GetString() });
    }

    private static string FindFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "conformance", "hlp.contracts.workflow", "fixtures.yaml");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Unable to locate conformance/hlp.contracts.workflow/fixtures.yaml.");
    }
}
