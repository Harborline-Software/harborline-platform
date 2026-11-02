using System.Text.Json;
using Harborline.Contracts.Authorization;
using Xunit;

namespace Harborline.Contracts.Tests;

public sealed class AuthorizationFixtureConformanceTests
{
    [Fact]
    public void Frozen_authorization_fixtures_pass_the_dotnet_projection()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var root = document.RootElement;
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var executed = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.Equal(23, cases.Length);

        foreach (var fixture in cases)
        {
            var id = fixture.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(ids.Add(id!), $"Duplicate fixture ID {id}.");
            var operation = fixture.GetProperty("operation").GetString()!;
            var definitions = root.GetProperty("definitions").Deserialize<RoleDefinition[]>()!;
            var capabilities = root.GetProperty("capabilities").Deserialize<AuthorizationCapabilityDefinition[]>()!;

            switch (operation)
            {
                case "resolve":
                    Assert.Equal(fixture.GetProperty("expected").GetString(), RoleVocabulary.FromApi(definitions)
                        .Resolve(fixture.GetProperty("role").Deserialize<RoleReference>()!)?.DisplayName);
                    break;
                case "require":
                {
                    var vocabulary = RoleVocabulary.FromApi(definitions);
                    var role = fixture.GetProperty("role").Deserialize<RoleReference>()!;
                    if (fixture.TryGetProperty("expectedError", out var expectedError))
                        Assert.Equal(expectedError.GetString(), Assert.Throws<KeyNotFoundException>(() => vocabulary.Require(role)).Message);
                    else
                        Assert.Equal(fixture.GetProperty("expected").GetString(), vocabulary.Require(role).DisplayName);
                    break;
                }
                case "allows":
                    Assert.Equal(fixture.GetProperty("expected").GetBoolean(), RoleGateResolver.Allows(
                        fixture.GetProperty("gate").Deserialize<RoleGate>()!,
                        RoleVocabulary.FromApi(definitions),
                        fixture.GetProperty("held").Deserialize<HeldRoleSet>()!));
                    break;
                case "invalid":
                    Assert.Contains(fixture.GetProperty("expectedError").GetString()!, Assert.Throws<ArgumentException>(() =>
                        RoleVocabulary.FromApi(MutateRoles(definitions, fixture.GetProperty("mutation").GetString()!))).Message, StringComparison.Ordinal);
                    break;
                case "resolve-capability":
                {
                    var expected = fixture.GetProperty("expected");
                    Assert.Equal(expected.ValueKind == JsonValueKind.Null ? (int?)null : expected.GetInt32(),
                        AuthorizationCapabilityRegister.FromDeclarations(capabilities)
                            .Resolve(fixture.GetProperty("capability").Deserialize<AuthorizationCapabilityReference>()!)?.Version);
                    break;
                }
                case "require-capability":
                {
                    var register = AuthorizationCapabilityRegister.FromDeclarations(capabilities);
                    var capability = fixture.GetProperty("capability").Deserialize<AuthorizationCapabilityReference>()!;
                    if (fixture.TryGetProperty("expectedError", out var expectedError))
                    {
                        var expected = expectedError.GetString()!;
                        if (expected.StartsWith("unknown-authorization-capability:", StringComparison.Ordinal))
                            Assert.Equal(expected, Assert.Throws<KeyNotFoundException>(() => register.Require(capability)).Message);
                        else
                            Assert.Contains(expected, Assert.Throws<ArgumentException>(() => register.Require(capability)).Message, StringComparison.Ordinal);
                    }
                    else
                        Assert.Equal(fixture.GetProperty("expected").GetInt32(), register.Require(capability).Version);
                    break;
                }
                case "invalid-capabilities":
                    Assert.Contains(fixture.GetProperty("expectedError").GetString()!, Assert.Throws<ArgumentException>(() =>
                        AuthorizationCapabilityRegister.FromDeclarations(MutateCapabilities(capabilities,
                            fixture.GetProperty("mutation").GetString()!))).Message, StringComparison.Ordinal);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown fixture operation {operation} ({id}).");
            }

            executed[operation] = executed.GetValueOrDefault(operation) + 1;
        }

        Assert.Equal(23, ids.Count);
        Assert.Equal(7, executed.Count);
        Assert.Equal(2, executed["resolve"]);
        Assert.Equal(2, executed["require"]);
        Assert.Equal(5, executed["allows"]);
        Assert.Equal(7, executed["invalid"]);
        Assert.Equal(2, executed["resolve-capability"]);
        Assert.Equal(2, executed["require-capability"]);
        Assert.Equal(3, executed["invalid-capabilities"]);
    }

    private static RoleDefinition[] MutateRoles(RoleDefinition[] definitions, string mutation)
    {
        switch (mutation)
        {
            case "duplicate":
                return [.. definitions, definitions[0]];
            case "unknown-vocabulary":
                definitions[0] = definitions[0] with { Role = definitions[0].Role with { Vocabulary = "sys.roles" } };
                break;
            case "platform-taxonomy":
                definitions[0] = definitions[0] with { Role = definitions[0].Role with { Vocabulary = "tax.roles" } };
                break;
            case "domain-platform":
                definitions[2] = definitions[2] with
                {
                    Role = definitions[2].Role with { Vocabulary = "sys.platform-roles" }, IsSealed = true,
                };
                break;
            case "unsealed-platform":
                definitions[0] = definitions[0] with { IsSealed = false };
                break;
            case "sealed-domain":
                definitions[2] = definitions[2] with { IsSealed = true };
                break;
            case "invented-platform-role":
                definitions[0] = definitions[0] with { Role = definitions[0].Role with { Name = "Captain" } };
                break;
            default:
                throw new InvalidOperationException($"Unknown role fixture mutation {mutation}.");
        }
        return definitions;
    }

    private static AuthorizationCapabilityDefinition[] MutateCapabilities(AuthorizationCapabilityDefinition[] capabilities, string mutation)
    {
        switch (mutation)
        {
            case "duplicate":
                return [.. capabilities, capabilities[0] with { Version = 2 }];
            case "malformed":
                capabilities[0] = capabilities[0] with { Capability = capabilities[0].Capability with { Name = "records" } };
                break;
            case "unversioned":
                capabilities[0] = capabilities[0] with { Version = 0 };
                break;
            default:
                throw new InvalidOperationException($"Unknown capability fixture mutation {mutation}.");
        }
        return capabilities;
    }

    private static string FindFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "conformance", "hlp.contracts.authorization", "fixtures.yaml");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Unable to locate conformance/hlp.contracts.authorization/fixtures.yaml.");
    }
}
