using System.Text.Json;
using Harborline.Contracts.Authorization;
using Xunit;

namespace Harborline.Contracts.Tests;

public sealed class AuthorizationContractTests
{
    private static readonly RoleDefinition Auditor = new(
        Guid.Parse("3b69e3cb-ec5e-4ad7-8c90-16ebc1896102"),
        RoleReference.Auditor,
        "Auditor",
        new(RoleOwnerKind.Platform, "harborline-platform"),
        true);

    [Fact]
    public void Api_shaped_vocabulary_resolves_and_role_gate_fails_closed()
    {
        var vocabulary = RoleVocabulary.FromApi([Auditor]);
        Assert.Equal(Auditor, vocabulary.Resolve(RoleReference.Auditor));
        Assert.True(RoleGateResolver.Allows(new([RoleReference.Auditor]), vocabulary, new([RoleReference.Auditor])));
        Assert.False(RoleGateResolver.Allows(
            new([new(RoleVocabularies.Domain, "missing")]), vocabulary, new([RoleReference.Auditor])));
        Assert.False(RoleGateResolver.Allows(
            new([new("sys.roles", "auditor")]), vocabulary, new([RoleReference.Auditor])));
    }

    [Fact]
    public void Fixed_platform_vocabulary_matches_api_and_excludes_job_titles()
    {
        Assert.Equal(["administrator", "auditor"], PlatformRoleVocabulary.Roles.Select(role => role.Name).Order());
        Assert.DoesNotContain(PlatformRoleVocabulary.Roles, role =>
            new[] { "Captain", "EngineerOfficer", "Navigator" }.Contains(role.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Role_and_standing_wire_shapes_are_distinct()
    {
        var role = JsonSerializer.SerializeToElement(RoleReference.Auditor);
        var standing = JsonSerializer.SerializeToElement(new RecordStandingReference("Compliant"));
        Assert.True(role.TryGetProperty("vocabulary", out _));
        Assert.False(standing.TryGetProperty("vocabulary", out _));
        Assert.Equal("Compliant", standing.GetProperty("name").GetString());
    }

    [Fact]
    public void Duplicate_and_ownership_inversions_are_rejected()
    {
        Assert.Contains("duplicate-role-reference", Assert.Throws<ArgumentException>(() => RoleVocabulary.FromApi([Auditor, Auditor])).Message);
        Assert.Contains("invalid-role-ownership", Assert.Throws<ArgumentException>(() => RoleVocabulary.FromApi([
            Auditor with { Role = new(RoleVocabularies.Domain, "auditor") }
        ])).Message);
        Assert.Contains("invalid-role-ownership", Assert.Throws<ArgumentException>(() => RoleVocabulary.FromApi([
            Auditor with { IsSealed = false }
        ])).Message);
        Assert.Contains("invalid-platform-role", Assert.Throws<ArgumentException>(() => RoleVocabulary.FromApi([
            Auditor with { Role = new(RoleVocabularies.Platform, "Captain") }
        ])).Message);
    }
}
