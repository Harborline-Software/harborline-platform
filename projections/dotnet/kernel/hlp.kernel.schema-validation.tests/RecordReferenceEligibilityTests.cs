using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordReferenceEligibilityTests
{
    // ADR-0054 and DES-0015 records-ck-10/11; identity comparisons are exact, no trait version is implied.
    [Theory]
    [InlineData("type", "pump", "equipment", "locatable", true)]
    [InlineData("type", "Pump", "equipment", "locatable", false)]
    [InlineData("class", "valve", "equipment", "locatable", true)]
    [InlineData("class", "valve", "people", "locatable", false)]
    [InlineData("class", "valve", null, "locatable", false)]
    [InlineData("class", "valve", "equipment", "Locatable", false)]
    [InlineData("class", "valve", "equipment", null, false)]
    public void records_eng_16_records_auth_34_records_ck_11_target_eligibility(
        string target, string type, string? home, string? trait, bool expected)
    {
        var reference = new RecordReferenceDefinition(target == "type" ? "pump" : null,
            target == "class" ? "equipment" : null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, "locatable");
        TraitReference[]? traits = trait is null ? null : [new(trait, "1.0.0", [])];
        Assert.Equal(expected, RecordReferenceEligibility.Admits(reference, type, home, traits));
    }

    [Fact]
    public void records_ck_10_no_required_trait_admits_null_traits_but_never_an_ambiguous_target()
    {
        var reference = new RecordReferenceDefinition("pump", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block);
        Assert.True(RecordReferenceEligibility.Admits(reference, "pump", null, null));
        Assert.False(RecordReferenceEligibility.Admits(reference with { TargetClassId = "equipment" }, "pump", "equipment", null));
        Assert.False(RecordReferenceEligibility.Admits(reference with { TargetTypeId = null }, "pump", null, null));
    }
}
