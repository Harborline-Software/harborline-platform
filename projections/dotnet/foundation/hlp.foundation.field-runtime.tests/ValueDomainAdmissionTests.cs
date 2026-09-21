using Harborline.Contracts.Fields;
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

    [Fact]
    public void An_inline_enum_cannot_supply_domain_membership()
    {
        var refusals = ValueDomainAdmission.ValidateJson(
            """{"enum":["open","closed"]}""", "/fields/status~1code/value_domain");

        Assert.Contains(refusals, refusal =>
            refusal.Code == "field.inline_membership_forbidden"
            && refusal.JsonPointer == "/fields/status~1code/value_domain/enum");
    }

    [Fact]
    public void No_source_is_refused_and_each_of_the_three_declared_sources_is_admitted()
    {
        Assert.Equal("field.value_domain_source_count",
            Assert.Single(ValueDomainAdmission.Validate(new(), "/domain")).Code);
        ValueDomainDefinition[] valid =
        [
            new(LiteralValues: ["open", "closed"]),
            new(TaxonomyScheme: new("status", "1.0.0")),
            new(RecordQuery: new("customer", "active-customers")),
        ];
        Assert.All(valid, domain => Assert.Empty(ValueDomainAdmission.Validate(domain, "/domain")));
    }

    [Theory]
    [InlineData("{\"literal_values\":[\"open\"],\"regex\":\"open\"}", "field.inline_membership_forbidden", "/domain/regex")]
    [InlineData("{\"literal_values\":[\"open\"],\"pattern\":\"open\"}", "field.pattern_membership_forbidden", "/domain/pattern")]
    [InlineData("{\"literal_values\":[\"open\"],\"literal_values\":[\"closed\"]}", "field.value_domain_member_duplicate", "/domain/literal_values")]
    [InlineData("{\"literal_values\":[\"open\"],\"a/b~\":true}", "field.value_domain_member_unknown", "/domain/a~1b~0")]
    [InlineData("{\"literal_values\":[null]}", "field.value_domain_shape_invalid", "/domain/literal_values/0")]
    [InlineData("{\"taxonomy_scheme\":{\"scheme_id\":\"status\",\"version\":\"\"}}", "field.value_domain_reference_required", "/domain/taxonomy_scheme/version")]
    [InlineData("{\"record_query\":{\"record_type_id\":\"customer\"}}", "field.value_domain_reference_required", "/domain/record_query/predicate")]
    public void Raw_admission_refuses_unsupported_members_and_bad_references_at_their_locations(
        string json, string code, string pointer)
    {
        var refusal = Assert.Single(ValueDomainAdmission.ValidateJson(json, "/domain"));
        Assert.Equal(code, refusal.Code);
        Assert.Equal(pointer, refusal.JsonPointer);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"open\"")]
    [InlineData("{broken")]
    public void Malformed_domain_objects_return_a_refusal(string json)
    {
        var refusal = Assert.Single(ValueDomainAdmission.ValidateJson(json, "/domain"));
        Assert.Equal("field.value_domain_shape_invalid", refusal.Code);
        Assert.Equal("/domain", refusal.JsonPointer);
    }
}
