using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class InstalledPlatformCatalogueTests
{
    [Theory]
    [InlineData("sys.platform-roles/administrator", true, true, false)]
    [InlineData("sys.platform-roles/auditor", false, false, true)]
    [InlineData("platform/administrator", false, false, false)]
    [InlineData("sys.platform-roles/unknown", false, false, false)]
    public async Task Installed_roles_and_bindings_resolve_from_their_definition_items(
        string role, bool catalogue, bool records, bool audit)
    {
        var node = await InstallAsync(role);

        Assert.Equal(catalogue, await node.AuthorizeAsync("catalogue:read"));
        Assert.Equal(records, await node.AuthorizeAsync("records:read"));
        Assert.Equal(audit, await node.AuthorizeAsync("audit:read"));
        Assert.False(await node.AuthorizeAsync("unknown:read"));
        var installed = await node.ReadInstalledStateAsync();
        Assert.Equal([audit, catalogue, records], installed.Capabilities.OrderBy(capability => capability.Name, StringComparer.Ordinal)
            .Select(capability => capability.ExpectedAllowed));
    }

    [Fact]
    public async Task The_binding_body_not_its_id_decides_the_operation_and_offered_roles()
    {
        var node = await InstallAsync("sys.platform-roles/auditor", "platform.binding.catalogue-read",
            JsonNode.Parse("""{"operation":"records:read","scope":"/","offeredRoles":["sys.platform-roles/auditor"]}""")!);

        Assert.False(await node.AuthorizeAsync("catalogue:read"));
        Assert.True(await node.AuthorizeAsync("records:read"));
    }

    [Fact]
    public async Task A_grant_cannot_resolve_a_role_absent_from_the_installed_role_definitions()
    {
        var node = await InstallAsync("sys.platform-roles/auditor", "platform.role.auditor",
            JsonNode.Parse("""{"role":"other","displayName":"Auditor"}""")!);

        Assert.False(await node.AuthorizeAsync("audit:read"));
    }

    private static async Task<InstalledPlatformCatalogue> InstallAsync(string role, string? changedId = null, JsonNode? body = null)
    {
        var seed = PlatformPackageSeed.Manifest;
        var items = seed.Items.Select(item =>
        {
            if (item.Id != "platform-package-ck-9" && item.Id != changedId) return item;
            var payload = JsonNode.Parse(item.Content.Payload.Span)!;
            if (item.Id == "platform-package-ck-9") payload["grants"] = new JsonArray(new JsonObject { ["role"] = role });
            else payload["body"] = body;
            return new PlatformPackageItem(item.Id, item.Stage, item.Dependencies,
                PlatformPackageContent.PresentJson(JsonSerializer.SerializeToUtf8Bytes(payload)));
        });
        var node = new InstalledPlatformCatalogue();
        var replay = await PlatformPackageReplayer.ReplayAsync(new(seed.SchemaVersion, seed.PackageKey, seed.Revision, items), node);
        Assert.True(replay.Succeeded, replay.RefusalCode);
        return node;
    }
}
