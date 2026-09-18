using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class ValueDomainAdmissionTests
{
    [Fact]
    public void A_domain_with_two_sources_is_refused_at_the_authored_location()
    {
        var domain = new ValueDomainDefinition(["open"], new("status", "1.0.0"));

        var refusals = ValueDomainAdmission.Validate(domain, "/fields/0/value_domain");

        var refusal = Assert.Single(refusals);
        Assert.Equal("field.value_domain_source_count", refusal.Code);
        Assert.Equal("/fields/0/value_domain", refusal.JsonPointer);
    }
}
