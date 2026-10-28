using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Kernel.SchemaValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

public sealed class LayoutFormPublicationImpactTests
{
    [Theory]
    [InlineData(FormPublicationCompatibility.Block, """{"type":"object","properties":{"name":{"type":"string"},"tax_id":{"type":"string"}},"required":["tax_id"]}""")]
    [InlineData(FormPublicationCompatibility.AuthorResolution, """{"type":"object","properties":{"name":{"type":"number"}}}""")]
    public async Task Advisory_findings_commit_the_Form_and_never_change_real_Layout_history(
        FormPublicationCompatibility expected, string schemaText)
    {
        var fixture = await FixtureAsync();
        var body = LiteralLayout("surface.customer", "1.0.0");
        await PublishLiteralSurface(fixture.Shared, "surface.customer", "surface-old-a", "1.0.0", 0, body);
        var schema = await fixture.Schemas.RegisterAsync(schemaText);
        var candidate = fixture.Candidate with { SchemaRef = new(schema.Id.Value) };
        var services = new ServiceCollection();
        services.AddSingleton<IVersionedDefinitionStore>(new ReadFaultStore(fixture.Shared));
        services.AddSingleton<IFormDefinitionStore>(fixture.Forms);
        services.AddSingleton<ISchemaRegistry>(fixture.Schemas);
        services.AddHarborlineFormsAuthoringPublisher();
        services.AddHarborlineLayoutFormPublicationSource();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>()
            .RegisterAndPublishWithImpactAsync(candidate, scope.ServiceProvider.FormPublicationContext(fixture.Basis));

        Assert.Equal(FormDefinitionStatus.Published, result.Definition.Status);
        Assert.Equal(expected, result.Assessment.Compatibility);
        Assert.Null(result.Assessment.Proposal);
        Assert.Equal(schemaText, result.Assessment.Inputs!.CandidateSchema.JsonSchemaText);
        var row = Assert.Single(result.Assessment.Surfaces);
        Assert.Equal("surface-old-a", row.VersionId);
        Assert.Equal(new[] { "form-a", "form-b" }, row.BlockIds);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))), row.Digest);
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "surface.customer");
        var head = (await fixture.Shared.GetPublishedHeadAsync(key))!;
        Assert.Equal("surface-old-a", head.Document.VersionId);
        Assert.Equal(body, head.Document.BodyJson);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))), head.Digest);
        Assert.Equal(new long[] { 1, 2 }, (await fixture.Shared.ListHistoryAsync(key)).Select(value => value.Revision));
        var decoded = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(head.Document.BodyJson));
        Assert.All(decoded.Blocks[0].Children, block => Assert.Equal("f-old-x9", block.Form!.FormVersionId));
        Assert.Equal("2.2.0", (await fixture.Forms.GetCurrentPublishedAsync(new("tenant-a"), new("form.customer")))!.Version.ToString());
    }

    [Fact]
    public async Task Every_historical_input_remains_retained_or_resolvable_after_later_publications_and_census_growth()
    {
        var fixture = await FixtureAsync();
        var expectedBodies = new Dictionary<string, string>
        {
            ["surface-old-a"] = LiteralLayout("surface.customer", "1.0.0"),
            ["surface-old-b"] = LiteralLayout("surface.customer", "2.0.0"),
        };
        await PublishLiteralSurface(fixture.Shared, "surface.customer", "surface-old-a", "1.0.0", 0, expectedBodies["surface-old-a"]);
        await PublishLiteralSurface(fixture.Shared, "surface.customer", "surface-old-b", "2.0.0", 2, expectedBodies["surface-old-b"]);
        var services = new ServiceCollection();
        services.AddSingleton<IVersionedDefinitionStore>(new ReadFaultStore(fixture.Shared));
        services.AddSingleton<IFormDefinitionStore>(fixture.Forms);
        services.AddSingleton<ISchemaRegistry>(fixture.Schemas);
        services.AddHarborlineFormsAuthoringPublisher();
        services.AddHarborlineLayoutFormPublicationSource();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>()
            .RegisterAndPublishWithImpactAsync(fixture.Candidate, scope.ServiceProvider.FormPublicationContext(fixture.Basis));
        Assert.True(result.Assessment.IsComplete);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Assessment.Compatibility);

        await fixture.Forms.RegisterAndPublishAsync(fixture.Candidate with { Version = new(2, 3, 0) });
        var formKey = new DefinitionKey("tenant-a", DefinitionKind.Forms, "form.customer");
        await fixture.Shared.SaveDraftAsync(new(formKey, "f-later-z7", "2.3.0", "{\"later\":true}"), 2, "later-form-draft");
        await fixture.Shared.PublishAsync(formKey, "f-later-z7", 3, "later-form-publish");
        await PublishLiteralSurface(fixture.Shared, "surface.customer", "surface-later-c", "3.0.0", 4, LiteralLayout("surface.customer", "3.0.0"));
        await PublishLiteralSurface(fixture.Shared, "surface.added", "surface-added-d", "1.0.0", 0, LiteralLayout("surface.added", "1.0.0"));

        var assessment = result.Assessment;
        Assert.Equal(new[] { "surface-old-a", "surface-old-b" }, assessment.Surfaces.Select(row => row.VersionId));
        Assert.Same(assessment.Surfaces, assessment.Proposal!.Surfaces);
        Assert.Equal("f-old-x9", assessment.Basis!.Predecessor!.VersionId);
        Assert.Equal("f-new-y8", assessment.Basis.Candidate.VersionId);
        var candidate = JsonNode.Parse(assessment.Inputs!.CandidateFormJson)!;
        var predecessor = JsonNode.Parse(assessment.Inputs.PredecessorFormJson!)!;
        Assert.Equal("form.customer", candidate["Id"]!.GetValue<string>());
        Assert.Equal("2.2.0", candidate["Version"]!.GetValue<string>());
        Assert.Equal(0, candidate["Status"]!.GetValue<int>());
        Assert.Equal("Customer details", candidate["Overlay"]!["Title"]!["Values"]!["en"]!.GetValue<string>());
        Assert.Equal("2.1.0", predecessor["Version"]!.GetValue<string>());
        Assert.Equal(1, predecessor["Status"]!.GetValue<int>());
        Assert.Equal("Name", predecessor["Overlay"]!["Fields"]!["name"]!["Label"]!["Values"]!["en"]!.GetValue<string>());
        Assert.Equal("""{"type":"object","properties":{"name":{"type":"string"}}}""", assessment.Inputs.CandidateSchema.JsonSchemaText);
        Assert.Equal("""{"type":"object","properties":{"name":{"type":"string"}}}""", assessment.Inputs.PredecessorSchema!.JsonSchemaText);
        Assert.Equal("Historical inputs observed before publication; the committed Form transition and current Layout membership are not guaranteed. Reassess before acting.", assessment.FreshnessLimit);
        foreach (var row in assessment.Surfaces)
        {
            Assert.Equal("tenant-a", row.Tenant);
            Assert.Equal("surface.customer", row.DefinitionId);
            var expectedBody = expectedBodies[row.VersionId];
            var resolved = (await fixture.Shared.ResolvePublishedAsync(new(new(row.Tenant, DefinitionKind.Layout, row.DefinitionId), row.VersionId)))!;
            Assert.Equal(expectedBody, resolved.Document.BodyJson);
            var expectedDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedBody)));
            Assert.Equal(expectedDigest, row.Digest);
            Assert.Equal(expectedDigest, resolved.Digest);
            var decoded = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(resolved.Document.BodyJson));
            Assert.Equal(new[] { "form-a", "form-b" }, row.BlockIds);
            Assert.Equal(new[] { "form-a", "form-b" }, decoded.Blocks[0].Children.Select(block => block.Id));
            foreach (var block in decoded.Blocks[0].Children)
            {
                Assert.Equal("form.customer", block.Form!.FormDefinitionId);
                Assert.Equal("f-old-x9", block.Form.FormVersionId);
                var form = (await LayoutFormReferenceResolver.ResolveAsync(fixture.Shared, "tenant-a", block.Form))!;
                Assert.Equal("f-old-x9", form.Document.VersionId);
                Assert.Equal("2.1.0", form.Document.Version);
                Assert.Equal("{}", form.Document.BodyJson);
            }
        }
        var later = Assert.IsType<FormPublishedSurfaceRead.Complete>(await scope.ServiceProvider
            .GetRequiredService<IFormPublishedSurfaceSource>().ReadAsync(fixture.Basis));
        Assert.Equal(new[] { "surface-added-d", "surface-later-c", "surface-old-a", "surface-old-b" }, later.Surfaces.Select(row => row.VersionId));
        Assert.Equal("surface-later-c", (await fixture.Shared.GetPublishedHeadAsync(new("tenant-a", DefinitionKind.Layout, "surface.customer")))!.Document.VersionId);
        Assert.Equal("f-later-z7", (await fixture.Shared.GetPublishedHeadAsync(formKey))!.Document.VersionId);
        Assert.Equal("2.3.0", (await fixture.Forms.GetCurrentPublishedAsync(new("tenant-a"), new("form.customer")))!.Version.ToString());
    }

    private static string LiteralLayout(string id, string version) => $$$"""
        {"envelope":{"identity":"{{{id}}}","version":"{{{version}}}","tenant":"tenant-a","cascade_layer":"tenant_configuration","provenance":{"source":"t1089-literal"},"retention_class":"standard","legal_hold":false,"requires":[],"contract":{"major":1,"minor":0}},"schema_version":1,"medium":"screen","default_intent":"capture",
        "blocks":[{"id":"container","kind":"layout.list","binding":{"binding_kind":"record_field","field_path":"customer.name"},"children":[
          {"id":"form-a","kind":"layout.form","binding":{"binding_kind":"record_field","field_path":"customer.name"},"children":[],"form":{"form_definition_id":"form.customer","form_version_id":"f-old-x9"}},
          {"id":"form-b","kind":"layout.form","binding":{"binding_kind":"record_field","field_path":"customer.name"},"children":[],"form":{"form_definition_id":"form.customer","form_version_id":"f-old-x9"}}]}],
        "page_layouts":[],"page_masters":[],"page_runs":[],"drill_through_targets":[]}
        """;

    private static async Task PublishLiteralSurface(IVersionedDefinitionStore store, string id,
        string versionId, string version, long expected, string body)
    {
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, id);
        await store.SaveDraftAsync(new(key, versionId, version, body), expected, "draft-" + versionId);
        await store.PublishAsync(key, versionId, expected + 1, "publish-" + versionId);
    }

    [Fact]
    public async Task Full_published_history_and_nested_exact_pins_are_observed_not_just_the_head()
    {
        var fixture = await FixtureAsync();
        await PublishSurface(fixture.Shared, "tenant-a", "surface.customer", "surface-old-a", "1.0.0", 0,
            "f-old-x9", nested: true);
        await PublishSurface(fixture.Shared, "tenant-a", "surface.customer", "surface-new-b", "2.0.0", 2, "f-new-y8");
        await DraftSurface(fixture.Shared, "tenant-a", "surface.customer", "surface-draft-c", "3.0.0", 4, "f-old-x9");
        await PublishSurface(fixture.Shared, "tenant-b", "surface.customer", "foreign-x", "1.0.0", 0, "f-old-x9");
        await PublishSurface(fixture.Shared, "tenant-a", "surface.unrelated", "unrelated-x", "1.0.0", 0, "2.1.0");

        var read = Assert.IsType<FormPublishedSurfaceRead.Complete>(await new LayoutFormPublicationImpactSource(fixture.Shared).ReadAsync(fixture.Basis));
        Assert.Equal("tenant-a", read.Tenant);
        var row = Assert.Single(read.Surfaces);
        Assert.Equal("surface.customer", row.DefinitionId);
        Assert.Equal("surface-old-a", row.VersionId);
        Assert.Equal("1.0.0", row.Version);
        Assert.Equal(new[] { "form-a", "form-b" }, row.BlockIds);
        Assert.Equal("surface-new-b", (await fixture.Shared.GetPublishedHeadAsync(new("tenant-a", DefinitionKind.Layout, "surface.customer")))!.Document.VersionId);
    }

    [Fact]
    public async Task Another_form_id_sharing_the_opaque_version_id_is_not_an_impacted_pin()
    {
        var fixture = await FixtureAsync();
        await PublishSurface(fixture.Shared, "tenant-a", "surface.other", "other-surface-a", "1.0.0", 0,
            "f-old-x9", formId: "form.other");
        var unrelated = Assert.IsType<FormPublishedSurfaceRead.Complete>(
            await new LayoutFormPublicationImpactSource(fixture.Shared).ReadAsync(fixture.Basis));
        Assert.Empty(unrelated.Surfaces);

        await PublishSurface(fixture.Shared, "tenant-a", "surface.customer", "surface-old-a", "1.0.0", 0,
            "f-old-x9", formId: "form.customer");
        var matching = Assert.IsType<FormPublishedSurfaceRead.Complete>(
            await new LayoutFormPublicationImpactSource(fixture.Shared).ReadAsync(fixture.Basis));
        var row = Assert.Single(matching.Surfaces);
        Assert.Equal("surface.customer", row.DefinitionId);
        Assert.Equal("surface-old-a", row.VersionId);
        Assert.Equal(new[] { "form-a" }, row.BlockIds);
    }

    [Fact]
    public async Task Real_registered_publisher_composes_real_Layout_source_and_retains_both_existing_pins()
    {
        var fixture = await FixtureAsync();
        await PublishSurface(fixture.Shared, "tenant-a", "surface.customer", "surface-old-a", "1.0.0", 0, "f-old-x9");
        var surfaceBinding = new DefinitionBinding(new("tenant-a", DefinitionKind.Layout, "surface.customer"), "surface-old-a");
        var before = (await fixture.Shared.ResolvePublishedAsync(surfaceBinding))!;
        var beforeBody = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(before.Document.BodyJson));
        var oldForm = await LayoutFormReferenceResolver.ResolveAsync(fixture.Shared, "tenant-a", beforeBody.Blocks[0].Form!);
        var historyBefore = await fixture.Shared.ListHistoryAsync(surfaceBinding.Key);

        var services = new ServiceCollection();
        services.AddSingleton<IVersionedDefinitionStore>(fixture.Shared);
        services.AddSingleton<IFormDefinitionStore>(fixture.Forms);
        services.AddSingleton<ISchemaRegistry>(fixture.Schemas);
        services.AddHarborlineFormsAuthoringPublisher();
        services.AddHarborlineLayoutFormPublicationSource();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var result = await publisher.RegisterAndPublishWithImpactAsync(fixture.Candidate,
            scope.ServiceProvider.FormPublicationContext(fixture.Basis));

        Assert.Equal(FormDefinitionStatus.Published, result.Definition.Status);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Assessment.Compatibility);
        var proposal = Assert.IsType<FormRepublishProposal>(result.Assessment.Proposal);
        Assert.Equal("f-old-x9", proposal.Predecessor.VersionId);
        Assert.Equal("f-new-y8", proposal.Candidate.VersionId);
        Assert.Equal("surface-old-a", Assert.Single(proposal.Surfaces).VersionId);
        var after = (await fixture.Shared.ResolvePublishedAsync(surfaceBinding))!;
        var afterBody = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(after.Document.BodyJson));
        var retainedForm = await LayoutFormReferenceResolver.ResolveAsync(fixture.Shared, "tenant-a", afterBody.Blocks[0].Form!);
        Assert.Equal("surface-old-a", after.Document.VersionId);
        Assert.Equal("f-old-x9", retainedForm!.Document.VersionId);
        Assert.Equal("f-old-x9", oldForm!.Document.VersionId);
        Assert.Equal(before.Document.BodyJson, after.Document.BodyJson);
        Assert.Equal(before.Digest, after.Digest);
        var historyAfter = await fixture.Shared.ListHistoryAsync(surfaceBinding.Key);
        Assert.Equal(historyBefore.Count, historyAfter.Count);
        Assert.Equal(historyBefore.Select(row => row.Revision), historyAfter.Select(row => row.Revision));
        Assert.Equal("2.2.0", (await fixture.Forms.GetCurrentPublishedAsync(new("tenant-a"), new("form.customer")))!.Version.ToString());
    }

    [Fact]
    public async Task Complete_empty_is_distinct_from_an_unreadable_or_partial_census()
    {
        var fixture = await FixtureAsync();
        var empty = Assert.IsType<FormPublishedSurfaceRead.Complete>(await new LayoutFormPublicationImpactSource(fixture.Shared).ReadAsync(fixture.Basis));
        Assert.Empty(empty.Surfaces);
        await PublishSurface(fixture.Shared, "tenant-a", "surface.customer", "surface-old-a", "1.0.0", 0, "f-old-x9");
        var failed = new ReadFaultStore(fixture.Shared) { History = _ => throw new IOException("private-history") };
        Assert.IsType<FormPublishedSurfaceRead.Unavailable>(await new LayoutFormPublicationImpactSource(failed).ReadAsync(fixture.Basis));
    }

    [Theory]
    [InlineData("foreign-key")]
    [InlineData("null-history")]
    [InlineData("bad-digest")]
    [InlineData("foreign-document")]
    [InlineData("malformed-body")]
    [InlineData("duplicate-key")]
    [InlineData("noncaller-cancellation")]
    public async Task Poisoned_or_incomplete_store_answers_never_become_a_complete_census(string fault)
    {
        var fixture = await FixtureAsync();
        await PublishSurface(fixture.Shared, "tenant-a", "surface.customer", "surface-old-a", "1.0.0", 0, "f-old-x9");
        var key = new DefinitionKey("tenant-a", DefinitionKind.Layout, "surface.customer");
        var revision = (await fixture.Shared.GetPublishedHeadAsync(key))!;
        var store = new ReadFaultStore(fixture.Shared);
        switch (fault)
        {
            case "foreign-key": store.Keys = [key with { Tenant = "tenant-b" }]; break;
            case "duplicate-key": store.Keys = [key, key]; break;
            case "null-history": store.History = _ => null!; break;
            case "bad-digest": store.History = _ => [revision with { Digest = "wrong" }]; break;
            case "foreign-document": store.History = _ => [revision with { Document = revision.Document with { Key = key with { Tenant = "tenant-b" } } }]; break;
            case "malformed-body":
                // The actual store honestly hashes this body. Typed decoding, not digest mismatch, refuses it.
                var badKey = key with { DefinitionId = "surface.bad" };
                await fixture.Shared.SaveDraftAsync(new(badKey, "bad-a", "1.0.0", "{}"), 0, "bad-draft");
                await fixture.Shared.PublishAsync(badKey, "bad-a", 1, "bad-publish");
                break;
            default: store.History = _ => throw new OperationCanceledException(); break;
        }
        var result = await new LayoutFormPublicationImpactSource(store).ReadAsync(fixture.Basis);
        Assert.IsType<FormPublishedSurfaceRead.Unavailable>(result);
    }

    [Fact]
    public async Task Exact_Form_binding_metadata_is_checked_and_semantic_label_cannot_substitute_for_opaque_id()
    {
        var fixture = await FixtureAsync();
        var mismatched = fixture.Basis with { Predecessor = fixture.Basis.Predecessor! with { VersionId = "2.1.0" } };
        Assert.IsType<FormPublishedSurfaceRead.Unavailable>(await new LayoutFormPublicationImpactSource(fixture.Shared).ReadAsync(mismatched));
        var wrongLabel = fixture.Basis with { Predecessor = fixture.Basis.Predecessor! with { Version = new(2, 0, 0) } };
        Assert.IsType<FormPublishedSurfaceRead.Unavailable>(await new LayoutFormPublicationImpactSource(fixture.Shared).ReadAsync(wrongLabel));
    }

    [Fact]
    public async Task Caller_cancellation_propagates_while_noncaller_cancellation_is_unavailable()
    {
        var fixture = await FixtureAsync();
        using var cancellation = new CancellationTokenSource();
        var store = new ReadFaultStore(fixture.Shared) { BeforeKeys = token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LayoutFormPublicationImpactSource(store)
            .ReadAsync(fixture.Basis, cancellation.Token).AsTask());
    }

    private static async Task<(InMemoryVersionedDefinitionStore Shared, InMemoryFormDefinitionStore Forms,
        InMemorySchemaRegistry Schemas, FormDefinition Candidate, FormPublicationBasis Basis)> FixtureAsync()
    {
        var shared = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            // These tests prove registry history/census and exact pins, not member admission or host capability.
            [DefinitionKind.Layout] = (_, _) => [], [DefinitionKind.Forms] = (_, _) => [],
        });
        var formKey = new DefinitionKey("tenant-a", DefinitionKind.Forms, "form.customer");
        await shared.SaveDraftAsync(new(formKey, "f-old-x9", "2.1.0", "{}"), 0, "form-old-draft");
        await shared.PublishAsync(formKey, "f-old-x9", 1, "form-old-publish");
        var schemas = new InMemorySchemaRegistry();
        var schema = await schemas.RegisterAsync("""{"type":"object","properties":{"name":{"type":"string"}}}""");
        var forms = new InMemoryFormDefinitionStore();
        var old = new FormDefinition(new("form.customer"), new(2, 1, 0), FormDefinitionStatus.Draft,
            new("tenant-a"), IdentityRef.System, new Harborline.Foundation.Assets.Common.SchemaId(schema.Id.Value),
            new(new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("Name")) }, [], []),
            null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, SubmitGate: new(Role: RoleReference.Domain("author")));
        await forms.RegisterAndPublishAsync(old);
        var candidate = old with { Version = new(2, 2, 0), Overlay = old.Overlay with { Title = InternationalizedText.FromInvariant("Customer details") } };
        var basis = new FormPublicationBasis(new(candidate.Tenant, candidate.Id, candidate.Version, "f-new-y8", "writer/candidate-y8"),
            new(old.Tenant, old.Id, old.Version, "f-old-x9", "writer/predecessor-x9"), false,
            new(Environment(), "environment/revision-a"), new(Environment(), "environment/revision-a"));
        return (shared, forms, schemas, candidate, basis);
    }

    private static BorrowerEnvironmentDeclaration Environment() => new("forms-ck-13", "harborline-jsonlogic/v1",
        new Dictionary<string, string> { ["candidate"] = "form-submission-candidate" }, ["=="], ["field-read"],
        "missing-field-reads-null", "evaluated-at", "utc", new Dictionary<EvaluationPhase, bool>
        {
            [EvaluationPhase.AuthoringValidation] = false, [EvaluationPhase.PublishValidation] = false,
            [EvaluationPhase.Render] = true, [EvaluationPhase.Submission] = true,
            [EvaluationPhase.Run] = false, [EvaluationPhase.SignOff] = false,
        }, "deterministic-over-pinned-clock-and-candidate");

    private static async Task PublishSurface(IVersionedDefinitionStore store, string tenant, string id,
        string versionId, string version, long expected, string pin, bool nested = false, string formId = "form.customer")
    {
        await DraftSurface(store, tenant, id, versionId, version, expected, pin, nested, formId);
        await store.PublishAsync(new(tenant, DefinitionKind.Layout, id), versionId, expected + 1, "publish-" + versionId);
    }

    private static async Task DraftSurface(IVersionedDefinitionStore store, string tenant, string id,
        string versionId, string version, long expected, string pin, bool nested = false, string formId = "form.customer")
    {
        var form = new LayoutBlock("form-a", "layout.form", new LayoutRecordFieldBinding("customer.name"), [],
            Form: new(formId, pin));
        IReadOnlyList<LayoutBlock> blocks = nested
            ? [new("container", "layout.list", new LayoutRecordFieldBinding("customer.name"),
                [form, form with { Id = "form-b" }])] : [form];
        var definition = new LayoutDefinition(new(id, version, tenant, LayoutCascadeLayer.TenantConfiguration,
            JsonSerializer.SerializeToElement(new { source = "t688-literal-fixture" }), "standard", false,
            [new("platform.layout", "1.0.0")], new(1, 0)), 1, LayoutMedium.Screen, LayoutIntent.Capture,
            blocks, [], [], [], null, []);
        await store.SaveDraftAsync(new(new(tenant, DefinitionKind.Layout, id), versionId, version,
            Encoding.UTF8.GetString(LayoutDefinitionJson.SerializeCanonical(definition))), expected, "draft-" + versionId);
    }

    private sealed class ReadFaultStore(IVersionedDefinitionStore inner) : IVersionedDefinitionStore
    {
        public IReadOnlyList<DefinitionKey>? Keys { get; set; }
        public Func<DefinitionKey, IReadOnlyList<DefinitionRevision>>? History { get; set; }
        public Action<CancellationToken>? BeforeKeys { get; set; }
        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default)
        { BeforeKeys?.Invoke(cancellationToken); return Keys is null ? inner.ListKeysAsync(tenant, kind, cancellationToken) : ValueTask.FromResult(Keys); }
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default) =>
            History is null ? inner.ListHistoryAsync(key, cancellationToken) : ValueTask.FromResult(History(key));
        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default) => inner.ResolvePublishedAsync(binding, cancellationToken);
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default) => inner.GetPublishedHeadAsync(key, cancellationToken);
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("source must not write");
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("source must not publish");
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("source must not restore");
    }
}
