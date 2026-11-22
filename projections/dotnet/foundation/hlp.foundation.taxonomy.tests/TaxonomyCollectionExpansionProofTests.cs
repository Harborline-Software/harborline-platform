using System.Text.Json;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyCollectionExpansionProofTests
{
    // Oracles: collection-expansion-proof acceptance 1–3 and frozen interface revision 4.
    // Expected vectors are restated literals, independent of the fixture and producer output.
    [Fact(DisplayName = "taxonomy-eng-11 taxonomy-run-5: named-Collection preserves supplied version and declared order")]
    public void Named_collection_preserves_supplied_version_and_declared_order()
    {
        var v1 = Definition("1.0.0");
        var v2 = Definition("2.0.0");
        var window = new DefinitionContractWindow(1, 0, 1);
        Assert.Empty(TaxonomyDefinitionAdmission.Validate(v1, TaxonomyAdmissionPhase.Publish, window));
        Assert.Empty(TaxonomyDefinitionAdmission.Validate(v2, TaxonomyAdmissionPhase.Publish, window, previous: v1));

        var firstRegistration = new TaxonomyInterpreter([v1]);
        AssertV1(firstRegistration.ExpandCollection(v1, "focus"));
        var bothRegistrations = new TaxonomyInterpreter([v1, v2]);
        AssertV1(bothRegistrations.ExpandCollection(v1, "focus"));
        var second = Assert.IsType<ResolvedTaxonomyCollectionExpansion>(bothRegistrations.ExpandCollection(v2, "focus"));
        Assert.Equal("new-only", Assert.Single(second.Codes));
        AssertV1(bothRegistrations.ExpandCollection(v1, "focus"));
    }

    [Theory(DisplayName = "taxonomy-eng-11 taxonomy-run-5: named-Collection missing names remain ordinal and untrimmed")]
    [InlineData("unknown")]
    [InlineData("Focus")]
    [InlineData(" focus ")]
    public void Named_collection_miss_preserves_exact_request_and_corrected_name_succeeds(string requestedName)
    {
        var v1 = Definition("1.0.0");
        var interpreter = new TaxonomyInterpreter([v1]);
        var miss = Assert.IsType<TaxonomyCollectionExpansionNotFound>(interpreter.ExpandCollection(v1, requestedName));
        Assert.Equal(requestedName, miss.CollectionName);
        AssertV1(interpreter.ExpandCollection(v1, "focus"));
    }

    [Fact(DisplayName = "taxonomy-eng-11 taxonomy-run-5: named-Collection absent list returns typed miss")]
    public void Named_collection_absent_list_returns_typed_miss()
    {
        var v1 = Definition("1.0.0");
        var interpreter = new TaxonomyInterpreter([v1]);
        var absent = v1 with { Collections = null };
        var miss = Assert.IsType<TaxonomyCollectionExpansionNotFound>(interpreter.ExpandCollection(absent, "focus"));
        Assert.Equal("focus", miss.CollectionName);
        AssertV1(interpreter.ExpandCollection(v1, "focus"));
    }

    [Fact(DisplayName = "taxonomy-eng-11 taxonomy-run-5: named-Collection malformed null members refuse then recover")]
    public void Named_collection_null_members_refuse_then_restored_members_succeed()
    {
        var v1 = Definition("1.0.0");
        var interpreter = new TaxonomyInterpreter([v1]);
        // Raw corruption only: a null list is not a publishable fixture.
        var malformed = v1 with { Collections = [new("other", ["unused"]), new("focus", null!)] };
        var refusal = Assert.Throws<TaxonomyTraversalException>(() => interpreter.ExpandCollection(malformed, "focus"));
        Assert.Equal("taxonomy.definition_malformed", refusal.Code);
        var restored = malformed with { Collections = [malformed.Collections![0], new("focus", ["z-leaf", "a-root"])] };
        AssertV1(interpreter.ExpandCollection(restored, "focus"));
    }

    private static void AssertV1(TaxonomyCollectionExpansionResult result)
    {
        var expansion = Assert.IsType<ResolvedTaxonomyCollectionExpansion>(result);
        Assert.Equal(2, expansion.Codes.Count);
        Assert.Equal("z-leaf", expansion.Codes[0]);
        Assert.Equal("a-root", expansion.Codes[1]);
    }

    private static TaxonomyDefinition Definition(string version)
    {
        var id = new TaxonomyDefinitionId("acme", "health", "scheme");
        TaxonomyNode[] retained = [Node("a-root"), Node("unused"), Node("z-leaf", "a-root")];
        return new TaxonomyDefinition("tenant-a", id, version, TaxonomyGovernanceRegime.Civilian, "author",
            version == "1.0.0" ? retained : [.. retained, Node("unused-v2"), Node("new-only")],
            Envelope: new("acme.health.scheme", version, "tenant-a", TaxonomyCascadeLayer.Tenant,
                JsonElement.Parse("{}"), "regulated", false, [], new(1, 0)))
        {
            Collections = version == "1.0.0"
                ? [new("other", ["unused"]), new("focus", ["z-leaf", "a-root"])]
                : [new("other", ["unused-v2"]), new("focus", ["new-only"])]
        };
    }

    private static TaxonomyNode Node(string code, string? parent = null) =>
        new(code, code, code, TaxonomyNodeStatus.Active, [], ParentCode: parent);
}
