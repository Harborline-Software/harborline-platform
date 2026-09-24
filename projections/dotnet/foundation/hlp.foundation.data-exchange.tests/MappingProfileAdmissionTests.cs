using Harborline.Foundation.DataExchange;
using System.Text.Json;
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

    [Fact]
    public void Portable_json_is_csvw_metadata_with_namespaced_harborline_extensions()
    {
        var mapping = new TabularMappingDocument(
            TabularMappingProfile.Family,
            TabularMappingProfile.SchemaUri,
            "1.0.0",
            new CanonicalTarget("records.customer/v1", "/customers"),
            [new MappingColumn("CustomerNumber", "string", true, "/customerNumber", Null: ["NA"], Extensions: new Dictionary<string, string> { ["hl:transform"] = "trim" })],
            new Dictionary<string, string> { ["hl:operation"] = "upsert" },
            "https://imports.harborline.test/customers.csv");

        var json = TabularMappingJson.Serialize(mapping);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("http://www.w3.org/ns/csvw", root.GetProperty("@context")[0].GetString());
        Assert.Equal("https://imports.harborline.test/customers.csv", root.GetProperty("url").GetString());
        Assert.Equal(TabularMappingProfile.Family, root.GetProperty("hl:profile").GetString());
        var column = root.GetProperty("tableSchema").GetProperty("columns")[0];
        Assert.Equal("CustomerNumber", column.GetProperty("name").GetString());
        Assert.Equal("string", column.GetProperty("datatype").GetString());
        Assert.Equal("/customerNumber", column.GetProperty("hl:target").GetString());
        Assert.Equal("trim", column.GetProperty("hl:transform").GetString());

        var roundTrip = TabularMappingJson.Deserialize(json);
        Assert.Equal(mapping.Profile, roundTrip.Profile);
        Assert.Equal(mapping.SchemaUri, roundTrip.SchemaUri);
        Assert.Equal(mapping.SourceUrl, roundTrip.SourceUrl);
        Assert.Equal(mapping.Columns[0].Null, roundTrip.Columns[0].Null);
    }
}
