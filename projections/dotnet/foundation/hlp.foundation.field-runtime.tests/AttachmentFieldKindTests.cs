using Harborline.Contracts.Fields;
using Xunit;

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
    [InlineData("item_limit_bytes", "0")]
    [InlineData("item_limit_bytes", "-1")]
    [InlineData("accepted_media_types", "[]")]
    [InlineData("accepted_media_types", "[\"application/exe\"]")]
    [InlineData("accepted_media_types", "null")]
    [InlineData("maximum_count", "1")]
    public void Invalid_or_widening_parameters_refuse_at_binding(string name, string value)
        => Assert.Throws<FieldAdmissionException>(() => Bind(new() { [name] = value }));

    private static ICompiledAttachmentFieldKind Bind(Dictionary<string, string> parameters)
        => Assert.IsAssignableFrom<ICompiledAttachmentFieldKind>(new FieldKindRuntime(new FieldKindRegistry([
            new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100)
            .Bind(new("attachment", "1.0.0", parameters), "/fields/0/kind"));

    private static string Reference(long length) =>
        "{\"content_id\":\"hmac-sha256:" + new string('a', 64) + "\",\"length\":"
        + length.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ",\"media_type\":\"text/plain\",\"display_name\":\"evidence.txt\",\"owner\":{\"record\":\"r1\",\"element\":\"evidence\"}}";
}
