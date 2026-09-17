using System.Text;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class CanonicalPlatformPackageSeedTests
{
    [Fact]
    public void Fixture_covers_ck_1_through_ck_14_in_declared_order_and_is_valid()
    {
        Assert.Equal(
            Enumerable.Range(1, 14).Select(index => $"platform-package-ck-{index}"),
            PlatformPackageSeed.Manifest.Items.Select(item => item.Id));
        Assert.True(PlatformPackageReplayer.Validate(PlatformPackageSeed.Manifest).Succeeded);
    }

    [Fact]
    public void Fixture_has_the_ruled_member_inventory()
    {
        Assert.Equal(34, Members("platform-package-ck-2").GetArrayLength());
        Assert.Equal(4, Members("platform-package-ck-3").GetArrayLength());
        Assert.Equal(2, Members("platform-package-ck-4").GetArrayLength());
        Assert.Equal(13, Members("platform-package-ck-6").GetArrayLength());
        Assert.Equal(39, Members("platform-package-ck-7").GetArrayLength());
        Assert.Equal(3, Members("platform-package-ck-8").GetArrayLength());
        Assert.Equal(2, Payload("platform-package-ck-9").GetProperty("roles").GetArrayLength());
    }

    [Fact]
    public void Views_authoring_is_mounted_in_Workshop_as_platform_package_metadata()
    {
        var views = Payload("platform-package-ck-7");
        var authoring = views.GetProperty("authoring");

        Assert.Equal("platform.workspace.workshop", authoring.GetProperty("workspace").GetString());
        Assert.Equal("platform.navigation.views.author", authoring.GetProperty("navigationEntry").GetProperty("id").GetString());
        Assert.Equal("views", authoring.GetProperty("navigationEntry").GetProperty("pillar").GetString());
        Assert.Equal("platform.editor.views", authoring.GetProperty("editor").GetProperty("id").GetString());
        Assert.Equal("ViewDefinition", authoring.GetProperty("editor").GetProperty("definitionKind").GetString());
        Assert.Equal(["react", "blazor"], authoring.GetProperty("editor").GetProperty("projections").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(39, views.GetProperty("members").GetArrayLength());
    }

    [Fact]
    public void Data_exchange_authoring_is_mounted_in_Workshop_with_the_fixed_mapping_profile()
    {
        var authoring = Payload("platform-package-ck-7").GetProperty("dataExchangeAuthoring");

        Assert.Equal("platform.workspace.workshop", authoring.GetProperty("workspace").GetString());
        Assert.Equal("platform.navigation.data-exchanges.author", authoring.GetProperty("navigationEntry").GetProperty("id").GetString());
        Assert.Equal("data-exchanges", authoring.GetProperty("navigationEntry").GetProperty("pillar").GetString());
        Assert.Equal("platform.editor.data-exchange", authoring.GetProperty("editor").GetProperty("id").GetString());
        Assert.Equal("DataExchangeDefinition", authoring.GetProperty("editor").GetProperty("definitionKind").GetString());
        Assert.Equal("hl:tabular-mapping/v1", authoring.GetProperty("mappingProfile").GetProperty("id").GetString());
        Assert.Equal("https://schemas.harborline.software/mapping/tabular/v1", authoring.GetProperty("mappingProfile").GetProperty("schemaUri").GetString());
        Assert.Equal("1.0.0", authoring.GetProperty("mappingProfile").GetProperty("documentVersion").GetString());
        Assert.Equal(["react", "blazor"], authoring.GetProperty("editor").GetProperty("projections").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void Every_seed_member_has_a_stable_unique_id()
    {
        var memberGroups = new[] { 2, 3, 4, 6, 7, 8 };
        var ids = memberGroups
            .SelectMany(index => Members($"platform-package-ck-{index}").EnumerateArray())
            .Select(member => member.GetProperty("id").GetString()!)
            .Concat(Payload("platform-package-ck-9").GetProperty("roles").EnumerateArray().Select(member => member.GetProperty("id").GetString()!))
            .Concat(Payload("platform-package-ck-10").GetProperty("bindings").EnumerateArray().Select(member => member.GetProperty("id").GetString()!))
            .Concat([Payload("platform-package-ck-5").GetProperty("id").GetString()!, Payload("platform-package-ck-11").GetProperty("id").GetString()!])
            .ToArray();

        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Navigation_and_capability_bindings_preserve_the_API_migration_authority()
    {
        Assert.Equal(
            ["asset-types", "forms", "workflows", "standards", "defaults", "terminology", "documents", "taxonomies", "reports", "data-exchanges", "standing-rules", "schedules", "views"],
            Members("platform-package-ck-6").EnumerateArray().Select(member => member.GetProperty("id").GetString()));

        var access = Payload("platform-package-ck-10");
        Assert.Equal(["catalogue:read", "records:read", "audit:read"], access.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            ["platform.binding.catalogue-read", "platform.binding.records-read", "platform.binding.audit-read"],
            access.GetProperty("bindings").EnumerateArray().Select(binding => binding.GetProperty("id").GetString()));
        Assert.Equal(
            ["platform/administrator", "platform/administrator", "platform/auditor"],
            access.GetProperty("bindings").EnumerateArray().Select(binding => binding.GetProperty("offeredRoles")[0].GetString()));
    }

    [Fact]
    public void Fixture_carries_the_ADR_0097_policy_values()
    {
        var audit = Members("platform-package-ck-3").EnumerateArray().Single(member => member.GetProperty("id").GetString() == "platform.operational-type.audit-entry");
        var auditRetention = audit.GetProperty("policies").GetProperty("retention");
        Assert.Equal("tenant", auditRetention.GetProperty("owner").GetString());
        Assert.Equal(JsonValueKind.Null, auditRetention.GetProperty("defaultValue").ValueKind);
        Assert.Equal("refuse-until-declared", auditRetention.GetProperty("publication").GetString());

        var defaults = Payload("platform-package-ck-14");
        Assert.Equal("reason-required", defaults.GetProperty("amendment").GetString());
        Assert.Equal("enabled", defaults.GetProperty("history").GetString());
        Assert.Equal("unset-warning", defaults.GetProperty("retention").GetString());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("planHorizonDefault").ValueKind);
    }

    [Fact]
    public void Fixture_declares_no_platform_owned_protocols()
    {
        var protocols = Payload("platform-package-ck-12");
        Assert.Equal("none", protocols.GetProperty("disposition").GetString());
        Assert.Empty(protocols.GetProperty("protocols").EnumerateArray());
    }

    [Fact]
    public void Fixture_preserves_all_explicit_absences_and_open_boundaries()
    {
        var text = Encoding.UTF8.GetString(PlatformPackageSeed.Export());
        var roles = Payload("platform-package-ck-9");

        Assert.Empty(roles.GetProperty("grants").EnumerateArray());
        Assert.DoesNotContain("platform.trait.inspectable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("for-you", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"shape\":\"board\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"shape\":\"map\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"shape\":\"tree\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"shape\":\"gallery\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("platform.domain", text, StringComparison.Ordinal);
        Assert.DoesNotContain("firstAuthoritySource", text, StringComparison.Ordinal);
        Assert.DoesNotContain("accessPackProvenance", text, StringComparison.Ordinal);
        Assert.DoesNotContain("platform-package-ck-15", text, StringComparison.Ordinal);
        Assert.DoesNotContain("platform-package-ck-16", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiTransport", text, StringComparison.Ordinal);
        Assert.DoesNotContain("signing", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installation", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fixture_replays_atomically_in_ck_order()
    {
        var target = new AtomicRecordingTarget();

        var result = await PlatformPackageReplayer.ReplayAsync(PlatformPackageSeed.Manifest, target);

        Assert.True(result.Succeeded);
        Assert.Equal(Enumerable.Range(1, 14).Select(index => $"platform-package-ck-{index}"), target.ItemIds);
    }

    [Fact]
    public void Checked_in_export_is_byte_identical_and_digest_bound()
    {
        Assert.True(PlatformPackageSeed.VerifyCheckedInExport());
        Assert.Equal(PlatformPackageSeed.Export(), PlatformPackageSeed.LoadCheckedInExport());
        using var document = JsonDocument.Parse(PlatformPackageSeed.LoadCheckedInExport());
        Assert.Empty(document.RootElement.GetProperty("closure").GetProperty("dependencies").EnumerateArray());
        Assert.Equal("sha256", document.RootElement.GetProperty("digest").GetProperty("algorithm").GetString());
    }

    private static JsonElement Payload(string id)
    {
        var item = PlatformPackageSeed.Manifest.Items.Single(item => item.Id == id);
        using var document = JsonDocument.Parse(item.Content.Payload);
        return document.RootElement.Clone();
    }

    private static JsonElement Members(string id) => Payload(id).GetProperty("members");

    private sealed class AtomicRecordingTarget : IPlatformPackageReplayTarget
    {
        public IReadOnlyList<string> ItemIds { get; private set; } = [];

        public ValueTask<bool> TryApplyToEmptyAsync(IReadOnlyList<PlatformPackageItem> items, CancellationToken cancellationToken = default)
        {
            ItemIds = items.Select(item => item.Id).ToArray();
            return ValueTask.FromResult(true);
        }
    }
}
