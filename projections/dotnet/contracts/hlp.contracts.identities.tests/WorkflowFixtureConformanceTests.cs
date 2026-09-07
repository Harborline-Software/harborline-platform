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
            ApplyMutation(node, row);
            var definition = (WorkflowDefinition)WorkflowWire.Deserialize("WorkflowDefinition", node.ToJsonString());
            var result = WorkflowAdmissionValidator.Validate(definition, resolver, roleVocabulary);
            var expectedResult = row.GetProperty("expected");
            Assert.Equal(expectedResult.GetProperty("isValid").GetBoolean(), result.IsValid);
            foreach (var code in expectedResult.GetProperty("codes").EnumerateArray().Select(value => value.GetString()!))
                Assert.Contains(result.Violations, violation => violation.Code == code);
        }

        Assert.Equal(19, cases.Length);
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

    private static void ApplyMutation(JsonObject definition, JsonElement row)
    {
        if (!row.TryGetProperty("mutation", out var mutation)) return;
        var action = definition["actions"]!.AsArray()[0]!.AsObject();
        var transition = definition["transitions"]!.AsArray()[0]!.AsObject();
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
            transition["requiredRoles"] = new JsonArray(new JsonObject { ["vocabulary"] = "tax.roles", ["name"] = "Missing" });
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
