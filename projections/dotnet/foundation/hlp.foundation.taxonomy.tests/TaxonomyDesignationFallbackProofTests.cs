using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyDesignationFallbackProofTests
{
    // DES-0024 ck-19/auth-10 and interface revision 4: only null means unsupplied.
    [Fact(DisplayName = "taxonomy-ck-19 taxonomy-auth-10: designations fall back per field and preserve the same vendor")]
    public void Designations_fall_back_per_field_without_changing_the_same_vendor()
    {
        TaxonomyNode[] nodes = [new("root", "Vendor label", "Vendor description", TaxonomyNodeStatus.Active, [])];
        var node = nodes[0];
        var vendor = Vendor(nodes);
        var interpreter = new TaxonomyInterpreter([vendor]);
        (string? Display, string? Description, string ExpectedDisplay, string ExpectedDescription)[] cases =
        [
            ("Tenant label", null, "Tenant label", "Vendor description"),
            (null, "Tenant description", "Vendor label", "Tenant description"),
            (null, null, "Vendor label", "Vendor description"),
            ("", "", "", ""),
        ];

        foreach (var item in cases)
        {
            var overlay = Overlay([new("root", item.Display, item.Description)]);
            var resolved = interpreter.ResolveOverlayDesignations(overlay, vendor);

            AssertVendorUnchanged(interpreter, vendor, nodes, node);
            var designation = Assert.Single(resolved);
            Assert.Equal(("root", item.ExpectedDisplay, item.ExpectedDescription),
                (designation.VendorNodeCode, designation.Display, designation.Description));
        }
    }

    [Theory(DisplayName = "taxonomy-ck-19 taxonomy-auth-10: absent designation lists return nothing and preserve the vendor")]
    [InlineData(false)]
    [InlineData(true)]
    public void Null_and_empty_designation_lists_preserve_the_vendor(bool empty)
    {
        TaxonomyNode[] nodes = [new("root", "Vendor label", "Vendor description", TaxonomyNodeStatus.Active, [])];
        var node = nodes[0];
        var vendor = Vendor(nodes);
        var interpreter = new TaxonomyInterpreter([vendor]);

        var resolved = interpreter.ResolveOverlayDesignations(Overlay(empty ? [] : null), vendor);

        Assert.Empty(resolved);
        AssertVendorUnchanged(interpreter, vendor, nodes, node);
    }

    private static TaxonomyDefinition Vendor(TaxonomyNode[] nodes) => new(
        "vendor-registry", new("vendor", "health", "scheme"), "1.0.0",
        TaxonomyGovernanceRegime.Authoritative, "vendor-owner", nodes);

    private static TaxonomyDefinition Overlay(IReadOnlyList<TaxonomyOverlayDesignation>? designations) => new(
        "tenant-a", new("tenant", "health", "scheme-overlay"), "1.0.0",
        TaxonomyGovernanceRegime.Civilian, "tenant-author", [],
        Overlay: new(new("vendor", "health", "scheme"), "1.0.0"), OverlayDesignations: designations);

    private static void AssertVendorUnchanged(TaxonomyInterpreter interpreter, TaxonomyDefinition vendor,
        TaxonomyNode[] nodes, TaxonomyNode node)
    {
        var definition = Assert.IsType<ResolvedTaxonomyDefinition>(interpreter.ResolveDefinition(
            new("vendor-registry", new("vendor", "health", "scheme"), "1.0.0"))).Definition;
        Assert.Same(vendor, definition);
        Assert.Same(nodes, definition.Nodes);
        var unchanged = Assert.Single(definition.Nodes);
        Assert.Same(node, unchanged);
        Assert.Equal(("root", TaxonomyNodeStatus.Active, "Vendor label", "Vendor description"),
            (unchanged.Code, unchanged.Status, unchanged.Display, unchanged.Description));
    }
}
