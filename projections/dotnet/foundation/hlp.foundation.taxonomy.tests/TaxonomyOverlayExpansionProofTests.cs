using System.Text.Json;
using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

// Oracles: T-493/overlay-expansion-proof acceptance 1–4 at Control a25fb174c,
// DES-0024 eng-12/run-5 and ruling 2, and frozen Taxonomy interface revision 4.
public sealed class TaxonomyOverlayExpansionProofTests
{
    [Fact(DisplayName = "taxonomy-eng-12 taxonomy-run-5 overlay uses each explicitly requested vendor release in vendor-first order")]
    public void taxonomy_eng_12_taxonomy_run_5_overlay_requested_release_and_order()
    {
        var overlay = Overlay();
        var interpreter = Interpreter(overlay);

        AssertV1(interpreter.ExpandOverlay(Coordinates(), "1.0.0"));
        Assert.Equal(["z-root", "a-leaf", "new-only", "z-addon", "a-addon"],
            Assert.IsType<ResolvedTaxonomyOverlayExpansion>(interpreter.ExpandOverlay(Coordinates(), "1.1.0")).Codes);
        Assert.Equal("1.0.0", overlay.Overlay!.VendorVersion);
        AssertV1(interpreter.ExpandOverlay(Coordinates(), "1.0.0"));
    }

    [Fact(DisplayName = "taxonomy-eng-12 taxonomy-run-5 overlay missing vendor has literal empty-tenant coordinates then recovers")]
    public void taxonomy_eng_12_taxonomy_run_5_overlay_missing_vendor()
    {
        var interpreter = Interpreter(Overlay());
        var refusal = Assert.IsType<TaxonomyOverlayExpansionNotFound>(
            interpreter.ExpandOverlay(Coordinates(), "9.9.9"));

        Assert.Equal(new TaxonomyDefinitionCoordinates("", new("vendor", "health", "scheme"), "9.9.9"), refusal.Coordinates);
        AssertV1(interpreter.ExpandOverlay(Coordinates(), "1.0.0"));
    }

    [Fact(DisplayName = "taxonomy-eng-12 taxonomy-run-5 overlay missing overlay has literal tenant coordinates then recovers")]
    public void taxonomy_eng_12_taxonomy_run_5_overlay_missing_overlay()
    {
        var interpreter = Interpreter(Overlay());
        var refusal = Assert.IsType<TaxonomyOverlayExpansionNotFound>(
            interpreter.ExpandOverlay(new("tenant-a", new("tenant", "health", "scheme-overlay"), "9.9.9"), "1.0.0"));

        Assert.Equal(new TaxonomyDefinitionCoordinates("tenant-a", new("tenant", "health", "scheme-overlay"), "9.9.9"), refusal.Coordinates);
        AssertV1(interpreter.ExpandOverlay(Coordinates(), "1.0.0"));
    }

    [Fact(DisplayName = "taxonomy-eng-12 taxonomy-run-5 overlay refuses both vendor registrations without preferring a tenant")]
    public void taxonomy_eng_12_taxonomy_run_5_overlay_ambiguous_vendor()
    {
        var vendorV1 = Vendor("1.0.0");
        var vendorV11 = Vendor("1.1.0");
        var overlay = Overlay();
        var other = vendorV1 with { Tenant = "vendor-other", Envelope = vendorV1.Envelope! with { Tenant = "vendor-other" } };
        var interpreter = new TaxonomyInterpreter([vendorV1, vendorV11, overlay, other]);
        var refusal = Assert.IsType<TaxonomyOverlayExpansionAmbiguousVendor>(interpreter.ExpandOverlay(Coordinates(), "1.0.0"));

        Assert.Equal(new TaxonomyDefinitionId("vendor", "health", "scheme"), refusal.VendorDefinitionId);
        Assert.Equal("1.0.0", refusal.VendorVersion);
        Assert.Equal(["vendor-other", "vendor-registry"], refusal.Tenants.Order(StringComparer.Ordinal).ToArray());
        AssertV1(new TaxonomyInterpreter([vendorV1, vendorV11, overlay]).ExpandOverlay(Coordinates(), "1.0.0"));
    }

