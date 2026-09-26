using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

/// <summary>
/// T-600. The definition lives in the shared builder-definitions catalogue (DefinitionKind.DataExchange)
/// with the foundation's intent validator bound as its admission; the platform package carries the
/// provider-neutral closure, manifest and digest.
/// </summary>
public sealed class DataExchangeDefinitionStoreTests
{
    private const string Tenant = "tenant-a";
    private const string Key = "exchange.customers";
    private static readonly DefinitionKey CatalogueKey = new(Tenant, DefinitionKind.DataExchange, Key);

    [Fact]
    [Trait("Holds", "data-exchange-ck-7")]
    public async Task Catalogue_keeps_published_heads_immutable_and_restore_registers_a_new_draft()
    {
        var catalogue = Catalogue();

        await catalogue.SaveDraftAsync(Document(Definition("1.0.0")), 0, "draft-1");
        await catalogue.PublishAsync(CatalogueKey, "1.0.0", 1, "publish-1");
        var overwrite = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.SaveDraftAsync(Document(Definition("1.0.0") with { Title = "Edited" }), 2, "edit").AsTask());
        Assert.Contains(overwrite.Refusals, refusal => refusal.Code == "definition.version_immutable");
        var restored = await catalogue.RestoreAsDraftAsync(CatalogueKey, "1.0.0", "1.1.0", "1.1.0", 2, "restore");

