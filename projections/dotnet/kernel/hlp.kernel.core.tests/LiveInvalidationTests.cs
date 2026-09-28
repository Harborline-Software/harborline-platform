using System.Text.Json;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class LiveInvalidationTests
{
    [Fact]
    public void WirePayloadRejectsANullInvalidation() =>
        Assert.Throws<ArgumentNullException>("invalidation", () => LiveInvalidationWire.Serialize(null!));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WirePayloadRejectsAnUnnamedResource(string resourceId) =>
        Assert.Throws<ArgumentException>("invalidation.ResourceId", () =>
            LiveInvalidationWire.Serialize(new LiveInvalidation(resourceId, 7)));

    [Fact]
    public void WirePayloadContainsOnlyResourceIdentityAndVersion()
    {
        var message = new LiveInvalidation("work-item/42", 7);

        using var payload = JsonDocument.Parse(LiveInvalidationWire.Serialize(message));

        var properties = payload.RootElement.EnumerateObject().ToArray();
        Assert.Equal(["resourceId", "version"], properties.Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("work-item/42", payload.RootElement.GetProperty("resourceId").GetString());
        Assert.Equal(7, payload.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(
            ["ResourceId", "Version"],
            typeof(LiveInvalidation).GetProperties().Select(property => property.Name).OrderBy(name => name));
    }
}
