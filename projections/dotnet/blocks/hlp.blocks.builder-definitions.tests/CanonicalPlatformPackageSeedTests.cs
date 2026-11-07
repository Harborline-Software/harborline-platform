using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class CanonicalPlatformPackageSeedTests
{
    // The replay order of the DES-0007 section 2 table: ck-1 to ck-14, the released navigation definition (DES-0052
    // layout-eng-24) and the four items the api carried alone that have a platform home (T-670 owner ruling 2026-11-04).
    private static readonly string[] SeedItemIds =
    [
        "platform-package-ck-1", "platform-package-ck-2", "platform-package-ck-3", "platform-package-ck-4",
        "platform-package-ck-5", "platform-package-ck-6", "platform.workshop", "platform.navigation.workshop",
        "platform-package-ck-7", "platform-package-ck-8", "platform-package-ck-9", "platform-package-ck-10",
        "platform-package-ck-11", "platform-package-ck-12", "platform.detail.form", "platform.pack.author",
        "platform-package-ck-13", "platform-package-ck-14", "platform.defaults.pack-author",
    ];

    [Fact]
    public void Fixture_covers_ck_1_through_ck_14_and_the_released_navigation_in_declared_order_and_is_valid()
    {
        Assert.Equal(SeedItemIds, PlatformPackageSeed.Manifest.Items.Select(item => item.Id));
        Assert.True(PlatformPackageReplayer.Validate(PlatformPackageSeed.Manifest).Succeeded);
    }

    [Fact]
    public void Fixture_has_the_ruled_member_inventory()
    {
        var contract = Payload("platform-package-ck-1").GetProperty("contract");

        Assert.Equal(1, contract.GetProperty("major").GetInt32());
        Assert.Equal(0, contract.GetProperty("minor").GetInt32());
        Assert.Equal([1, 1], contract.GetProperty("window").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(34, Members("platform-package-ck-2").GetArrayLength());
        Assert.Equal(4, Members("platform-package-ck-3").GetArrayLength());
        Assert.Equal(2, Members("platform-package-ck-4").GetArrayLength());
        Assert.Equal(13, Members("platform-package-ck-6").GetArrayLength());
        Assert.Equal(39, Members("platform-package-ck-7").GetArrayLength());
        Assert.Equal(3, Members("platform-package-ck-8").GetArrayLength());
        Assert.Equal(2, Payload("platform-package-ck-9").GetProperty("roles").GetArrayLength());
    }

    [Fact]
    public void The_seed_is_revision_2_0_0_in_its_export_its_package_record_and_its_navigation_definition()
    {
        using var export = JsonDocument.Parse(PlatformPackageSeed.Export());

        Assert.Equal("2.0.0", export.RootElement.GetProperty("revision").GetString());
        Assert.Equal("2.0.0", Payload("platform-package-ck-1").GetProperty("version").GetString());
        Assert.Equal("2.0.0", PlatformPackageSeed.ReleasedNavigation.Version);
    }

    [Fact]
    public void The_seed_declares_no_editor_destination_or_navigation_entry_outside_the_released_definition()
    {
        var views = Payload("platform-package-ck-7");
        foreach (var retired in new[] { "authoring", "dataExchangeAuthoring", "schedulingAuthoring" })
            Assert.False(views.TryGetProperty(retired, out _), retired);

        foreach (var item in PlatformPackageSeed.Manifest.Items.Where(item => item.Id != "platform.navigation.workshop"))
        {
            using var payload = JsonDocument.Parse(item.Content.Payload);
            var names = PropertyNames(payload.RootElement).ToHashSet(StringComparer.Ordinal);
            foreach (var retired in new[] { "navigationEntry", "editor", "surface" }) Assert.DoesNotContain(retired, names);
        }
    }

    [Fact]
    public void Mapping_profile_create_actions_and_scheduling_defaults_keep_their_values_at_named_homes()
    {
        var views = Payload("platform-package-ck-7");

        var profile = views.GetProperty("dataExchangeMappingProfile");
        Assert.Equal("hl:tabular-mapping/v1", profile.GetProperty("id").GetString());
        Assert.Equal("https://schemas.harborline.software/mapping/tabular/v1", profile.GetProperty("schemaUri").GetString());
        Assert.Equal("1.0.0", profile.GetProperty("documentVersion").GetString());

        Assert.Equal(
            [("platform.action.schedules.create", "schedule", "ScheduleDefinition"), ("platform.action.protocols.create", "protocol", "Protocol"),
             ("platform.action.constraints.create", "constraint", "Constraint")],
            views.GetProperty("schedulingCreateActions").EnumerateArray().Select(action => (
                action.GetProperty("id").GetString(), action.GetProperty("kind").GetString(), action.GetProperty("definitionKind").GetString())));

        var defaults = views.GetProperty("schedulingEditorDefaults");
        Assert.Equal("None", defaults.GetProperty("problemClass").GetString());
        Assert.Equal("none", defaults.GetProperty("planner").GetString());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("planHorizon").ValueKind);
        Assert.Empty(defaults.GetProperty("constraintScopes").EnumerateArray());
        Assert.Equal("domain-package", defaults.GetProperty("protocolInstanceOwner").GetString());
        Assert.False(defaults.TryGetProperty("grammar", out _));
    }

    [Fact]
    public void The_export_carries_exactly_one_admitted_released_navigation_definition_naming_the_three_editors()
    {
        var export = PlatformPackageSeed.Export();
        using var document = JsonDocument.Parse(export);
        var definitions = document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => TryReadDefinition(item.GetProperty("content").GetProperty("payload").GetRawText()))
            .OfType<ReleasedNavigationDefinition>()
            .ToArray();

        var definition = Assert.Single(definitions);
        ReleasedNavigationDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Install);
        var entry = Assert.Single(ReleasedNavigationDefinitionPackageExporter.Read(export, "platform.navigation.workshop"));
        Assert.Equal(("platform.navigation.workshop", "2.0.0"), (entry.DefinitionId, entry.Version));
        Assert.Equal(("platform.navigation.workshop", "2.0.0", "platform.workspace.workshop"), (definition.Identity, definition.Version, definition.Workspace));
        Assert.Equal(
            [("platform.navigation.views.author", "Create view", "views", "platform.editor.views"),
             ("platform.navigation.data-exchanges.author", "Create data exchange", "data-exchanges", "platform.editor.data-exchange"),
             ("platform.navigation.schedules.author", "Create schedule", "schedules", "platform.editor.scheduling")],
            definition.NavigationEntries.Select(value => (value.Id, value.Label, value.Pillar, value.EditorDestinationId)));
        Assert.Equal(
            [("platform.editor.views", "platform.editor.views", "ViewDefinition"),
             ("platform.editor.data-exchange", "platform.editor.data-exchange", "DataExchangeDefinition"),
             ("platform.editor.scheduling", "platform.editor.scheduling", "ScheduleDefinition,Protocol,Constraint")],
            definition.EditorDestinations.Select(value => (value.Id, value.SurfaceId, string.Join(',', value.DefinitionKinds))));
        Assert.All(definition.EditorDestinations, value => Assert.Equal(["react", "blazor"], value.Projections));
    }

    [Theory]
    [InlineData("platform.navigation.views.author", "platform.editor.views", "platform.editor.views")]
    [InlineData("platform.navigation.data-exchanges.author", "platform.editor.data-exchange", "platform.editor.data-exchange")]
    [InlineData("platform.navigation.schedules.author", "platform.editor.scheduling", "platform.editor.scheduling")]
    public void Lookup_over_the_seed_export_resolves_each_navigation_entry_to_its_editor_and_surface(string entry, string destination, string surface)
    {
        var lookup = ReleasedNavigationLookup.Install(
            ReleasedNavigationDefinitionPackageExporter.Read(PlatformPackageSeed.LoadCheckedInExport(), "platform.navigation.workshop"));

        var resolved = lookup.Resolve(entry, AllowsAll);

        Assert.Equal((entry, destination, surface), (resolved.Entry.Id, resolved.Destination.Id, resolved.Surface.Id));
    }

    [Fact]
    public void A_seed_without_the_definition_refuses_each_entry_with_no_compiled_fallback()
    {
        var export = PlatformPackageExporter.Export(Replacing(null));
        var entries = ReleasedNavigationDefinitionPackageExporter.Read(export, "platform.navigation.workshop");
        var lookup = ReleasedNavigationLookup.Install(entries);

        Assert.Empty(entries);
        var refused = Assert.Throws<DefinitionRefusalException>(() => lookup.Resolve("platform.navigation.views.author", AllowsAll));
        Assert.Equal([new DefinitionRefusal("released-navigation.navigation-entry.missing", "/navigation_entries/platform.navigation.views.author")], refused.Refusals);
    }

    [Fact]
    public void A_seed_with_an_unreadable_definition_refuses_at_install()
    {
        var export = PlatformPackageExporter.Export(Replacing(PlatformPackageContent.PresentJson("""{"identity":"platform.navigation.workshop","editors":[]}"""u8)));

        var refused = Assert.Throws<DefinitionRefusalException>(() =>
            ReleasedNavigationLookup.Install(ReleasedNavigationDefinitionPackageExporter.Read(export, "platform.navigation.workshop")));

        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal([new DefinitionRefusal("released-navigation.definition.invalid", "/", "platform.navigation.workshop@2.0.0")], refused.Refusals);
    }

    [Fact]
    public void A_malformed_platform_export_refuses_at_install()
    {
        var refused = Assert.Throws<DefinitionRefusalException>(() =>
            ReleasedNavigationDefinitionPackageExporter.Read("""{"revision":"2.0.0"}"""u8, "platform.navigation.workshop"));

        Assert.Equal([new DefinitionRefusal("released-navigation.definition.invalid", "/", "platform.navigation.workshop")], refused.Refusals);
    }

    [Fact]
    public void A_platform_export_with_two_items_of_the_definition_id_refuses_rather_than_combining_them()
    {
        var seed = PlatformPackageSeed.Manifest;
        var released = seed.Items.Single(item => item.Id == "platform.navigation.workshop");
        var export = PlatformPackageExporter.Export(new PlatformPackageManifest(seed.SchemaVersion, seed.PackageKey, seed.Revision,
            seed.Items.SelectMany(item => item == released ? new[] { item, item } : [item])));

        var refused = Assert.Throws<DefinitionRefusalException>(() =>
            ReleasedNavigationDefinitionPackageExporter.Read(export, "platform.navigation.workshop"));

        Assert.Equal([new DefinitionRefusal("released-navigation.definition.invalid", "/", "platform.navigation.workshop")], refused.Refusals);
    }

    [Fact]
    public void The_revision_and_digest_are_stable_across_exports_and_the_digest_changes_when_an_entry_changes()
    {
        var first = PublishedArtifact.FromExport(PlatformPackageSeed.Export());
        var second = PublishedArtifact.FromExport(PlatformPackageSeed.Export());
        var relabelled = PlatformPackageSeed.ReleasedNavigation with
        {
            NavigationEntries = [.. PlatformPackageSeed.ReleasedNavigation.NavigationEntries.Select(entry =>
                entry.Id == "platform.navigation.views.author" ? entry with { Label = "Create a view" } : entry)],
        };
        var changed = PublishedArtifact.FromExport(PlatformPackageExporter.Export(
            Replacing(ReleasedNavigationDefinitionPackageExporter.Export(relabelled).Content)));

        Assert.Equal(first, second);
        Assert.Equal("2.0.0", first.Version);
        Assert.Matches("^[0-9a-f]{64}$", first.Digest);
        Assert.Equal("2.0.0", changed.Version);
        Assert.NotEqual(first.Digest, changed.Digest);
    }

    [Fact]
    public void The_released_definition_refuses_an_entry_whose_editor_destination_is_missing()
    {
        var broken = PlatformPackageSeed.ReleasedNavigation with
        {
            EditorDestinations = [.. PlatformPackageSeed.ReleasedNavigation.EditorDestinations.Where(value => value.Id != "platform.editor.scheduling")],
        };

        var refused = Assert.Throws<DefinitionRefusalException>(() => ReleasedNavigationDefinitionAdmission.Validate(broken, DefinitionAdmissionPhase.Publish));

        Assert.Equal([new DefinitionRefusal("released-navigation.editor-destination.missing", "/navigation_entries/2/editor_destination_id")], refused.Refusals);
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
        Assert.Equal(SeedItemIds, target.ItemIds);
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

    [Fact]
    public void The_export_declares_the_catalogue_field_source_capability_exactly_once_at_package_level()
    {
        using var export = JsonDocument.Parse(PlatformPackageSeed.LoadCheckedInExport());

        Assert.Equal(["forms.catalogue-field-source"],
            export.RootElement.GetProperty("capabilityRequirements").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("forms.catalogue-field-source", Body("platform.detail.form")["catalogueFieldSource"]!["capabilityId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("platform.detail.form", "FormDefinition", 0)]
    [InlineData("platform.pack.author", "FormDefinition", 0)]
    [InlineData("platform.workshop", "NavWorkspaceConfig", 3)]
    [InlineData("platform.defaults.pack-author", "CascadeDefaults", 4)]
    public void The_export_states_each_api_only_item_and_its_shipped_content_kind(string key, string kind, int value)
    {
        using var export = JsonDocument.Parse(PlatformPackageSeed.LoadCheckedInExport());
        var payload = Assert.Single(export.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == key)
            .GetProperty("content").GetProperty("payload");

        Assert.Equal(key, payload.GetProperty("definitionKey").GetString());
        Assert.Equal("harborline.platform", payload.GetProperty("packageKey").GetString());
        Assert.Equal(kind, payload.GetProperty("contentKind").GetString());
        Assert.Equal(kind, ApiItems()[key]!["kind"]!.GetValue<string>());
        var row = PackContentKindRegistry.Kinds.Single(candidate => candidate.Name == kind);
        Assert.Equal((value, PackContentKindStatus.Shipped), (row.Value, row.Status));
    }

    [Theory]
    [InlineData("platform.detail.form")]
    [InlineData("platform.pack.author")]
    [InlineData("platform.defaults.pack-author")]
    public void An_api_only_item_keeps_the_api_content_unchanged(string key)
        => Assert.True(JsonNode.DeepEquals(ApiItems()[key]!["content"], Body(key)), key);

    [Fact]
    public void Workshop_config_resolves_through_ck_5_and_ck_6_to_the_api_content_and_carries_no_entry_of_its_own()
    {
        var body = Body("platform.workshop");
        Assert.DoesNotContain("itemIds", body.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"items\"", body.ToJsonString(), StringComparison.Ordinal);

        var workspace = body["seedWorkspaces"]![0]!.AsObject();
        Assert.Equal("platform-package-ck-5", workspace["workspaceItem"]!.GetValue<string>());
        workspace.Remove("workspaceItem");
        workspace["labelKey"] = Payload("platform-package-ck-5").GetProperty("labelKey").GetString();
        var group = workspace["groups"]![0]!.AsObject();
        Assert.Equal("platform-package-ck-6", group["entriesItem"]!.GetValue<string>());
        group.Remove("entriesItem");
        var entries = Members("platform-package-ck-6").EnumerateArray()
            .Select(member => (Id: member.GetProperty("id").GetString()!, Label: member.GetProperty("labelKey").GetString()!)).ToArray();
        group["itemIds"] = new JsonArray(entries.Select(entry => (JsonNode)entry.Id).ToArray());
        group["items"] = new JsonArray(entries.Select(entry => (JsonNode)new JsonObject { ["id"] = entry.Id, ["labelKey"] = entry.Label }).ToArray());

        Assert.True(JsonNode.DeepEquals(ApiItems()["platform.workshop"]!["content"], body), body.ToJsonString());
    }

    [Fact]
    public void Released_navigation_uses_the_one_workspace_and_register_the_workshop_config_names()
    {
        var pillars = Members("platform-package-ck-6").EnumerateArray().Select(member => member.GetProperty("pillar").GetString()).ToArray();

        Assert.Equal(Payload("platform-package-ck-5").GetProperty("id").GetString(), PlatformPackageSeed.ReleasedNavigation.Workspace);
        Assert.All(PlatformPackageSeed.ReleasedNavigation.NavigationEntries, entry => Assert.Contains(entry.Pillar, pillars));
    }

    [Theory]
    [InlineData("platform.detail.form")]
    [InlineData("platform.pack.author")]
    public void Form_text_passes_the_api_released_form_text_rules(string key)
        => Assert.Empty(FormTextFaults(Body(key)));

    [Theory]
    [InlineData("""{"overlay":{"title":"plain"}}""", "overlay.title")]
    [InlineData("""{"overlay":{"title":{"defaultLocale":"en","values":{"fr":"x"}}}}""", "overlay.title")]
    [InlineData("""{"overlay":{"fields":{"a":{}}}}""", "overlay.fields.a.label")]
    [InlineData("""{"overlay":{"sections":[{"id":"s"}]}}""", "overlay.sections[0].title")]
    [InlineData("""{"title":"no overlay"}""", "overlay")]
    public void The_form_text_rules_refuse_what_the_api_refuses(string form, string fault)
        => Assert.Equal([fault], FormTextFaults(JsonNode.Parse(form)!));

    // The rule of harborline-api PlatformPackPreloadHostedService.ValidateReleasedFormText, written separately for the seed's forms.
    private static List<string> FormTextFaults(JsonNode form)
    {
        var faults = new List<string>();
        if (form["overlay"] is not JsonObject overlay) return ["overlay"];
        void Text(JsonNode? node, string path, bool required)
        {
            if (node is null && !required) return;
            var locale = node is JsonObject text && text.Count == 2 ? text["defaultLocale"]?.GetValue<string>() : null;
            var values = locale is null ? null : node!["values"] as JsonObject;
            if (string.IsNullOrWhiteSpace(locale) || values is null || !values.ContainsKey(locale)
                || values.Any(value => string.IsNullOrWhiteSpace(value.Key) || string.IsNullOrWhiteSpace(value.Value?.GetValue<string>())))
                faults.Add(path);
        }
        Text(overlay["title"], "overlay.title", required: false);
        Text(overlay["description"], "overlay.description", required: false);
        foreach (var field in overlay["fields"]?.AsObject() ?? [])
            Text(field.Value?["label"], $"overlay.fields.{field.Key}.label", required: true);
        foreach (var (section, index) in (overlay["sections"]?.AsArray() ?? []).Select((section, index) => (section, index)))
            Text(section?["title"], $"overlay.sections[{index}].title", required: true);
        return faults;
    }

    private static JsonNode Body(string key) => JsonNode.Parse(Payload(key).GetProperty("body").GetRawText())!;

    // The api's own copy of the four items, which the seed does not read.
    private static JsonNode ApiItems() => JsonNode.Parse(File.ReadAllText(
        RecordWriteJournalBindingTests.Fixture("api-platform-1.6.1/platform-only-items.json")))!;

    private static JsonElement Payload(string id)
    {
        var item = PlatformPackageSeed.Manifest.Items.Single(item => item.Id == id);
        using var document = JsonDocument.Parse(item.Content.Payload);
        return document.RootElement.Clone();
    }

    private static JsonElement Members(string id) => Payload(id).GetProperty("members");

    private static readonly IReleasedNavigationAccess AllowsAll = new AllowingAccess();

    // The seed manifest with its released navigation item's content replaced, or the item removed when content is null.
    private static PlatformPackageManifest Replacing(PlatformPackageContent? content)
    {
        var seed = PlatformPackageSeed.Manifest;
        var items = seed.Items
            .Where(item => content is not null || item.Id != "platform.navigation.workshop")
            .Select(item => content is not null && item.Id == "platform.navigation.workshop"
                ? new PlatformPackageItem(item.Id, item.Stage, item.Dependencies, content)
                : item);
        return new(seed.SchemaVersion, seed.PackageKey, seed.Revision, items);
    }

    private static ReleasedNavigationDefinition? TryReadDefinition(string payload)
    {
        try { return ReleasedNavigationDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(payload)); }
        catch (JsonException) { return null; }
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => PropertyNames(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    private sealed class AllowingAccess : IReleasedNavigationAccess
    {
        public bool CanRead(ReleasedNavigationDefinition definition, ReleasedNavigationEntry entry, ReleasedEditorDestination destination, ReleasedSurface surface)
            => true;
    }

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
