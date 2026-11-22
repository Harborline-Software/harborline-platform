using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyClassificationResolutionProofTests
{
    [Theory(DisplayName = "taxonomy-eng-2 / taxonomy-run-1: exact pins retain classification identity and labels")]
    [Trait("Holds", "taxonomy-eng-2")]
    [Trait("Holds", "taxonomy-run-1")]
    [InlineData("tenant-a", "1.0.0", "Z-v1", "A-v1", "Retired-v1")]
    [InlineData("tenant-a", "2.0.0", "Z-v2", "A-v2", "Retired-v2")]
    [InlineData("tenant-b", "1.0.0", "Z-tenant-b", "A-tenant-b", "Retired-tenant-b")]
    public void Exact_pins_preserve_identity_and_labels(string tenant, string version, string z, string a, string retired)
    {
        var interpreter = Fixture();
        var definition = Select(interpreter, tenant, version);
        AssertClassification(interpreter.ResolveClassification(definition, Reference("z", tenant, version)), tenant, version, "z", z, true);
        AssertClassification(interpreter.ResolveClassification(definition, Reference("a", tenant, version)), tenant, version, "a", a, true);
        AssertClassification(interpreter.ResolveClassification(definition, Reference("retired", tenant, version)), tenant, version, "retired", retired, false);
    }

    [Theory(DisplayName = "taxonomy-eng-2 / taxonomy-run-1: missing, null and empty codes return null")]
    [Trait("Holds", "taxonomy-eng-2")]
    [Trait("Holds", "taxonomy-run-1")]
    [InlineData("missing")]
    [InlineData(null)]
    [InlineData("")]
    public void Unknown_codes_return_null(string? code)
    {
        var interpreter = Fixture();
        Assert.Null(interpreter.ResolveClassification(Select(interpreter), Reference(code!)));
    }

    [Theory(DisplayName = "taxonomy-eng-2 / taxonomy-run-1: each mismatched reference coordinate refuses")]
    [Trait("Holds", "taxonomy-eng-2")]
    [Trait("Holds", "taxonomy-run-1")]
    [InlineData("tenant-b", "acme", "1.0.0")]
    [InlineData("tenant-a", "another", "1.0.0")]
    [InlineData("tenant-a", "acme", "2.0.0")]
    public void Mismatched_coordinates_name_reference(string tenant, string vendor, string version)
    {
        var interpreter = Fixture();
        var definition = Select(interpreter);
        var reference = new TaxonomyClassificationReference(tenant, new(vendor, "health", "scheme"), version, "z");
        Assert.Equal("reference", Assert.Throws<ArgumentException>(() => interpreter.ResolveClassification(definition, reference)).ParamName);
        AssertClassification(interpreter.ResolveClassification(definition, Reference("z")), "tenant-a", "1.0.0", "z", "Z-v1", true);
    }

    [Fact(DisplayName = "taxonomy-eng-3 / taxonomy-run-1: ordered batch retains misses, duplicates and tombstones")]
    [Trait("Holds", "taxonomy-eng-3")]
    [Trait("Holds", "taxonomy-run-1")]
    public void Batch_preserves_every_position()
    {
        var interpreter = Fixture();
        var results = interpreter.ResolveClassifications(Select(interpreter),
            [Reference("z"), Reference("missing"), null!, Reference("a"), Reference("z"), Reference("retired")]);
        Assert.Equal(6, results.Count);
        Assert.Collection(results,
            value => AssertClassification(value, "tenant-a", "1.0.0", "z", "Z-v1", true),
            value => Assert.Null(value),
            value => Assert.Null(value),
            value => AssertClassification(value, "tenant-a", "1.0.0", "a", "A-v1", true),
            value => AssertClassification(value, "tenant-a", "1.0.0", "z", "Z-v1", true),
            value => AssertClassification(value, "tenant-a", "1.0.0", "retired", "Retired-v1", false));
    }

    [Fact(DisplayName = "taxonomy-eng-4: active, tombstoned and missing controls retain identity")]
    [Trait("Holds", "taxonomy-eng-4")]
    public void Activity_does_not_follow_succession()
    {
        var interpreter = Fixture();
        var definition = Select(interpreter);
        AssertClassification(interpreter.ResolveClassification(definition, Reference("z")), "tenant-a", "1.0.0", "z", "Z-v1", true);
        var retired = interpreter.ResolveClassification(definition, Reference("retired"));
        AssertClassification(retired, "tenant-a", "1.0.0", "retired", "Retired-v1", false);
        Assert.Equal("retired", retired!.Node.DeprecationReason);
        Assert.Equal("a", retired.Node.SuccessorCode);
        var missing = interpreter.ResolveClassification(definition, Reference("missing"));
        Assert.Null(missing);
        Assert.False(missing?.IsActive ?? false);
    }

    // DES-0024 eng-2/3/4, run-1 and interface revision 4 supply the literal expectations above.
    private static void AssertClassification(ResolvedTaxonomyClassification? value, string tenant, string version, string code, string display, bool active)
    {
        var result = Assert.IsType<ResolvedTaxonomyClassification>(value);
        Assert.Equal(tenant, result.Reference.Tenant);
        Assert.Equal("acme.health.scheme", result.Reference.DefinitionId.ToString());
        Assert.Equal(version, result.Reference.Version);
        Assert.Equal(code, result.Reference.Code);
        Assert.Equal(code, result.Node.Code);
        Assert.Equal(display, result.Node.Display);
        Assert.Equal(active ? TaxonomyNodeStatus.Active : TaxonomyNodeStatus.Tombstoned, result.Node.Status);
        Assert.Equal(active, result.IsActive);
    }

    private static TaxonomyClassificationReference Reference(string code, string tenant = "tenant-a", string version = "1.0.0") =>
        new(tenant, new("acme", "health", "scheme"), version, code);

    private static TaxonomyDefinition Select(TaxonomyInterpreter interpreter, string tenant = "tenant-a", string version = "1.0.0") =>
        Assert.IsType<ResolvedTaxonomyDefinition>(interpreter.ResolveDefinition(new(tenant, new("acme", "health", "scheme"), version))).Definition;

    private static TaxonomyInterpreter Fixture() => new([
        Definition("tenant-a", "1.0.0", "Z-v1", "A-v1", "Retired-v1"),
        Definition("tenant-a", "2.0.0", "Z-v2", "A-v2", "Retired-v2"),
        Definition("tenant-b", "1.0.0", "Z-tenant-b", "A-tenant-b", "Retired-tenant-b")]);

    private static TaxonomyDefinition Definition(string tenant, string version, string z, string a, string retired) =>
        new(tenant, new("acme", "health", "scheme"), version, TaxonomyGovernanceRegime.Civilian, "author", [
            new("z", z, "Z description", TaxonomyNodeStatus.Active, []),
            new("a", a, "A description", TaxonomyNodeStatus.Active, []),
            new("retired", retired, "Retired description", TaxonomyNodeStatus.Tombstoned, [], SuccessorCode: "a", DeprecationReason: "retired")]);
}