    [Fact(DisplayName = "taxonomy-eng-12 taxonomy-run-5 taxonomy-auth-22 taxonomy-ck-19 overlay refuses copied vendor code then recovers")]
    public void taxonomy_eng_12_taxonomy_run_5_overlay_copied_vendor_code()
    {
        var overlay = Overlay();
        var copied = overlay with { Nodes = [overlay.Nodes[0] with { Code = "z-root" }, overlay.Nodes[1]] };
        var refusal = Assert.IsType<TaxonomyOverlayExpansionCopiesVendorNode>(
            Interpreter(copied).ExpandOverlay(Coordinates(), "1.0.0"));

        Assert.Equal("z-root", refusal.Code);
        var corrected = copied with { Nodes = [copied.Nodes[0] with { Code = "z-addon" }, copied.Nodes[1]] };
        AssertV1(Interpreter(corrected).ExpandOverlay(Coordinates(), "1.0.0"));
    }

    [Fact(DisplayName = "taxonomy-eng-12 taxonomy-run-5 taxonomy-auth-10 overlay refuses unknown designation then recovers")]
    public void taxonomy_eng_12_taxonomy_run_5_overlay_unknown_designation()
    {
        var overlay = Overlay() with { OverlayDesignations = [new("missing-vendor-code", "Tenant label", null)] };
        var refusal = Assert.IsType<TaxonomyOverlayExpansionUnknownDesignation>(
            Interpreter(overlay).ExpandOverlay(Coordinates(), "1.0.0"));

        Assert.Equal("missing-vendor-code", refusal.VendorNodeCode);
        var corrected = overlay with { OverlayDesignations = [overlay.OverlayDesignations[0] with { VendorNodeCode = "z-root" }] };
        AssertV1(Interpreter(corrected).ExpandOverlay(Coordinates(), "1.0.0"));
    }

    private static void AssertV1(TaxonomyOverlayExpansionResult result) =>
        Assert.Equal(["z-root", "a-leaf", "z-addon", "a-addon"], Assert.IsType<ResolvedTaxonomyOverlayExpansion>(result).Codes);

    private static TaxonomyDefinitionCoordinates Coordinates() => new("tenant-a", new("tenant", "health", "scheme-overlay"), "1.0.0");
    private static TaxonomyInterpreter Interpreter(TaxonomyDefinition overlay) => new([Vendor("1.0.0"), Vendor("1.1.0"), overlay]);

    private static TaxonomyDefinition Vendor(string version) => Definition("vendor-registry", new("vendor", "health", "scheme"), version,
        version == "1.0.0" ? [Node("z-root"), Node("a-leaf", "z-root")] : [Node("z-root"), Node("a-leaf", "z-root"), Node("new-only", "z-root")]);

    private static TaxonomyDefinition Overlay() => Definition("tenant-a", new("tenant", "health", "scheme-overlay"), "1.0.0",
        [Node("z-addon", "z-root"), Node("a-addon", "z-root")]) with { Overlay = new(new("vendor", "health", "scheme"), "1.0.0") };

    private static TaxonomyDefinition Definition(string tenant, TaxonomyDefinitionId id, string version, IReadOnlyList<TaxonomyNode> nodes) =>
        new(tenant, id, version, TaxonomyGovernanceRegime.Civilian, "author", nodes,
            Envelope: new(id.ToString(), version, tenant, TaxonomyCascadeLayer.Tenant, JsonElement.Parse("{}"), "regulated", false, [], new(1, 0)));

    private static TaxonomyNode Node(string code, string? parent = null) => new(code, "Display", "Description", TaxonomyNodeStatus.Active, [], ParentCode: parent);
}
