using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.MeasureCatalogue;
using Harborline.Blocks.Reports.Definitions;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class ReportDefinitionCatalogueTests
{
    private static InMemoryVersionedDefinitionStore Store() => new(new System.Collections.Generic.Dictionary<DefinitionKind, DefinitionAdmission>
    {
        [DefinitionKind.Reports] = ReportDefinitionCatalogue.Admission(new(1, 0, 1), new("empty", new EmptyCatalogue())),
    });

    private sealed class EmptyCatalogue : IMeasureCatalogue
    {
        public ValueTask<MeasureDescriptor?> ResolveAsync(MeasureRef reference, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.FromResult<MeasureDescriptor?>(null);
        public ValueTask<MeasureResult> EvaluateAsync(MeasureRef reference, MeasureRequest request, System.Threading.CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Admission cannot evaluate.");
    }

    private static ReportDefinition Definition() => new("tenant-r", "report-a", "1.0.0", [],
        new("report-a", "1.0.0", "tenant-r", new(1, 0), LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { producer = "fixture" }), "business", true, [new("reports", "1.0.0")]),
        [new("count", ReportParameterType.Integer, new IntegerReportParameterDefault(7))]);

    [Fact, Trait("Holds", "reports-ck-3")]
    public async Task reports_ck_3_restore_is_a_new_draft_and_publications_are_immutable()
    {
        var store = Store();
        var catalogue = new ReportDefinitionCatalogue(store);
        await catalogue.SaveDraftAsync(Definition(), 0, "save");
        var published = await catalogue.PublishAsync("tenant-r", "report-a", "1.0.0", 1, "publish");
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalogue.SaveDraftAsync(Definition(), 2, "overwrite"));
        Assert.Equal("definition.version_immutable", Assert.Single(error.Refusals).Code);
        var restored = await catalogue.RestoreAsDraftAsync("tenant-r", "report-a", "1.0.0", "2.0.0", 2, "restore");
        Assert.Equal(DefinitionStatus.Draft, restored.Revision.Status);
        Assert.Equal("2.0.0", restored.Definition.Version);
        Assert.Equal("2.0.0", restored.Definition.Envelope!.Version);
        Assert.Equal("report-a@1.0.0", restored.Revision.RestoredFromVersionId);
        Assert.Equal(published.Revision, (await catalogue.ResolveHeadAsync("tenant-r", "report-a")).Revision);
        await catalogue.PublishAsync("tenant-r", "report-a", "2.0.0", 3, "publish-restored");
        Assert.Equal("2.0.0", (await catalogue.ResolveHeadAsync("tenant-r", "report-a")).Definition.Version);
        var history = await store.ListHistoryAsync(new("tenant-r", DefinitionKind.Reports, "report-a"));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, history.Select(item => item.Revision));
        Assert.Equal(published.Revision, history[1]);
    }

    [Fact, Trait("Holds", "reports-ck-1"), Trait("Holds", "reports-ck-2")]
    public async Task reports_ck_1_ck_2_pack_preserves_digest_and_full_envelope_without_authority()
    {
        var source = new ReportDefinitionCatalogue(Store());
        await source.SaveDraftAsync(Definition(), 0, "save");
        var published = await source.PublishAsync("tenant-r", "report-a", "1.0.0", 1, "publish");
        var pack = ReportDefinitionCatalogue.Export(published);
        Assert.Equal(9, pack.ContentKind);
        using var json = JsonDocument.Parse(pack.BodyJson);
        var envelope = json.RootElement.GetProperty("envelope");
        Assert.Equal("report-a", envelope.GetProperty("identity").GetString());
        Assert.Equal("1.0.0", envelope.GetProperty("version").GetString());
        Assert.Equal("tenant-r", envelope.GetProperty("tenant").GetString());
        Assert.Equal("domain_package", envelope.GetProperty("cascade_layer").GetString());
        Assert.Equal("fixture", envelope.GetProperty("provenance").GetProperty("producer").GetString());
        Assert.Equal("business", envelope.GetProperty("retention_class").GetString());
        Assert.True(envelope.GetProperty("legal_hold").GetBoolean());
        Assert.Equal("reports", envelope.GetProperty("requires")[0].GetProperty("capability").GetString());
        Assert.DoesNotContain("authority", pack.BodyJson, StringComparison.OrdinalIgnoreCase);
        var target = new ReportDefinitionCatalogue(Store());
        var installed = await target.ImportDraftAsync(pack, 0, "install");
        Assert.Equal(published.Revision.Digest, installed.Revision.Digest);
        Assert.Equal(new IntegerReportParameterDefault(7), Assert.Single(installed.Definition.Parameters!).Default);
        await target.PublishAsync("tenant-r", "report-a", "1.0.0", 1, "publish");
        Assert.Equal(pack, ReportDefinitionCatalogue.Export(await target.ResolveHeadAsync("tenant-r", "report-a")));
    }

    [Fact, Trait("Holds", "reports-eng-1")]
    public async Task reports_eng_1_head_refuses_missing_and_drafts_and_chooses_semver()
    {
        var catalogue = new ReportDefinitionCatalogue(Store());
        var missing = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalogue.ResolveHeadAsync("tenant-r", "report-a"));
        Assert.Equal("report.definition.no_published_head", Assert.Single(missing.Refusals).Code);
        var draft = await catalogue.SaveDraftAsync(Definition(), 0, "save");
        await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalogue.ResolveHeadAsync("tenant-r", "report-a"));
        var refused = Assert.Throws<DefinitionRefusalException>(() => ReportDefinitionCatalogue.Export(draft));
        Assert.Equal("report.definition.published_version_required", Assert.Single(refused.Refusals).Code);
        await catalogue.PublishAsync("tenant-r", "report-a", "1.0.0", 1, "publish");
        await catalogue.RestoreAsDraftAsync("tenant-r", "report-a", "1.0.0", "0.5.0", 2, "older");
        await catalogue.PublishAsync("tenant-r", "report-a", "0.5.0", 3, "publish-older");
        Assert.Equal("1.0.0", (await catalogue.ResolveHeadAsync("tenant-r", "report-a")).Definition.Version);
        await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalogue.ResolveHeadAsync("foreign", "report-a"));
    }

    [Fact, Trait("Holds", "reports-eng-2"), Trait("Holds", "reports-ck-2")]
    public async Task reports_eng_2_admission_refuses_invalid_writes_and_rechecks_publish()
    {
        var store = Store();
        var catalogue = new ReportDefinitionCatalogue(store);
        var invalid = Definition() with { Measures = [new("unknown.measure")] };
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalogue.SaveDraftAsync(invalid, 0, "bad"));
        Assert.Equal("report.measure_unknown", Assert.Single(refused.Refusals).Code);
        Assert.Empty(await store.ListHistoryAsync(new("tenant-r", DefinitionKind.Reports, "report-a")));
        await catalogue.SaveDraftAsync(Definition() with { Envelope = null }, 0, "draft");
        var publish = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalogue.PublishAsync("tenant-r", "report-a", "1.0.0", 1, "publish"));
        Assert.Equal("definition.envelope_required", Assert.Single(publish.Refusals).Code);
        var badEnvelope = Definition() with { Envelope = Definition().Envelope! with { RetentionClass = "" } };
        var envelope = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalogue.SaveDraftAsync(badEnvelope, 1, "bad-envelope"));
        Assert.Equal("/envelope/retention_class", Assert.Single(envelope.Refusals).Pointer);
    }
    [Theory, Trait("Holds", "reports-ck-3"), Trait("Holds", "reports-eng-2")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task reports_ck_3_direct_store_refuses_repeated_versions_before_commit(bool inEnvelope)
    {
        var store = Store();
        var body = System.Text.Json.Nodes.JsonNode.Parse("""
            {"tenant":"tenant-r","key":"report-a","measures":[]}
            """)!.AsObject();
        if (inEnvelope)
            body["envelope"] = System.Text.Json.Nodes.JsonNode.Parse("""
                {"identity":"report-a","version":"1.0.0","tenant":"tenant-r","contract":{"major":1,"minor":0},
                 "cascade_layer":"domain_package","provenance":{},"retention_class":"business","legal_hold":false,"requires":[]}
                """);
        else body["version"] = "1.0.0";
        var key = new DefinitionKey("tenant-r", DefinitionKind.Reports, "report-a");
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(new(key, "report-a@1.0.0", "1.0.0", body.ToJsonString()), 0, "raw"));
        Assert.Equal("report.definition.body_invalid", Assert.Single(refused.Refusals).Code);
        Assert.Empty(await store.ListHistoryAsync(key));
    }

    [Theory, Trait("Holds", "reports-eng-2")]
    [InlineData("measures")]
    [InlineData("parameters")]
    public async Task reports_eng_2_null_elements_refuse_on_raw_and_pack_writes(string collection)
    {
        var store = Store();
        var key = new DefinitionKey("tenant-r", DefinitionKind.Reports, "report-a");
        var body = System.Text.Json.Nodes.JsonNode.Parse("""
            {"tenant":"tenant-r","key":"report-a","measures":[]}
            """)!.AsObject();
        body[collection] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        var raw = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(new(key, "report-a@1.0.0", "1.0.0", body.ToJsonString()), 0, "raw"));
        Assert.Equal("report.definition.body_invalid", Assert.Single(raw.Refusals).Code);
        body["version"] = "1.0.0";
        var pack = new ReportDefinitionPackEntry(9, "report-a", "1.0.0", body.ToJsonString());
        var imported = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await new ReportDefinitionCatalogue(store).ImportDraftAsync(pack, 0, "pack"));
        Assert.Equal("report.definition.body_invalid", Assert.Single(imported.Refusals).Code);
        Assert.Empty(await store.ListHistoryAsync(key));
    }

    [Fact, Trait("Holds", "reports-ck-1"), Trait("Holds", "reports-eng-2")]
    public async Task reports_ck_1_direct_store_requires_canonical_bytes_and_preserves_their_digest()
    {
        // Independent literal of the versionless report representation; the shared store hashes these exact UTF-8 bytes.
        const string canonical = """
            {"tenant":"tenant-r","key":"report-a","measures":[],"envelope":{"identity":"report-a","tenant":"tenant-r","contract":{"major":1,"minor":0},"cascade_layer":"domain_package","provenance":{},"retention_class":"business","legal_hold":false,"requires":[]}}
            """;
        var store = Store();
        var key = new DefinitionKey("tenant-r", DefinitionKind.Reports, "report-a");
        var document = new DefinitionDocument(key, "report-a@1.0.0", "1.0.0", canonical);
        var reordered = canonical.Replace("\"tenant\":\"tenant-r\",\"key\":\"report-a\"", "\"key\":\"report-a\",\"tenant\":\"tenant-r\"", StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(canonical);
        var indented = JsonSerializer.Serialize(parsed.RootElement, new JsonSerializerOptions { WriteIndented = true });
        foreach (var body in new[] { reordered, indented })
        {
            var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
                await store.SaveDraftAsync(document with { BodyJson = body }, 0, "noncanonical"));
            Assert.Equal("report.definition.body_invalid", Assert.Single(refused.Refusals).Code);
            Assert.Empty(await store.ListHistoryAsync(key));
        }
        await store.SaveDraftAsync(document, 0, "raw");
        await store.PublishAsync(key, "report-a@1.0.0", 1, "publish");
        var published = await new ReportDefinitionCatalogue(store).ResolveHeadAsync("tenant-r", "report-a");
        var pack = ReportDefinitionCatalogue.Export(published);
        var imported = await new ReportDefinitionCatalogue(Store()).ImportDraftAsync(pack, 0, "import");
        var expectedDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(canonical, published.Revision.Document.BodyJson);
        Assert.Equal(canonical, imported.Revision.Document.BodyJson);
        Assert.Equal(expectedDigest, published.Revision.Digest);
        Assert.Equal(expectedDigest, imported.Revision.Digest);
    }

    [Theory, Trait("Holds", "reports-eng-2")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"tenant\":\"tenant-r\",\"key\":\"report-a\",\"version\":\"1.0.0\",\"measures\":[],\"parameters\":[{\"name\":\"p\",\"type\":\"integer\",\"default\":{\"kind\":\"unknown\",\"value\":7}}]}")]
    [InlineData("{\"tenant\":\"tenant-r\",\"key\":\"report-a\",\"version\":\"1.0.0\",\"measures\":[],\"envelope\":{\"identity\":\"report-a\",\"version\":\"1.0.0\",\"tenant\":\"tenant-r\",\"cascade_layer\":\"domain_package\"}}")]
    public async Task reports_eng_2_pack_codec_failures_are_named_without_committing(string body)
    {
        var store = Store();
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await new ReportDefinitionCatalogue(store).ImportDraftAsync(new(9, "report-a", "1.0.0", body), 0, "pack"));
        Assert.Equal("report.definition.body_invalid", Assert.Single(refusal.Refusals).Code);
        Assert.Equal("/body", Assert.Single(refusal.Refusals).Pointer);
        Assert.Empty(await store.ListHistoryAsync(new("tenant-r", DefinitionKind.Reports, "report-a")));
    }

    [Theory, Trait("Holds", "reports-eng-2")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("17")]
    public async Task reports_eng_2_raw_nonobject_roots_are_named_without_committing(string body)
    {
        var store = Store();
        var key = new DefinitionKey("tenant-r", DefinitionKind.Reports, "report-a");
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(new(key, "report-a@1.0.0", "1.0.0", body), 0, "raw"));
        Assert.Equal("report.definition.body_invalid", Assert.Single(refusal.Refusals).Code);
        Assert.Equal("/body", Assert.Single(refusal.Refusals).Pointer);
        Assert.Empty(await store.ListHistoryAsync(key));
    }

}
