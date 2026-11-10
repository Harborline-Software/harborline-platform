using Harborline.Contracts.Fields;
using Xunit;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class AttachmentFieldKindTests
{
    [Fact]
    public void Field_limit_above_store_is_retained_while_effective_limit_stays_at_store()
    {
        // DES-0054 ck-15, cc-1: never rewrite a pack limit, never widen a store limit.
        var field = Bind(new() { ["item_limit_bytes"] = "200" });
        Assert.Equal(200, field.DeclaredLimitBytes);
        Assert.Equal(100, field.EffectiveLimitBytes);
        Assert.True(field.LimitAboveStore);
        Assert.Equal("200", field.JsonSchema.GetProperty("x-harborline-field-kind")
            .GetProperty("parameters").GetProperty("item_limit_bytes").GetString());
        Assert.Equal("content.too-large", Assert.Single(field.ValidateJson(Reference(101), "/evidence/0")).Code);
    }

    [Fact]
    public void Attachment_references_preserve_metadata_and_field_media_narrows_the_store()
    {
        var field = Bind(new() { ["accepted_media_types"] = "[\"text/plain\"]", ["item_limit_bytes"] = "50" });
        Assert.Equal(50, field.EffectiveLimitBytes);
        Assert.Equal(["text/plain"], field.AcceptedMediaTypes);
        Assert.Empty(field.ValidateJson(Reference(50), "/evidence/0"));
        Assert.Equal("content.field-media-type-not-allowed", Assert.Single(field.ValidateJson(
            Reference(50).Replace("text/plain", "image/png", StringComparison.Ordinal), "/evidence/0")).Code);
        Assert.Equal("field.value_type_mismatch", Assert.Single(field.ValidateJson("\"id\"", "/evidence/0")).Code);
    }

    [Theory]
    [InlineData("item_limit_bytes", "0", "field.kind_parameter_invalid")]
    [InlineData("item_limit_bytes", "-1", "field.kind_parameter_invalid")]
    [InlineData("accepted_media_types", "[]", "content.field-media-type-not-allowed")]
    [InlineData("accepted_media_types", "[\"application/exe\"]", "content.field-media-type-not-allowed")]
    [InlineData("accepted_media_types", "null", "field.kind_parameter_invalid")]
    [InlineData("maximum_count", "1", "field.kind_parameter_unknown")]
    public void Invalid_or_widening_parameters_refuse_at_binding(string name, string value, string code)
    {
        var refusal = Assert.Single(Assert.Throws<FieldAdmissionException>(() => Bind(new() { [name] = value })).Refusals);
        Assert.Equal(code, refusal.Code);
        Assert.Equal("/fields/0/kind/parameters/" + name, refusal.JsonPointer);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
    }

    [Fact]
    public void Default_binding_uses_the_store_contract_without_inventing_a_field_limit()
    {
        var field = Bind(new());
        Assert.Null(field.DeclaredLimitBytes);
        Assert.False(field.LimitAboveStore);
        Assert.Equal(100, field.EffectiveLimitBytes);
        Assert.Equal(["application/pdf", "image/jpeg", "image/png", "image/webp", "text/csv", "text/plain"], field.AcceptedMediaTypes);
        var schema = field.JsonSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["content_id", "length", "media_type", "display_name", "owner"], schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("attachment", schema.GetProperty("x-harborline-field-kind").GetProperty("kind_id").GetString());
        Assert.Equal("1.0.0", schema.GetProperty("x-harborline-field-kind").GetProperty("version").GetString());
        Assert.Equal(100, schema.GetProperty("properties").GetProperty("length").GetProperty("maximum").GetInt64());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)field.AcceptedMediaTypes)[0] = "application/exe");
    }

    [Theory]
    [InlineData("content_id", "\"bad\"")]
    [InlineData("content_id", "null")]
    [InlineData("length", "-1")]
    [InlineData("length", "\"3\"")]
    [InlineData("length", "0.5")]
    [InlineData("display_name", "\"\"")]
    [InlineData("display_name", "\" \"")]
    [InlineData("media_type", "null")]
    [InlineData("display_name", "false")]
    [InlineData("owner", "{\"record\":true,\"element\":\"evidence\"}")]
    [InlineData("owner", "{}")]
    [InlineData("owner", "{\"record\":\"\",\"element\":\"evidence\"}")]
    [InlineData("owner", "{\"record\":\"r1\",\"element\":null}")]
    public void Malformed_reference_members_refuse_with_the_authored_pointer(string member, string replacement)
    {
        var value = JsonNode.Parse(Reference(3))!.AsObject();
        value[member] = JsonNode.Parse(replacement);
        var refusal = Assert.Single(Bind(new()).ValidateJson(value.ToJsonString(), "/evidence/1"));
        Assert.Equal("field.value_type_mismatch", refusal.Code);
        Assert.Equal("/evidence/1", refusal.JsonPointer);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
        Assert.DoesNotContain("evidence.txt", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[")]
    [InlineData(null)]
    public void Malformed_json_never_throws_out_of_the_field_contract(string? json)
        => Assert.Equal("field.value_malformed", Assert.Single(Bind(new()).ValidateJson(json!, "/evidence/0")).Code);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_store_limits_are_not_admitted(long limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FieldKindRuntime(new FieldKindRegistry([]), limit));

    [Fact]
    public void Attachment_binding_requires_the_hosts_measured_store_limit()
    {
        var runtime = new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]));
        var refusal = Assert.Single(Assert.Throws<FieldAdmissionException>(() => runtime.Bind(new("attachment", "1.0.0", new Dictionary<string,string>()), "/kind")).Refusals);
        Assert.Equal("content.store-limit-unavailable", refusal.Code);
        Assert.Equal("/kind", refusal.JsonPointer);
    }

    [Theory]
    [InlineData("hmac-sha257:", 'a')]
    [InlineData("hmac-sha256:", 'g')]
    [InlineData("hmac-sha256:", 'A')]
    public void Content_identity_uses_the_declared_algorithm_and_lowercase_hex(string prefix, char digit)
    {
        var value = JsonNode.Parse(Reference(3))!.AsObject();
        value["content_id"] = prefix + new string(digit, 64);
        Assert.Equal("field.value_type_mismatch", Assert.Single(Bind(new()).ValidateJson(value.ToJsonString(), "/evidence/0")).Code);
    }

    [Fact]
    public void Unknown_parameter_diagnostics_escape_authored_pointer_tokens()
    {
        // RFC 6901 tokens: slash -> ~1 and tilde -> ~0, independently of the runtime's escaping helper.
        var refusal = Assert.Single(Assert.Throws<FieldAdmissionException>(() => Bind(new() { ["path/~"] = "1" })).Refusals);
        Assert.Equal("field.kind_parameter_unknown", refusal.Code);
        Assert.Equal("/fields/0/kind/parameters/path~1~0", refusal.JsonPointer);
    }

    private static ICompiledAttachmentFieldKind Bind(Dictionary<string, string> parameters)
        => Assert.IsAssignableFrom<ICompiledAttachmentFieldKind>(new FieldKindRuntime(new FieldKindRegistry([
            new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100)
            .Bind(new("attachment", "1.0.0", parameters), "/fields/0/kind"));

    private static string Reference(long length) =>
        "{\"content_id\":\"hmac-sha256:" + new string('a', 64) + "\",\"length\":"
        + length.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ",\"media_type\":\"text/plain\",\"display_name\":\"evidence.txt\",\"owner\":{\"record\":\"r1\",\"element\":\"evidence\"}}";
}
