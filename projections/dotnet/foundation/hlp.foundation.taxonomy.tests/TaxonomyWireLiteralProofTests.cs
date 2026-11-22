using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyWireLiteralProofTests
{
    // T-493/wire-literal-proof at Control 2d1da43057c6c2204025155f11bda360570194cc, acceptance 1.
    private const string Wire = """{"collections":[{"name":"branch","node_codes":["a","z"]}],"definition_id":"acme.health.scheme","envelope":{"cascade_layer":"tenant","contract":{"major":1,"minor":0},"identity":"acme.health.scheme","legal_hold":true,"provenance":{"a":1,"z":2},"requires":[],"retention_class":"regulated","tenant":"tenant-a","version":"1.2.3"},"governance":"civilian","nodes":[{"code":"z","description":"Z description","display":"Z label","display_history_entries":[],"status":"active"},{"code":"a","description":"A description","display":"A label","display_history_entries":[],"status":"active"}],"owner":"author","schema_version":1,"tenant":"tenant-a","version":"1.2.3"}""" + "\n";

    [Fact(DisplayName = "taxonomy-ck-2, taxonomy-ck-9, taxonomy-ck-18, taxonomy-ck-21: public constructors serialize to independent literal bytes")]
    public void Constructors_serialize_to_literal_bytes()
    {
        using var provenance = JsonDocument.Parse("""{"z":2,"a":1}""");
        var definition = new TaxonomyDefinition(
            "tenant-a", new("acme", "health", "scheme"), "1.2.3",
            TaxonomyGovernanceRegime.Civilian, "author",
            [new("z", "Z label", "Z description", TaxonomyNodeStatus.Active, []),
             new("a", "A label", "A description", TaxonomyNodeStatus.Active, [])],
            Envelope: new("acme.health.scheme", "1.2.3", "tenant-a", TaxonomyCascadeLayer.Tenant,
                provenance.RootElement.Clone(), "regulated", true, [], new(1, 0)))
        {
            Collections = [new("branch", ["a", "z"])]
        };

        Assert.Equal(Encoding.UTF8.GetBytes(Wire), TaxonomyDefinitionJson.SerializeCanonical(definition));
    }

    [Fact(DisplayName = "taxonomy-ck-1, taxonomy-ck-2, taxonomy-ck-9, taxonomy-ck-18, taxonomy-ck-21: literal bytes deserialize, publish and export independently")]
    public void Literal_bytes_deserialize_publish_and_export()
    {
        var definition = TaxonomyDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(Wire));

        Assert.Equal("tenant-a", definition.Tenant);
        Assert.Equal("acme", definition.DefinitionId.Vendor);
        Assert.Equal("health", definition.DefinitionId.Domain);
        Assert.Equal("scheme", definition.DefinitionId.TaxonomyName);
        Assert.Equal("1.2.3", definition.Version);
        Assert.Equal("author", definition.Owner);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal(TaxonomyGovernanceRegime.Civilian, definition.Governance);
        Assert.Equal(
            [("z", "Z label", "Z description", TaxonomyNodeStatus.Active),
             ("a", "A label", "A description", TaxonomyNodeStatus.Active)],
            definition.Nodes.Select(node => (node.Code, node.Display, node.Description, node.Status)));
        Assert.All(definition.Nodes, node =>
        {
            Assert.Empty(node.DisplayHistoryEntries);
            Assert.Null(node.ParentCode);
        });
        var collection = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<TaxonomyCollection>>(definition.Collections));
        Assert.Equal("branch", collection.Name);
        Assert.Equal(["a", "z"], collection.NodeCodes);
        var envelope = Assert.IsType<TaxonomyDefinitionEnvelope>(definition.Envelope);
        Assert.Equal("acme.health.scheme", envelope.Identity);
        Assert.Equal("1.2.3", envelope.Version);
        Assert.Equal("tenant-a", envelope.Tenant);
        Assert.Equal(TaxonomyCascadeLayer.Tenant, envelope.CascadeLayer);
        Assert.Equal(1, envelope.Provenance.GetProperty("a").GetInt32());
        Assert.Equal(2, envelope.Provenance.GetProperty("z").GetInt32());
        Assert.Equal("regulated", envelope.RetentionClass);
        Assert.True(envelope.LegalHold);
        Assert.Empty(envelope.Requires);
        var contract = Assert.IsType<DefinitionContractVersion>(envelope.Contract);
        Assert.Equal(1, contract.Major);
        Assert.Equal(0, contract.Minor);

        var window = new DefinitionContractWindow(1, 0, 1);
        Assert.Empty(TaxonomyDefinitionAdmission.Validate(definition, TaxonomyAdmissionPhase.Publish, window));
        var exported = TaxonomyDefinitionPackExporter.Export(definition, window);
        Assert.Equal(8, exported.ContentKind);
        Assert.Equal(Encoding.UTF8.GetBytes(Wire), exported.Content.ToArray());
    }
}
