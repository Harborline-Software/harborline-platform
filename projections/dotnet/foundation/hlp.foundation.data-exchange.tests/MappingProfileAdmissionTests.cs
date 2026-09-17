using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class MappingProfileAdmissionTests
{
    [Fact]
    public void Accepted_profile_preserves_csvw_terms_and_canonical_target()
    {
        var mapping = new TabularMappingDocument(
            TabularMappingProfile.Family,
            TabularMappingProfile.SchemaUri,
            "1.0.0",
            new CanonicalTarget("records.customer/v1", "/customers"),
            [new MappingColumn("CustomerNumber", "string", true, "/customerNumber")],
            new Dictionary<string, string> { ["hl:operation"] = "upsert" });

        var accepted = TabularMappingAdmission.Validate(mapping);

        Assert.Same(mapping, accepted);
        Assert.Equal("string", accepted.Columns[0].Datatype);
        Assert.Equal("records.customer/v1", accepted.Target.Contract);
    }

    [Theory]
    [InlineData("csvw-ish/v1", "https://schemas.harborline.software/mapping/tabular/v1", "1.0.0", "mapping.profile_unknown")]
    [InlineData("hl:tabular-mapping/v1", "https://schemas.harborline.software/mapping/tabular/v2", "1.0.0", "mapping.schema_mismatch")]
    [InlineData("hl:tabular-mapping/v1", "https://schemas.harborline.software/mapping/tabular/v1", "2.0.0", "mapping.major_unsupported")]
    public void Unknown_or_incompatible_profile_is_refused(
        string family,
        string schemaUri,
        string version,
        string expectedCode)
    {
        var mapping = new TabularMappingDocument(
            family,
            schemaUri,
            version,
            new CanonicalTarget("records.customer/v1", "/customers"),
            [new MappingColumn("CustomerNumber", "string", true, "/customerNumber")],
            new Dictionary<string, string>());

        var exception = Assert.Throws<DataExchangeAdmissionException>(
            () => TabularMappingAdmission.Validate(mapping));

        Assert.Contains(exception.Refusals, refusal => refusal.Code == expectedCode);
    }

    [Fact]
    public void Importer_private_target_and_unknown_execution_term_are_refused()
    {
        var mapping = new TabularMappingDocument(
            TabularMappingProfile.Family,
            TabularMappingProfile.SchemaUri,
            "1.0.0",
            new CanonicalTarget("connector.erpnext.dto/v1", "/CustomerDto"),
            [new MappingColumn("CustomerNumber", "string", true, "/customerNumber")],
            new Dictionary<string, string> { ["hl:arbitraryCode"] = "run()" });

        var exception = Assert.Throws<DataExchangeAdmissionException>(
            () => TabularMappingAdmission.Validate(mapping));

        Assert.Contains(exception.Refusals, refusal => refusal.Code == "mapping.target_not_canonical");
        Assert.Contains(exception.Refusals, refusal => refusal.Code == "mapping.extension_unknown");
    }
}
