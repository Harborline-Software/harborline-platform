using System.Text;
using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 owned-item test names (control #1415 review finding 7): the DES-0015 items whose behaviour is on main, each
/// asserted by a test that carries its id. Expected wire names, bodies, codes and pointers are literals.
/// </summary>
public sealed class RecordTypeOwnedItemTests
{
    [Fact]
    [Trait("Holds", "records-ck-13")]
    public async Task records_ck_13_a_fields_governance_members_round_trip_under_their_wire_names_through_the_canonical_body()
    {
        var host = Host();
        var request = AssetClass("1.0.0") with
        {
            Fields = [new("owner", "Owner", Governance: new FieldGovernanceDefinition(true, true, false, "restricted"))],
        };

        var created = await host.Records.CreateDraftAsync(request, "create");

        // Oracle: DES-0015 records-ck-13 names the four members; the values are this test's own.
        var body = created.Revision.Document.BodyJson;
        using (var document = JsonDocument.Parse(body))
        {
            var governance = document.RootElement.GetProperty("fields")[0].GetProperty("governance");
            Assert.Equal(["classification", "confidential", "masked", "personal_data"],
                governance.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
            Assert.True(governance.GetProperty("personal_data").GetBoolean());
            Assert.True(governance.GetProperty("confidential").GetBoolean());
            Assert.False(governance.GetProperty("masked").GetBoolean());
            Assert.Equal("restricted", governance.GetProperty("classification").GetString());
        }

        var read = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(body));
        Assert.Equal(new FieldGovernanceDefinition(true, true, false, "restricted"), Assert.Single(read.Fields).Governance);
        Assert.Equal(body, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(read)));
    }

    [Fact]
    [Trait("Holds", "records-ck-29")]
    public async Task records_ck_29_a_published_type_round_trips_cannot_change_keeps_append_only_history_and_restores_as_a_new_draft()
    {
        var host = Host();
        var key = RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class");
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        // Canonical JSON and the definition envelope round-trip without loss (CreatedAssetClassJson is a literal).
        var head = Assert.IsType<RecordTypeDocument>(await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"));
        Assert.Equal(new RecordsDefinitionEnvelope(Tenant, "eam", new DefinitionContractVersion(1, 0), "eam-core"), head.Envelope);
        var bytes = RecordTypeDefinitionJson.SerializeCanonical(head);
        Assert.Equal(CreatedAssetClassJson, Encoding.UTF8.GetString(bytes));
        Assert.Equal(CreatedAssetClassJson,
            Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(RecordTypeDefinitionJson.Deserialize(bytes))));
        Assert.Equal(CreatedAssetClassJson,
            Encoding.UTF8.GetString(RecordTypeDefinitionPackageExporter.Export(published.Revision).Content.Payload.Span));

        // A published version cannot change, and the refused attempt appends nothing.
        var before = await host.Catalogue.ListHistoryAsync(key);
        var edited = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(CreatedAssetClassJson)) with { Name = "Edited" };
        var overwrite = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", edited, "1.0.0", 2, "overwrite").AsTask());
        Assert.Equal([("definition.version_immutable", "/versionId")],
            overwrite.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
        Assert.Equal(before, await host.Catalogue.ListHistoryAsync(key));

        // Restore-as-draft appends a new draft version and leaves every earlier revision and the head as they were.
        var restored = await host.Records.RestoreAsDraftAsync(Tenant, "eam.asset-class", "1.0.0", "1.1.0", 2, "restore");
        Assert.Equal(("1.1.0", DefinitionStatus.Draft, "1.0.0", CreatedAssetClassJson),
            (restored.Document.Version, restored.Status, restored.RestoredFromVersionId, restored.Document.BodyJson));
        var after = await host.Catalogue.ListHistoryAsync(key);
        Assert.Equal(before.Append(restored), after);
        Assert.Equal("1.0.0", (await host.Catalogue.GetPublishedHeadAsync(key))!.Document.Version);
    }
}