        Assert.Equal(DefinitionStatus.Draft, restored.Status);
        Assert.Equal("1.0.0", restored.RestoredFromVersionId);
        Assert.Equal("1.1.0", restored.Document.Version);
        // The shared catalogue copies the body verbatim; the member owns consistency before publish.
        Assert.Equal("1.0.0", DataExchangeDefinitionJson.Deserialize(restored.Document.BodyJson).Version);
        var stale = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.PublishAsync(CatalogueKey, "1.1.0", 3, "publish-stale").AsTask());
        Assert.Contains(stale.Refusals, refusal => refusal.Code == "definition.catalogue_mismatch" && refusal.Pointer == "/version");
        Assert.Equal("1.0.0", (await catalogue.GetPublishedHeadAsync(CatalogueKey))?.Document.Version);

        await catalogue.SaveDraftAsync(Document(Definition("1.1.0")), 3, "re-version");
        await catalogue.PublishAsync(CatalogueKey, "1.1.0", 4, "publish-2");

        var history = await catalogue.ListHistoryAsync(CatalogueKey);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], history.Select(revision => revision.Revision));
        Assert.Equal(
            [DefinitionStatus.Draft, DefinitionStatus.Published, DefinitionStatus.Draft, DefinitionStatus.Draft, DefinitionStatus.Published],
            history.Select(revision => revision.Status));
        Assert.Equal("1.1.0", (await catalogue.GetPublishedHeadAsync(CatalogueKey))?.Document.Version);
        Assert.NotNull(await catalogue.ResolvePublishedAsync(new(CatalogueKey, "1.0.0")));
        Assert.Null(await catalogue.ResolvePublishedAsync(new(CatalogueKey, "2.0.0")));
    }

    [Fact]
    public async Task Published_head_resolves_the_latest_published_semver_and_ignores_newer_drafts()
    {
        var catalogue = Catalogue();
        await catalogue.SaveDraftAsync(Document(Definition("1.0.0")), 0, "d1");
        await catalogue.PublishAsync(CatalogueKey, "1.0.0", 1, "p1");
        await catalogue.SaveDraftAsync(Document(Definition("1.1.0")), 2, "d2");
        await catalogue.PublishAsync(CatalogueKey, "1.1.0", 3, "p2");
        await catalogue.SaveDraftAsync(Document(Definition("2.0.0")), 4, "d3");

        var resolver = new CatalogueResolver(catalogue);
        var head = await resolver.ResolvePublishedHeadAsync(Tenant, Key);

        Assert.Equal("1.1.0", head?.Version);
        Assert.Null(await resolver.ResolvePublishedHeadAsync(Tenant, "exchange.missing"));
    }

    [Fact]
    [Trait("Holds", "data-exchange-ck-1")]
    [Trait("Holds", "data-exchange-ck-2")]
    [Trait("Holds", "data-exchange-ck-3")]
    [Trait("Holds", "data-exchange-ck-4")]
    [Trait("Holds", "data-exchange-ck-5")]
    [Trait("Holds", "data-exchange-ck-6")]
    [Trait("Holds", "data-exchange-ck-9")]
    [Trait("Holds", "data-exchange-ck-11")]
    [Trait("Holds", "data-exchange-ck-12")]
    [Trait("Holds", "data-exchange-ck-13")]
    [Trait("Holds", "data-exchange-ck-14")]
    [Trait("Holds", "data-exchange-ck-15")]
    [Trait("Holds", "data-exchange-ck-16")]
    [Trait("Holds", "data-exchange-ck-22")]
    [Trait("Holds", "data-exchange-ck-23")]
    [Trait("Holds", "data-exchange-eng-3")]
    public void Every_definition_member_round_trips_through_canonical_json()
    {
        var definition = Definition("1.0.0") with
        {
            Source = Definition("1.0.0").Source with
            {
                Parameters = new Dictionary<string, string> { ["batchSize"] = "500", ["encoding"] = "utf-8" },
                FormatId = "csv",
            },
            Mapping = Fixtures.Mapping() with
            {
                Columns =
                [
                    new MappingColumn("CustomerNumber", "string", true, "/customerNumber"),
                    new MappingColumn("Tags", "string", false, "/tags", Default: "", Null: ["NA", "-"], Separator: ";",
                        Extensions: new Dictionary<string, string> { ["hl:transform"] = "trim" }),
                ],
            },
            ReferenceSet = new("dataset.customers", "pack:customers.csv", "feed:customers"),
        };

        var first = DataExchangeDefinitionJson.SerializeCanonical(definition);
        var parsed = DataExchangeDefinitionJson.Deserialize(first);
        var second = DataExchangeDefinitionJson.SerializeCanonical(parsed);

        Assert.Equal(first, second);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.Empty(DataExchangeDefinitionAdmission.Validate(parsed, DataExchangeAdmissionPhase.Install, Sources("batchSize", "encoding")));
        Assert.Equal(1, parsed.SchemaVersion);
        Assert.Equal("erpnext", parsed.ExchangeKind);
        Assert.Equal("Customer opening load", parsed.Title);
        Assert.Equal(definition.Envelope!.Requires[0], parsed.Envelope!.Requires[0]);
        Assert.Equal(DataExchangeCascadeLayer.Tenant, parsed.Envelope.CascadeLayer);
        Assert.Equal("tenant", parsed.Envelope.Provenance.GetProperty("kind").GetString());
        Assert.Equal("500", parsed.Source.Parameters["batchSize"]);
        Assert.Equal("secret://erpnext/customer-import", parsed.Source.SecretReference);
        Assert.Equal(TabularMappingProfile.Family, parsed.Mapping.Profile);
        Assert.Equal(TabularMappingProfile.SchemaUri, parsed.Mapping.SchemaUri);
        Assert.Equal("records.customer/v1", parsed.Mapping.Target.Contract);
        Assert.Equal(["NA", "-"], parsed.Mapping.Columns[1].Null);
        Assert.Equal("trim", parsed.Mapping.Columns[1].Extensions!["hl:transform"]);
        Assert.Equal("upsert", parsed.Mapping.Extensions["hl:operation"]);
        Assert.Equal(MappingMetadataPrecedence.TenantOverPack, parsed.MetadataPrecedence);
        Assert.Equal(["CustomerNumber"], parsed.ExternalKeyColumns);
        Assert.Equal(ReplayPolicy.AppendDeduplicate, parsed.ReplayPolicy);
        Assert.Equal("schedule.weekly", parsed.RefreshScheduleReference);
        Assert.Equal(definition.ReferenceSet, parsed.ReferenceSet);
        var json = Encoding.UTF8.GetString(first);
        Assert.Contains("\"replay_policy\":\"append_dedup\"", json, StringComparison.Ordinal);
        Assert.Contains("\"metadata_precedence\":\"tenant_over_pack\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("exchange_kind", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Holds", "data-exchange-ck-8")]
    [Trait("Holds", "data-exchange-ck-25")]
    public void Pack_export_carries_content_kind_11_through_the_platform_package_with_closure_and_digest()
    {
        var entry = DataExchangeDefinitionPackExporter.Export(Definition("1.0.0"), Sources());
        var manifest = new PlatformPackageManifest(1, "tenant-a.customers", "1.0.0",
        [
            new PlatformPackageItem("package", PlatformSeedStage.PackageRecord, [], PlatformPackageContent.PresentJson("{}"u8)),
            new PlatformPackageItem($"data-exchange:{entry.DefinitionId}@{entry.Version}", PlatformSeedStage.SealedDefinitions,
                ["package"], PlatformPackageContent.PresentJson(entry.Content.Span)),
        ]);

        var exported = PlatformPackageExporter.Export(manifest);
        using var document = JsonDocument.Parse(exported);
        var root = document.RootElement;

        Assert.Equal(12, entry.ContentKind); // the api's PackContentKind.DataExchangeDefinition; 11 is ScheduleDefinition
        Assert.Equal(DataExchangePackIdentity.ContentKind, entry.ContentKind);
        Assert.True(PlatformPackageReplayer.Validate(manifest).Succeeded);
        Assert.True(PlatformPackageExporter.Verify(manifest, exported));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("closure").GetProperty("dependencies").ValueKind);
        Assert.Equal("sha256", root.GetProperty("digest").GetProperty("algorithm").GetString());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("digest").GetProperty("value").GetString());
        var payload = root.GetProperty("items")[1].GetProperty("content").GetProperty("payload");
        Assert.Equal(Key, payload.GetProperty("key").GetString());
        Assert.Equal("secret://erpnext/customer-import", payload.GetProperty("source").GetProperty("secret_reference").GetString());
        var text = Encoding.UTF8.GetString(exported);
        foreach (var operational in new[] { "dry_run", "checkpoint", "cursor", "attempt", "retain_until", "cadence", "password" })
            Assert.DoesNotContain(operational, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Holds", "data-exchange-ck-22")]
    [Trait("Holds", "data-exchange-auth-25")]
    public void Create_does_not_seed_a_cadence_and_refresh_is_only_a_schedule_citation()
    {
        var created = Definition("1.0.0") with { RefreshScheduleReference = null };

        var json = Encoding.UTF8.GetString(DataExchangeDefinitionJson.SerializeCanonical(created));

        Assert.DoesNotContain("refresh_schedule_reference", json, StringComparison.Ordinal);
        Assert.DoesNotContain("cadence", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cron", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(DataExchangeDefinitionAdmission.Validate(created, DataExchangeAdmissionPhase.Publish, Sources()));
    }

    public static TheoryData<string, string, string> AuthoringRefusals => new()
    {
        { "unregistered-kind", "definition.source_capability_unregistered", "/source/capability_id" },
        { "missing-dedup-key", "definition.external_key_required", "/external_key_columns" },
        { "external-key-unknown", "definition.external_key_unknown", "/external_key_columns/Unknown" },
        { "private-dto-target", "mapping.target_not_canonical", "/mapping/target/contract" },
        { "unknown-family", "mapping.profile_unknown", "/mapping/profile" },
        { "schema-mismatch", "mapping.schema_mismatch", "/mapping/schemaUri" },
        { "unsupported-major", "mapping.major_unsupported", "/mapping/version" },
        { "unknown-hl-term", "mapping.extension_unknown", "/mapping/extensions/hl:arbitraryCode" },
        { "credential", "definition.credential_forbidden", "/source/parameters/password" },
        { "cursor", "definition.cursor_forbidden", "/source/parameters/cursor" },
        { "retention", "definition.retention_forbidden", "/source/parameters/retentionDays" },
        { "secret-value", "definition.secret_reference_invalid", "/source/secret_reference" },
        { "envelope-mismatch", "definition.envelope_mismatch", "/envelope" },
        { "schema-version", "definition.schema_version_unsupported", "/schema_version" },
        { "no-format", "definition.format_required", "/source/format_id" },
    };

    [Theory]
    [MemberData(nameof(AuthoringRefusals))]
    [Trait("Holds", "data-exchange-ck-13")]
    [Trait("Holds", "data-exchange-eng-2")]
    [Trait("Holds", "data-exchange-eng-3")]
    [Trait("Holds", "data-exchange-auth-15")]
    [Trait("Holds", "data-exchange-auth-17")]
    [Trait("Holds", "data-exchange-auth-18")]
    [Trait("Holds", "data-exchange-auth-21")]
    public async Task Each_authoring_refusal_admits_nothing_and_its_valid_counterpart_admits_exactly_one(
        string variant, string code, string pointer)
    {
        var catalogue = Catalogue();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.SaveDraftAsync(Document(Mutate(Definition("1.0.0"), variant)), 0, "refused").AsTask());
        Assert.Contains(refused.Refusals, refusal => refusal.Code == code && refusal.Pointer == pointer);
        Assert.Empty(await catalogue.ListHistoryAsync(CatalogueKey));

        await catalogue.SaveDraftAsync(Document(Definition("1.0.0")), 0, "admitted");
        Assert.Single(await catalogue.ListHistoryAsync(CatalogueKey));
    }

    [Theory]
    [InlineData("", "[]", "definition.settings_not_object", "/")]
    [InlineData("mapping", "\"nonsense\"", "definition.settings_not_object", "/mapping")]
    [InlineData("rollback", "\"all-or-nothing\"", "definition.rollback_refused", "/rollback")]
    [InlineData("all_or_nothing_rollback", "true", "definition.rollback_refused", "/all_or_nothing_rollback")]
    [InlineData("export", "{\"shape\":\"RowSet\"}", "definition.export_refused", "/export")]
    [InlineData("direction", "\"outbound\"", "definition.export_refused", "/direction")]
    [InlineData("connection_state", "\"open\"", "definition.body_invalid", "/")]
    [Trait("Holds", "data-exchange-ck-24")]
    [Trait("Holds", "data-exchange-auth-16")]
    [Trait("Holds", "data-exchange-auth-17")]
    [Trait("Holds", "data-exchange-auth-20")]
    [Trait("Holds", "data-exchange-auth-23")]
    public async Task Install_refuses_non_object_settings_rollback_export_and_unknown_members_with_zero_publication(
        string member, string valueJson, string code, string pointer)
    {
        var body = member.Length == 0 ? valueJson : Tamper(Definition("1.0.0"), member, valueJson);
        var catalogue = Catalogue();

        var refusals = DataExchangeDefinitionAdmission.AdmitJson(body, DataExchangeAdmissionPhase.Install, Sources());
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.SaveDraftAsync(new(CatalogueKey, "1.0.0", "1.0.0", body), 0, "install").AsTask());

        Assert.Contains(refusals, refusal => refusal.Code == code && refusal.Pointer == pointer);
        Assert.Contains(refused.Refusals, refusal => refusal.Code == code);
        Assert.Empty(await catalogue.ListHistoryAsync(CatalogueKey));
        Assert.Null(await catalogue.GetPublishedHeadAsync(CatalogueKey));
    }

    [Fact]
    [Trait("Holds", "data-exchange-eng-2")]
    public async Task Publish_readmits_and_refuses_a_kind_the_host_no_longer_registers()
    {
        var sources = new MutableSources();
        var catalogue = Catalogue(sources);
        await catalogue.SaveDraftAsync(Document(Definition("1.0.0")), 0, "draft");
        sources.Registered = false;

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.PublishAsync(CatalogueKey, "1.0.0", 1, "publish").AsTask());

        Assert.Contains(refused.Refusals, refusal => refusal.Code == "definition.source_capability_unregistered");
        Assert.Null(await catalogue.GetPublishedHeadAsync(CatalogueKey));
        Assert.Equal(DefinitionStatus.Draft, Assert.Single(await catalogue.ListHistoryAsync(CatalogueKey)).Status);
    }

    [Fact]
    public void Envelope_is_optional_while_authoring_and_required_to_publish_or_install()
    {
        var draft = Definition("1.0.0") with { Envelope = null };

        Assert.Empty(DataExchangeDefinitionAdmission.Validate(draft, DataExchangeAdmissionPhase.Author, Sources()));
        Assert.Contains(DataExchangeDefinitionAdmission.Validate(draft, DataExchangeAdmissionPhase.Publish, Sources()),
            refusal => refusal.Code == "definition.envelope_required");
        Assert.Contains(DataExchangeDefinitionAdmission.Validate(draft, DataExchangeAdmissionPhase.Install, Sources()),
            refusal => refusal.Code == "definition.envelope_required");
        Assert.Throws<DataExchangeAdmissionException>(() => DataExchangeDefinitionPackExporter.Export(draft, Sources()));
    }

    [Theory]
    [InlineData("apiKey")]
    [InlineData("accessToken")]
    [InlineData("clientSecret")]
    [InlineData("pass.word")]
    [Trait("Holds", "data-exchange-ck-8")]
    [Trait("Holds", "data-exchange-ck-25")]
    [Trait("Holds", "data-exchange-auth-17")]
    public void Credential_aliases_are_refused_even_when_a_capability_schema_declares_them(string parameter)
    {
        var definition = Definition("1.0.0") with
        {
            Source = Definition("1.0.0").Source with
            {
                Parameters = new Dictionary<string, string> { [parameter] = "must-not-export" },
            },
        };

        var refusals = DataExchangeDefinitionAdmission.Validate(definition, DataExchangeAdmissionPhase.Author, Sources(parameter));

        Assert.Contains(refusals, refusal => refusal.Code == "definition.credential_forbidden");
    }

    [Theory]
    [InlineData("plain-text-secret")]
    [InlineData("secret://")]
    [InlineData("secretref:bad reference")]
    [InlineData("https://vault.example/secret")]
    public void Secret_reference_must_use_the_opaque_reference_grammar(string secretReference)
    {
        var definition = Definition("1.0.0") with
        {
            Source = Definition("1.0.0").Source with { SecretReference = secretReference },
        };

        var refusals = DataExchangeDefinitionAdmission.Validate(definition, DataExchangeAdmissionPhase.Author, Sources());

        Assert.Contains(refusals, refusal => refusal.Code == "definition.secret_reference_invalid");
    }

    private static DataExchangeDefinition Mutate(DataExchangeDefinition definition, string variant) => variant switch
    {
        "unregistered-kind" => definition with { Source = definition.Source with { CapabilityId = "unregistered" } },
        "missing-dedup-key" => definition with { ExternalKeyColumns = [] },
        "external-key-unknown" => definition with { ExternalKeyColumns = ["Unknown"] },
        "private-dto-target" => definition with { Mapping = definition.Mapping with { Target = new("connector.erpnext.dto/v1", "/CustomerDto") } },
        "unknown-family" => definition with { Mapping = definition.Mapping with { Profile = "csvw-ish/v1" } },
        "schema-mismatch" => definition with { Mapping = definition.Mapping with { SchemaUri = TabularMappingProfile.SchemaUri + "-next" } },
        "unsupported-major" => definition with { Mapping = definition.Mapping with { Version = "2.0.0" } },
        "unknown-hl-term" => definition with { Mapping = definition.Mapping with { Extensions = new Dictionary<string, string> { ["hl:arbitraryCode"] = "run()" } } },
        "credential" => Parameter(definition, "password", "secret"),
        "cursor" => Parameter(definition, "cursor", "secret-cursor"),
        "retention" => Parameter(definition, "retentionDays", "1"),
        "secret-value" => definition with { Source = definition.Source with { SecretReference = "hunter2" } },
        "envelope-mismatch" => definition with { Envelope = definition.Envelope! with { Identity = "exchange.other" } },
        "schema-version" => definition with { SchemaVersion = 2 },
        "no-format" => definition with { Source = definition.Source with { FormatId = " " } },
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static DataExchangeDefinition Parameter(DataExchangeDefinition definition, string name, string value)
        => definition with { Source = definition.Source with { Parameters = new Dictionary<string, string> { [name] = value } } };

    private static string Tamper(DataExchangeDefinition definition, string member, string valueJson)
    {
        var body = JsonNode.Parse(DataExchangeDefinitionJson.SerializeCanonical(definition))!.AsObject();
        body[member] = JsonNode.Parse(valueJson);
        return body.ToJsonString();
    }

    private static InMemoryVersionedDefinitionStore Catalogue(ISourceParameterSchemaRegistry? sources = null)
    {
        sources ??= Sources();
        return new(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.DataExchange] = (document, phase) => DataExchangeDefinitionAdmission
                .AdmitJson(
                    document.BodyJson,
                    phase == DefinitionAdmissionPhase.Publish ? DataExchangeAdmissionPhase.Publish : DataExchangeAdmissionPhase.Author,
                    sources,
                    new(document.Key.Tenant, document.Key.DefinitionId, document.Version))
                .Select(refusal => new DefinitionRefusal(refusal.Code, refusal.Pointer))
                .ToArray(),
        });
    }

    private static DefinitionDocument Document(DataExchangeDefinition definition) => new(
        new(definition.Tenant, DefinitionKind.DataExchange, definition.Key),
        definition.Version,
        definition.Version,
        Encoding.UTF8.GetString(DataExchangeDefinitionJson.SerializeCanonical(definition)));

    private static ISourceParameterSchemaRegistry Sources(params string[] parameters)
        => new OneParameterSchema(parameters);

    private static DataExchangeDefinition Definition(string version) => new(
        Tenant,
        Key,
        version,
        "Customer opening load",
        new ExchangeSourceBinding(
            "erpnext",
            "4.1.0",
            "secret://erpnext/customer-import",
            new Dictionary<string, string>()),
        Fixtures.Mapping(),
        ReplayPolicy.AppendDeduplicate,
        ["CustomerNumber"],
        "schedule.weekly",
        Envelope: new(
            Key,
            version,
            Tenant,
            DataExchangeCascadeLayer.Tenant,
            JsonSerializer.SerializeToElement(new { kind = "tenant" }),
            [new("records.customer", "1.0.0")]));
}

