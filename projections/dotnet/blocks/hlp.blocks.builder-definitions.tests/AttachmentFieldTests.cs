using System.Text;
using System.Text.Json;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class AttachmentFieldTests
{
    [Fact]
    public void A_field_carries_its_authored_required_condition_without_defaulting_one()
    {
        // Oracle: DES-0054 cc-1 / DES-0015 ck-8 and ck-42: an expression, unset by default.
        const string authored = """{"fields":[{"display_name":"Evidence","field_key":"evidence","required_condition":{"==":[{"var":"status"},"closed"]}}],"name":"Case","record_type_id":"cases.case"}""";
        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(authored));
        using var body = JsonDocument.Parse(RecordTypeDefinitionJson.SerializeCanonical(document));
        Assert.Equal("""{"==":[{"var":"status"},"closed"]}""", body.RootElement.GetProperty("fields")[0].GetProperty("required_condition").GetRawText());

        var fresh = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(
            """{"fields":[{"display_name":"Evidence","field_key":"evidence"}],"name":"Case","record_type_id":"cases.case"}"""));
        using var newBody = JsonDocument.Parse(RecordTypeDefinitionJson.SerializeCanonical(fresh));
        Assert.False(newBody.RootElement.GetProperty("fields")[0].TryGetProperty("required_condition", out _));
    }
}
