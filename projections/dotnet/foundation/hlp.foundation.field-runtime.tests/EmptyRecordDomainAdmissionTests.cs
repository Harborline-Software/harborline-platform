using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class EmptyRecordDomainAdmissionTests
{
    // Rules owns static operator admission (DES-0030 field-runtime-eng-1 and cc-4).
    // T-588 rejects unknown operators throughout the expression, including unreachable
    // branches. An empty candidate source cannot bypass that compiler admission.
    [Theory]
    [InlineData("{\"unknown_operator\":[]}")]
    [InlineData("{\"if\":[true,true,{\"unknown_operator\":[]}]}")]
    public async Task An_empty_source_still_refuses_operators_rejected_by_rules(string predicate)
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = [];

        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await fixture.Runtime().ResolveAsync(
            new(RecordQuery: new("case", predicate)), DomainFixture.Scope, "/fields/status/domain"));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.value_domain_predicate_invalid", refusal.Code);
        Assert.Equal("/fields/status/domain", refusal.JsonPointer);
    }
}
