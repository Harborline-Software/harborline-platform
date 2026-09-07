using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Contracts.Tests;

public sealed class IdentityContractTests
{
    [Fact]
    public void TenantId_accepts_a_real_tenant()
    {
        var tenant = new TenantId("tenant-acme");

        Assert.Equal("tenant-acme", tenant.Value);
        Assert.False(tenant.IsSystemSentinel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("__external__")]
    public void TenantId_rejects_invalid_external_values(string value) =>
        Assert.ThrowsAny<ArgumentException>(() => new TenantId(value));

    [Fact]
    public void TenantId_default_and_system_are_fail_closed()
    {
        Assert.True(default(TenantId).IsSystemSentinel);
        Assert.True(TenantId.System.IsSystemSentinel);
        Assert.Equal("__system__", TenantId.System.Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(TenantId)));
    }

    [Fact]
    public void TenantId_json_round_trips_a_real_tenant()
    {
        var json = JsonSerializer.Serialize(new TenantId("tenant-acme"));

        Assert.Equal("\"tenant-acme\"", json);
        Assert.Equal(new TenantId("tenant-acme"), JsonSerializer.Deserialize<TenantId>(json));
    }

    [Fact]
    public void EntityId_round_trips_canonically()
    {
        var id = EntityId.Parse("property:acme-rentals/42");

        Assert.Equal("property", id.Scheme);
        Assert.Equal("acme-rentals", id.Authority);
        Assert.Equal("42", id.LocalPart);
        Assert.Equal("property:acme-rentals/42", id.ToString());
        Assert.Equal(id, JsonSerializer.Deserialize<EntityId>(JsonSerializer.Serialize(id)));
    }

    [Theory]
    [InlineData(":acme/42")]
    [InlineData("property:/42")]
    [InlineData("property:acme/")]
    [InlineData("property:   /42")]
    public void EntityId_rejects_malformed_values(string value)
    {
        Assert.Throws<FormatException>(() => EntityId.Parse(value));
        Assert.False(EntityId.TryParse(value, out _));
    }

    [Fact]
    public void EntityId_try_parse_rejects_null() => Assert.False(EntityId.TryParse(null, out _));
}
