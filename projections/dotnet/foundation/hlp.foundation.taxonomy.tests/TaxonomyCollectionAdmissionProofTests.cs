using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyCollectionAdmissionProofTests
{
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);

    [Theory(DisplayName = "taxonomy-ck-17 taxonomy-ck-18 taxonomy-auth-8: Validate returns only the outside-member refusal, then admits the correction")]
    [InlineData(TaxonomyAdmissionPhase.Author)]
    [InlineData(TaxonomyAdmissionPhase.Publish)]
    [InlineData(TaxonomyAdmissionPhase.Install)]
    public void Validate_proves_closed_world_collection_membership(TaxonomyAdmissionPhase phase)
    {
        var definition = Definition();
        AssertUnknownMember(TaxonomyDefinitionAdmission.Validate(definition, phase, Window));
        Assert.Empty(TaxonomyDefinitionAdmission.Validate(Correct(definition), phase, Window));
    }

    [Theory(DisplayName = "taxonomy-ck-17 taxonomy-ck-18 taxonomy-auth-8: Require throws only the outside-member refusal, then returns the corrected definition")]
    [InlineData(TaxonomyAdmissionPhase.Author)]
    [InlineData(TaxonomyAdmissionPhase.Publish)]
    [InlineData(TaxonomyAdmissionPhase.Install)]
    public void Require_proves_closed_world_collection_membership(TaxonomyAdmissionPhase phase)
    {
        var definition = Definition();
        var exception = Assert.Throws<TaxonomyAdmissionException>(() => TaxonomyDefinitionAdmission.Require(definition, phase, Window));
        AssertUnknownMember(exception.Refusals);
        var corrected = Correct(definition);
        Assert.Same(corrected, TaxonomyDefinitionAdmission.Require(corrected, phase, Window));
    }

    [Theory(DisplayName = "taxonomy-ck-17 taxonomy-ck-18 taxonomy-auth-8: AdmitJson returns only the outside-member refusal, then admits the correction")]
    [InlineData(TaxonomyAdmissionPhase.Author)]
    [InlineData(TaxonomyAdmissionPhase.Publish)]
    [InlineData(TaxonomyAdmissionPhase.Install)]
    public void AdmitJson_proves_closed_world_collection_membership(TaxonomyAdmissionPhase phase)
    {
        var definition = Definition();
        AssertUnknownMember(TaxonomyDefinitionAdmission.AdmitJson(Json(definition), phase, Window));
        Assert.Empty(TaxonomyDefinitionAdmission.AdmitJson(Json(Correct(definition)), phase, Window));
    }

    // Oracle: DES-0024 ck-17/18, auth-8, ruling 4; named-collections evidence and frozen interface revision 4.
    private static void AssertUnknownMember(IReadOnlyList<TaxonomyRefusal> refusals)
    {
        Assert.Collection(refusals, refusal =>
        {
            Assert.Equal("collection.node_unknown", refusal.Code);
            Assert.Equal("/collections/0/node_codes/1", refusal.Pointer);
        });
    }

    private static TaxonomyDefinition Definition() => new(
        "tenant-a", new("acme", "health", "scheme"), "1.0.0", TaxonomyGovernanceRegime.Civilian, "author",
        [new("root", "Root", "Root concept", TaxonomyNodeStatus.Active, []),
         new("leaf", "Leaf", "Leaf concept", TaxonomyNodeStatus.Active, [])],
        Envelope: new("acme.health.scheme", "1.0.0", "tenant-a", TaxonomyCascadeLayer.Tenant,
            JsonSerializer.SerializeToElement(new { source = "author" }), "regulated", false, [], new(1, 0)))
        { Collections = [new("branch", ["root", "missing"])] };

    private static TaxonomyDefinition Correct(TaxonomyDefinition definition) => definition with
        { Collections = [new("branch", ["root", "leaf"])] };

    private static string Json(TaxonomyDefinition definition) => Encoding.UTF8.GetString(TaxonomyDefinitionJson.SerializeCanonical(definition));
}
