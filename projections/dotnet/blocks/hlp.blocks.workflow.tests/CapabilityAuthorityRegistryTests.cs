using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// The canonical capability→authority registry (ADR 0143 — the .NET closure for red-team Chains 1+5 / F2).
/// It reads the SAME <c>command-authority.json</c> the carrier-sdk `authorityOf` + the `.mjs` bridge read
/// (embedded from carrier-sdk at build). These tests pin the load-bearing behaviour: the real CP ops
/// resolve CP, the reversible notify effect resolves AP, and an UNKNOWN capability fails closed to CP.
/// </summary>
public sealed class CapabilityAuthorityRegistryTests
{
    private static readonly ICapabilityAuthorityRegistry Registry = CapabilityAuthorityRegistry.Canonical;

    [Fact]
    public void The_canonical_registry_loads_from_the_embedded_command_authority_json()
    {
        // If the embedded resource were missing/malformed, FromEmbeddedResource() throws at type-init;
        // reaching here (and resolving a known op) proves the single-source embed is wired.
        Assert.Equal(ActionClassification.CP, Registry.AuthorityOf("ledger.post-journal-entry"));
    }

    [Theory]
    [InlineData("ledger.post-journal-entry")]
    [InlineData("ledger.void-payment")]
    [InlineData("demo-cp-op")]
    public void Real_and_synthetic_cp_ops_resolve_cp(string capability)
        => Assert.Equal(ActionClassification.CP, Registry.AuthorityOf(capability));

    [Theory]
    [InlineData("notify.email")]
    [InlineData("invoke")]
    [InlineData("health")]
    public void Ap_capabilities_resolve_ap(string capability)
        => Assert.Equal(ActionClassification.AP, Registry.AuthorityOf(capability));

    [Fact]
    public void An_unknown_capability_fails_closed_to_cp()
    {
        // Mirrors the TS `authorityOf` unknown⇒CP default — an unregistered capability is never AP.
        Assert.Equal(ActionClassification.CP, Registry.AuthorityOf("totally.unregistered-capability"));
    }

    [Fact]
    public void FromMap_still_fails_closed_to_cp_for_an_absent_capability()
    {
        var registry = CapabilityAuthorityRegistry.FromMap(new Dictionary<string, ActionClassification>
        {
            ["x.reversible"] = ActionClassification.AP,
        });
        Assert.Equal(ActionClassification.AP, registry.AuthorityOf("x.reversible"));
        Assert.Equal(ActionClassification.CP, registry.AuthorityOf("y.unknown"));
    }
}
