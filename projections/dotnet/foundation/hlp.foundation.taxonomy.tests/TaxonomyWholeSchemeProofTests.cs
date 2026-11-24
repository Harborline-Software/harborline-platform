using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

[Trait("Item", "taxonomy-eng-11")]
[Trait("Item", "taxonomy-run-5")]
[Trait("Arm", "whole-scheme")]
public sealed class TaxonomyWholeSchemeProofTests
{
    // Oracle: frozen interface revision 4 and DES-0024 eng-11/run-5, whole-scheme arm.
    [Fact]
    public void Whole_scheme_preserves_supplied_version_every_node_and_declaration_order()
    {
        var v1 = Definition("1.0.0", [
            new("z-root", "Root", "Root", TaxonomyNodeStatus.Active, []),
            new("retired", "Retired", "Retired", TaxonomyNodeStatus.Tombstoned, [],
                ParentCode: "z-root", SuccessorCode: "a-leaf", DeprecationReason: "replaced"),
            new("a-leaf", "Leaf", "Leaf", TaxonomyNodeStatus.Active, [], ParentCode: "z-root")
        ]) with { Collections = [new("branch", ["a-leaf"])] };
        var v2 = Definition("2.0.0", [new("new-only", "New", "New", TaxonomyNodeStatus.Active, [])]);
        var interpreter = new TaxonomyInterpreter([v1, v2]);

        Assert.Collection(interpreter.ExpandWholeScheme(v1),
            code => Assert.Equal("z-root", code),
            code => Assert.Equal("retired", code),
            code => Assert.Equal("a-leaf", code));
        Assert.Collection(interpreter.ExpandWholeScheme(v2), code => Assert.Equal("new-only", code));
    }

    [Fact]
    public void Whole_scheme_expands_an_empty_raw_authoring_definition_to_no_codes()
    {
        var definition = Definition("1.0.0", []);
        Assert.Equal<string>([], new TaxonomyInterpreter([]).ExpandWholeScheme(definition));
    }

    // Oracle: interface revision 4 malformed-graph identity; only the duplicate code is corrected.
    [Fact]
    public void Whole_scheme_refuses_duplicate_codes_and_accepts_the_corrected_positive_control()
    {
        var definition = Definition("1.0.0", [
            new("z-root", "Root", "Root", TaxonomyNodeStatus.Active, []),
            new("z-root", "Leaf", "Leaf", TaxonomyNodeStatus.Active, [])
        ]);
        var interpreter = new TaxonomyInterpreter([]);
        var refused = Assert.Throws<TaxonomyTraversalException>(() => interpreter.ExpandWholeScheme(definition));
        Assert.Equal("taxonomy.definition_malformed", refused.Code);

        var corrected = definition with { Nodes = [definition.Nodes[0], definition.Nodes[1] with { Code = "a-leaf" }] };
        Assert.Collection(interpreter.ExpandWholeScheme(corrected),
            code => Assert.Equal("z-root", code),
            code => Assert.Equal("a-leaf", code));
    }

    private static TaxonomyDefinition Definition(string version, IReadOnlyList<TaxonomyNode> nodes) =>
        new("tenant-a", new("acme", "health", "scheme"), version, TaxonomyGovernanceRegime.Civilian, "author", nodes);
}
