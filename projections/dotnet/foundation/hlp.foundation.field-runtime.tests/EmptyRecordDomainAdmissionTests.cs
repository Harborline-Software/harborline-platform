using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class EmptyRecordDomainAdmissionTests
{
    // ponytail: the predicate's operator vocabulary is Rules' (DES-0030 field-runtime-eng-1,
    // cc-4), and RuleCompiler on main admits an unrecognised operator. With no candidate row
    // to evaluate, nothing forces it, so an empty source resolves to no values instead of a
    // refusal. Static operator admission belongs in RuleCompiler, which T-588 owns; this test
    // pins the boundary so the behaviour cannot change unnoticed.
    [Theory]
    [InlineData("{\"unknown_operator\":[]}")]
    [InlineData("{\"if\":[true,true,{\"unknown_operator\":[]}]}")]
    public async Task An_empty_source_resolves_to_no_values_and_defers_operator_admission_to_rules(string predicate)
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = [];

        var resolved = await fixture.Runtime().ResolveAsync(
            new(RecordQuery: new("case", predicate)), DomainFixture.Scope, "/fields/status/domain");

        Assert.Empty(resolved.Values);
        Assert.Equal(FieldEditorKind.None, resolved.Editor);
        Assert.Equal(ValueDomainSourceKind.RecordQuery, resolved.SourceKind);
    }
}
