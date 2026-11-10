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

        Assert.Equal(88, cases.Length);
        Assert.Equal(ActionClassification.CP, resolver.AuthorityOf("unregistered.capability"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImmutableWorkflowAuthorityResolver(
            new Dictionary<string, ActionClassification> { ["invalid"] = (ActionClassification)99 }));

        var duplicateNode = JsonNode.Parse(canonical.GetRawText())!.AsObject();
        duplicateNode["actions"]!.AsArray().Add(duplicateNode["actions"]!.AsArray()[0]!.DeepClone());
        var duplicate = (WorkflowDefinition)WorkflowWire.Deserialize("WorkflowDefinition", duplicateNode.ToJsonString());
        Assert.Contains(WorkflowAdmissionValidator.Validate(duplicate, resolver).Violations,
            violation => violation.Code == WorkflowAdmissionCodes.DuplicateId);
    }

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
