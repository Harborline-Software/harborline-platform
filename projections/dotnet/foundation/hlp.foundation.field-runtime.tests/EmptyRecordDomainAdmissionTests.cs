using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class EmptyRecordDomainAdmissionTests
{
    [Theory]
    [InlineData("{\"unknown_operator\":[]}")]
    [InlineData("{\"if\":[true,true,{\"unknown_operator\":[]}]}")]
    public async Task An_empty_source_does_not_hide_an_unsupported_predicate_operator(string predicate)
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