/// <summary>Registers only <c>erpnext</c> 4.1.0 with the named parameters; every other kind is unregistered.</summary>
internal sealed class OneParameterSchema(params string[] parameters) : ISourceParameterSchemaRegistry
{
    public SourceParameterSchema? Resolve(string capabilityId, string connectorVersion)
        => capabilityId == "erpnext" && connectorVersion == "4.1.0"
            ? new(new HashSet<string>(parameters, StringComparer.Ordinal))
            : null;
}

internal sealed class MutableSources : ISourceParameterSchemaRegistry
{
    public bool Registered { get; set; } = true;

    public SourceParameterSchema? Resolve(string capabilityId, string connectorVersion)
        => Registered ? new(new HashSet<string>(StringComparer.Ordinal)) : null;
}

/// <summary>The host-side binding from the shared catalogue to the interpreter's resolver port.</summary>
internal sealed class CatalogueResolver(IVersionedDefinitionStore catalogue) : IDataExchangeDefinitionResolver
{
    public async ValueTask<DataExchangeDefinition?> ResolvePublishedHeadAsync(
        string tenant, string key, CancellationToken cancellationToken = default)
    {
        var head = await catalogue.GetPublishedHeadAsync(new(tenant, DefinitionKind.DataExchange, key), cancellationToken)
            .ConfigureAwait(false);
        return head is null ? null : DataExchangeDefinitionJson.Deserialize(head.Document.BodyJson);
    }
}
