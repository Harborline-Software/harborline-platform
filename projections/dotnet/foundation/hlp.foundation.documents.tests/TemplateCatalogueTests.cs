using System.Text;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>
/// T-593: templates live in the shared builder-definitions catalogue (DefinitionKind.Templates) with the
/// Documents validator bound as its admission. Versioning follows the Forms pattern (documents-ck-16).
/// </summary>
public sealed class TemplateCatalogueTests
{
    private static readonly DefinitionKey Key = new(Tenant, DefinitionKind.Templates, "template.invoice");

    [Fact(DisplayName = "documents-ck-16: draft to publish, published versions immutable, semantic-version head, append-only history, and a pin never resolves a body stating another version")]
    public async Task VersioningFollowsTheFormsPattern()
    {
        var store = Store();
        await store.SaveDraftAsync(Document(Template("1.9.0")), 0, "d1");
        await store.PublishAsync(Key, "1.9.0", 1, "p1");
        var overwrite = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => store.SaveDraftAsync(Document(Template("1.9.0") with { DocumentType = "quote" }), 2, "edit").AsTask());
        Assert.Contains(overwrite.Refusals, refusal => refusal.Code == "definition.version_immutable");
        await store.SaveDraftAsync(Document(Template("1.10.0")), 2, "d2");
        await store.PublishAsync(Key, "1.10.0", 3, "p2");

        Assert.Equal("1.10.0", (await store.GetPublishedHeadAsync(Key))?.Document.Version);
        Assert.Equal([1L, 2L, 3L, 4L], (await store.ListHistoryAsync(Key)).Select(revision => revision.Revision));
        Assert.Equal("invoice",
            TemplateDefinitionJson.Deserialize(Encoding.UTF8.GetBytes((await store.ResolvePublishedAsync(new(Key, "1.9.0")))!.Document.BodyJson)).DocumentType);

        // Restore copies the 1.10.0 body verbatim under 2.0.0; the body still states 1.10.0, so it cannot publish.
        await store.RestoreAsDraftAsync(Key, "1.10.0", "2.0.0", "2.0.0", 4, "restore");
        var stale = await Assert.ThrowsAsync<DefinitionRefusalException>(() => store.PublishAsync(Key, "2.0.0", 5, "p3").AsTask());
        Assert.Equal([new DefinitionRefusal(TemplateDefinitionCodes.CatalogueMismatch, "/envelope/version")], stale.Refusals);
        Assert.Equal("1.10.0", (await store.GetPublishedHeadAsync(Key))?.Document.Version);
    }

    [Fact(DisplayName = "documents-auth-26: the same key, version and body replays; the same key and version with a different body refuses once by name and keeps the stored body")]
    public async Task PinnedTupleWithADifferentBodyRefusesOnce()
    {
        var store = Store();
        var entry = TemplatePack.Export(Template(), Surfaces());

        Assert.Equal(TemplateInstallOutcomeKind.Published, Assert.Single(await TemplatePack.InstallAsync([entry], Target(store))).Kind);
        var replay = Assert.Single(await TemplatePack.InstallAsync([entry], Target(store)));
        Assert.Equal(TemplateInstallOutcomeKind.AlreadyPresent, replay.Kind);
        Assert.Empty(replay.Refusals);

        var changed = TemplatePack.Export(Template() with { DocumentType = "quote" }, Surfaces());
        var conflict = Assert.Single(await TemplatePack.InstallAsync([changed], Target(store)));

        Assert.Equal(TemplateInstallOutcomeKind.Refused, conflict.Kind);
        Assert.Equal([new TemplateRefusal(TemplateDefinitionCodes.PinnedTupleConflict, "/envelope/version")], conflict.Refusals);
        var stored = (await store.ResolvePublishedAsync(new(Key, "1.0.0")))!;
        Assert.Equal("invoice", TemplateDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(stored.Document.BodyJson)).DocumentType);
        Assert.Equal(2, (await store.ListHistoryAsync(Key)).Count);
    }

    private static InMemoryVersionedDefinitionStore Store() => new(new Dictionary<DefinitionKind, DefinitionAdmission>
    {
        [DefinitionKind.Templates] = (document, phase) => TemplateDefinitionAdmission
            .AdmitCatalogueBody(document.Key.Tenant, document.Key.DefinitionId, document.Version, document.BodyJson,
                phase == DefinitionAdmissionPhase.Publish, Surfaces())
            .Select(refusal => new DefinitionRefusal(refusal.Code, refusal.Pointer)).ToArray(),
    });

    private static DefinitionDocument Document(TemplateDefinition template) => new(
        Key with { DefinitionId = template.Envelope.Identity }, template.Envelope.Version, template.Envelope.Version,
        Encoding.UTF8.GetString(TemplateDefinitionJson.SerializeCanonical(template)));

    private static TemplateInstallTarget Target(InMemoryVersionedDefinitionStore store) => new(
        Tenant, Provenance, Surfaces(),
        async template => (await store.ResolvePublishedAsync(new(Key with { DefinitionId = template.Envelope.Identity }, template.Envelope.Version)))
            is { } revision ? TemplateDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(revision.Document.BodyJson)) : null,
        async template =>
        {
            var key = Key with { DefinitionId = template.Envelope.Identity };
            var revision = (await store.ListHistoryAsync(key)).LastOrDefault()?.Revision ?? 0;
            await store.SaveDraftAsync(Document(template), revision, $"install-draft-{template.Envelope.Version}");
            await store.PublishAsync(key, template.Envelope.Version, revision + 1, $"install-publish-{template.Envelope.Version}");
        });
}
