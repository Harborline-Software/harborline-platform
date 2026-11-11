using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class PlatformPackageSeedDefinitionEntriesTests
{
    [Theory]
    [InlineData("platform-package-ck-7", "members", "ViewDefinition", 10, 39, PlatformSeedStage.Views)]
    [InlineData("platform-package-ck-9", "roles", "RoleDefinition", 14, 2, PlatformSeedStage.AccessDefinitions)]
    [InlineData("platform-package-ck-10", "bindings", "AuthorizationCapabilityBinding", 15, 3, PlatformSeedStage.AccessDefinitions)]
    public void Each_row_lists_only_ids_in_bijection_with_its_typed_definition_items(
        string rowId, string memberProperty, string kind, int wireValue, int count, PlatformSeedStage stage)
    {
        using var export = JsonDocument.Parse(PlatformPackageSeed.Export());
        var items = export.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var row = Assert.Single(items, item => item.GetProperty("id").GetString() == rowId);
        var ids = row.GetProperty("content").GetProperty("payload").GetProperty(memberProperty)
            .EnumerateArray().Select(id => id.GetString()).ToArray();
        var definitions = items.Where(item => item.GetProperty("content").GetProperty("payload")
            .TryGetProperty("contentKind", out var stated) && stated.GetString() == kind).ToArray();

        Assert.Equal(count, ids.Length);
        Assert.Equal(count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(count, definitions.Length);
        Assert.Equal(ids.Order(StringComparer.Ordinal), definitions.Select(item => item.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal((wireValue, PackContentKindStatus.Shipped), (PackContentKindRegistry.Kind(kind).Value, PackContentKindRegistry.Kind(kind).Status));
        foreach (var item in definitions)
        {
            var payload = item.GetProperty("content").GetProperty("payload");
            Assert.Equal(["definitionKey", "packageKey", "contentKind", "body"], payload.EnumerateObject().Select(property => property.Name));
            Assert.Equal(item.GetProperty("id").GetString(), payload.GetProperty("definitionKey").GetString());
            Assert.Equal("harborline.platform", payload.GetProperty("packageKey").GetString());
            Assert.Equal(JsonValueKind.Object, payload.GetProperty("body").ValueKind);
            Assert.Equal(stage, PlatformPackageSeed.Manifest.Items.Single(candidate => candidate.Id == item.GetProperty("id").GetString()).Stage);
            Assert.Equal([rowId], item.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()));
        }
    }

    [Fact]
    public void Each_definition_body_has_exactly_one_carrier_anywhere_in_the_export()
    {
        var export = JsonNode.Parse(PlatformPackageSeed.Export())!;
        var payloads = export["items"]!.AsArray().Select(item => item!["content"]!["payload"]!).ToArray();
        var bodies = payloads.OfType<JsonObject>().Where(payload => payload.ContainsKey("body")).Select(payload => payload["body"]!).ToArray();

        Assert.Equal(48, bodies.Length);
        foreach (var body in bodies)
            Assert.Single(payloads.SelectMany(Objects), node => JsonNode.DeepEquals(node, body));
    }

    [Fact]
    public void Role_and_binding_bodies_match_api_1_6_1_and_use_the_sealed_sys_platform_roles_references()
    {
        var expected = JsonNode.Parse(File.ReadAllText(RecordWriteJournalBindingTests.Fixture("api-platform-1.6.1/access-items.json")))!.AsObject();
        foreach (var entry in expected.Where(entry => entry.Key != "_source"))
        {
            var item = PlatformPackageSeed.Manifest.Items.Single(item => item.Id == entry.Key);
            var payload = JsonNode.Parse(item.Content.Payload.Span)!;
            Assert.Equal(entry.Value!["kind"]!.GetValue<string>(), payload["contentKind"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(entry.Value["content"], payload["body"]), entry.Key);
        }
    }

    [Fact]
    public void All_bindings_offer_only_sealed_platform_roles_and_audit_read_offers_the_auditor()
    {
        var bindings = PlatformPackageSeed.Manifest.Items.Select(item => JsonNode.Parse(item.Content.Payload.Span)!)
            .Where(payload => payload["contentKind"]?.GetValue<string>() == "AuthorizationCapabilityBinding").ToArray();

        Assert.Equal(3, bindings.Length);
        foreach (var binding in bindings)
        {
            Assert.Equal(["operation", "scope", "offeredRoles"], binding["body"]!.AsObject().Select(property => property.Key));
            Assert.Equal("/", binding["body"]!["scope"]!.GetValue<string>());
            Assert.All(binding["body"]!["offeredRoles"]!.AsArray(), role => Assert.Contains(role!.GetValue<string>(),
                new[] { "sys.platform-roles/administrator", "sys.platform-roles/auditor" }));
        }
        var audit = Assert.Single(bindings, binding => binding["definitionKey"]!.GetValue<string>() == "platform.binding.audit-read");
        Assert.Equal("audit:read", audit["body"]!["operation"]!.GetValue<string>());
        Assert.Equal(["sys.platform-roles/auditor"], audit["body"]!["offeredRoles"]!.AsArray().Select(role => role!.GetValue<string>()));
    }

    private static IEnumerable<JsonNode> Objects(JsonNode node)
    {
        if (node is JsonObject value)
        {
            yield return value;
            foreach (var child in value.Select(property => property.Value).OfType<JsonNode>().SelectMany(Objects)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>().SelectMany(Objects)) yield return child;
    }
}
