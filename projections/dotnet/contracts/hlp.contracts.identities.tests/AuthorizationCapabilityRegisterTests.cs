using System.Text.Json;
using Harborline.Contracts.Authorization;
using Xunit;

namespace Harborline.Contracts.Tests;

/// <summary>T-747: Access's register of named, versioned authorization capabilities (DES-0032 access-ck-3, T-724 ruling 77).</summary>
public sealed class AuthorizationCapabilityRegisterTests
{
    private static readonly AuthorizationCapabilityDefinition RecordsWrite = new(new("records:write"), 1);
    private static readonly AuthorizationCapabilityDefinition LayoutOpen = new(new("layout:open"), 2);

    [Fact(DisplayName = "T-747 register: a declared capability resolves by name with its version, and an unknown one is refused by name")]
    public void A_declared_capability_resolves_and_an_unknown_one_is_refused_by_name()
    {
        var register = AuthorizationCapabilityRegister.FromDeclarations([RecordsWrite, LayoutOpen]);

        Assert.Equal(LayoutOpen, register.Resolve(new("layout:open")));
        Assert.Equal(2, register.Require(new("layout:open")).Version);
        Assert.Null(register.Resolve(new("layout:submit")));
        var refused = Assert.Throws<KeyNotFoundException>(() => register.Require(new("layout:submit")));
        Assert.Equal("unknown-authorization-capability: layout:submit", refused.Message);
    }

    [Fact(DisplayName = "T-747 register: duplicate, malformed and unversioned declarations are refused, so the register never mints a name")]
    public void Malformed_declarations_are_refused()
    {
        Assert.Contains("duplicate-authorization-capability: records:write", Assert.Throws<ArgumentException>(() =>
            AuthorizationCapabilityRegister.FromDeclarations([RecordsWrite, RecordsWrite with { Version = 2 }])).Message);
        foreach (var name in new[] { "", "records", "Records:write", "records:", ":write", "records:write:all", "records: write" })
            Assert.Contains("invalid-authorization-capability", Assert.Throws<ArgumentException>(() =>
                AuthorizationCapabilityRegister.FromDeclarations([new(new(name), 1)])).Message);
        Assert.Contains("invalid-authorization-capability-version: records:write", Assert.Throws<ArgumentException>(() =>
            AuthorizationCapabilityRegister.FromDeclarations([RecordsWrite with { Version = 0 }])).Message);
    }

    [Fact(DisplayName = "T-747 register: a malformed reference is refused rather than matched as a string")]
    public void A_malformed_reference_is_refused()
    {
        var register = AuthorizationCapabilityRegister.FromDeclarations([RecordsWrite]);
        Assert.Contains("invalid-authorization-capability", Assert.Throws<ArgumentException>(() => register.Resolve(new("RECORDS:WRITE"))).Message);
    }

    [Fact(DisplayName = "T-747 register: capability, role and standing references have distinct wire shapes")]
    public void Capability_wire_shape_is_distinct_from_role_and_standing()
    {
        var capability = JsonSerializer.SerializeToElement(new AuthorizationCapabilityReference("records:write"));
        Assert.Equal("records:write", capability.GetProperty("name").GetString());
        Assert.False(capability.TryGetProperty("vocabulary", out _));
        var definition = JsonSerializer.SerializeToElement(RecordsWrite);
        Assert.Equal("records:write", definition.GetProperty("capability").GetProperty("name").GetString());
        Assert.Equal(1, definition.GetProperty("version").GetInt32());
        Assert.NotEqual(typeof(AuthorizationCapabilityReference), typeof(RecordStandingReference));
    }
}
