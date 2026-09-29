using System.Reflection;
using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

public sealed class ActorContractTests
{
    [Fact]
    public void CurrentUserValues_AreHostDerivedAndPreserved()
    {
        ICurrentUser actor = new FakeActor("alice", ["inspector", "reviewer"]);

        Assert.Equal("alice", actor.UserId);
        Assert.Equal(["inspector", "reviewer"], actor.Roles);
    }

    [Fact]
    public void EmptyRoles_AreAllowed()
    {
        ICurrentUser actor = new FakeActor("alice", []);

        Assert.Empty(actor.Roles);
    }

    [Fact]
    public void PartyResolutionSignatureAcceptsNoCallerControlledPartyId()
    {
        var method = typeof(IPartyContext).GetMethod(
            nameof(IPartyContext.GetCurrentPartyIdAsync),
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
    }

    [Fact]
    public void Party_resolution_failures_carry_their_reason_and_a_message_without_identifiers()
    {
        (PrincipalPartyResolutionException Error, PrincipalPartyResolutionFailure Failure, string Message)[] cases =
        [
            (PrincipalPartyResolutionException.NoAuthenticatedPrincipal(), PrincipalPartyResolutionFailure.NoAuthenticatedPrincipal,
                "Cannot resolve a PartyId because no authenticated principal is present."),
            (PrincipalPartyResolutionException.TenantUnresolved(), PrincipalPartyResolutionFailure.TenantUnresolved,
                "Cannot resolve a PartyId because no tenant is resolved for the current principal."),
            (PrincipalPartyResolutionException.TenantSentinel(), PrincipalPartyResolutionFailure.TenantSentinel,
                "Cannot resolve a PartyId for a default or system tenant sentinel."),
            (PrincipalPartyResolutionException.NoPartyForPrincipal("user-42", new("tenant-7")), PrincipalPartyResolutionFailure.PartyNotProvisioned,
                "The authenticated principal is not provisioned with a Party in the resolved tenant."),
        ];

        foreach (var (error, failure, message) in cases)
        {
            Assert.Equal(failure, error.Failure);
            Assert.Equal(message, error.Message);
            Assert.DoesNotContain("user-42", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-7", error.Message, StringComparison.Ordinal);
        }
    }

    private sealed record FakeActor(string UserId, IReadOnlyList<string> Roles) : ICurrentUser;
}
