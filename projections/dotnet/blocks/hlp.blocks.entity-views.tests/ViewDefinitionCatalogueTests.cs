using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

/// <summary>
/// DES-0017 <c>views-eng-16</c> (ADR 0071; the owner's ruling of 2026-11-07): Views admission runs inside the shared
/// definition store, so a caller that writes through <see cref="IVersionedDefinitionStore"/> directly, skipping Views
/// authoring, is refused the same way. Expected codes and pointers are DES-0017 and store-contract literals.
/// </summary>
public sealed class ViewDefinitionCatalogueTests
{
    private const string Tenant = "tenant-a";

    [Fact(DisplayName = "views-eng-16: a draft saved straight through the shared store is refused by Views admission, and nothing is stored")]
    public async Task ADraftSavedStraightThroughTheSharedStoreIsAdmittedByViews()
    {
        var (_, shared) = ViewStores.Create();
        var key = ViewDefinitionCatalogue.KeyOf(Tenant, "work.queue");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await shared.SaveDraftAsync(Document(Draft(kind: "views.not-registered")), 0, "bypass"));

        Assert.Contains(refused.Refusals, refusal => refusal == new DefinitionRefusal("view_definition.kind_unknown", "/binding/kind"));
        Assert.Empty(await shared.ListHistoryAsync(key));
    }

    [Fact(DisplayName = "views-eng-16: a body that is not the draft wire, or that names another definition, is refused at the store")]
    public async Task ABodyThatIsNotTheWireOrNamesAnotherDefinitionIsRefused()
    {
        var (_, shared) = ViewStores.Create();
        var key = ViewDefinitionCatalogue.KeyOf(Tenant, "work.queue");

        var malformed = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await shared.SaveDraftAsync(new DefinitionDocument(key, "1.0.0", "1.0.0", """{"definition":{}}"""), 0, "malformed"));
        var elsewhere = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await shared.SaveDraftAsync(Document(Draft(), ViewDefinitionCatalogue.KeyOf(Tenant, "other.view")), 0, "elsewhere"));

        Assert.Contains(new DefinitionRefusal("view_definition.body_invalid", ""), malformed.Refusals);
        Assert.Contains(new DefinitionRefusal("definition.catalogue_mismatch", "/definition/envelope"), elsewhere.Refusals);
    }

    [Fact(DisplayName = "views-eng-16: publishing a view whose record type has no published version is refused at the commit, and no head appears")]
    public async Task PublishingAViewOverAnUnpublishedRecordTypeIsRefused()
    {
        var (catalogue, shared) = ViewStores.Create();
        await catalogue.CreateDraftAsync(Draft(recordType: "not-published").Definition, Draft().Binding);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await shared.PublishAsync(ViewDefinitionCatalogue.KeyOf(Tenant, "work.queue"), "1.0.0", 1, "publish"));

        Assert.Equal([new DefinitionRefusal("view_definition.record_type_unresolved", "/definition/recordType")], refused.Refusals);
        Assert.Null(await catalogue.ResolvePublishedHeadAsync(Tenant, "work.queue"));
    }

    [Fact(DisplayName = "views-eng-16: publishing a view that names a field its record type does not declare is refused at that field's pointer")]
    public async Task PublishingAViewOverAnUndeclaredFieldIsRefused()
    {
        var catalogue = ViewStores.Catalogue();
        var draft = Draft(column: "not-a-field");
        await catalogue.CreateDraftAsync(draft.Definition, draft.Binding);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await catalogue.PublishAsync(Tenant, "work.queue", "1.0.0"));

        Assert.Equal([new DefinitionRefusal("view_definition.field_unknown", "/definition/parameters/columns/0/field")], refused.Refusals);
        Assert.Equal(ViewDefinitionStatus.Draft, Assert.Single(await catalogue.ListHistoryAsync(Tenant, "work.queue")).Status);
    }

    [Fact(DisplayName = "views-eng-16: a published view records its dependency on the record type it reads")]
    public async Task APublishedViewRecordsItsRecordTypeDependency()
    {
        var (catalogue, shared) = ViewStores.Create();
        var draft = Draft();
        await catalogue.CreateDraftAsync(draft.Definition, draft.Binding);

        await catalogue.PublishAsync(Tenant, "work.queue", "1.0.0");

        var dependencies = await shared.ReadDependenciesAsync(Tenant, "platform.definition-type.view", "work.queue");
        var edge = Assert.Single(dependencies.DependsOn);
        Assert.Equal(new CatalogueDefinitionReference("platform.definition-type.record-type", "work-item"), edge.ToDefinitionId);
        Assert.Equal(("record_type", DefinitionDependencyKind.Derived), (edge.EdgeReason, edge.Kind));
        Assert.Equal("1.0.0", (await catalogue.ResolvePublishedHeadAsync(Tenant, "work.queue"))!.Version);
    }

    private static DefinitionDocument Document(ViewDefinitionDraft draft, DefinitionKey? key = null) =>
        new(key ?? ViewDefinitionCatalogue.KeyOf(Tenant, draft.Definition.Key), draft.Definition.Version, draft.Definition.Version,
            ViewDefinitionDraftJson.Serialize(draft));

    private static ViewDefinitionDraft Draft(string kind = "layout.table", string recordType = "work-item", string column = "title") => new(
        new ViewDefinition(
            new("work.queue", "1.0.0", Tenant, ViewCascadeLayer.Tenant, JsonSerializer.SerializeToElement(new { source = "test" }), [], new(1, 0)),
            SchemaVersion: 1,
            Title: "Work queue",
            RecordType: recordType,
            Ownership: ViewOwnershipTier.Public,
            OpenPermission: "work:read",
            Parameters: new([new(column, 240)], [new("title", ViewSortDirection.Ascending)], null, null, null)),
        new ViewBinding(kind, new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }));
}
