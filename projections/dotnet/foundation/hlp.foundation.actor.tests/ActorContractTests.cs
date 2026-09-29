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

    private sealed record FakeActor(string UserId, IReadOnlyList<string> Roles) : ICurrentUser;
}
