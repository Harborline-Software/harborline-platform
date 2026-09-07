using System.Reflection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.MultiTenancy;
using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

public sealed class ActorContractTests
{
    private static readonly Guid AlicePartyId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void CurrentUserValues_AreHostDerivedAndPreserved()
    {
        var actor = Actor("alice", "acme", ["inspector", "reviewer"]);

        Assert.Equal("alice", actor.UserId);
        Assert.Equal(["inspector", "reviewer"], actor.Roles);
    }

    [Fact]
    public void EmptyRoles_AreAllowed()
    {
        var actor = Actor("alice", "acme", []);

        Assert.Empty(actor.Roles);
    }

    [Fact]
    public async Task PartyResolvesFromCurrentActor()
    {
        var resolver = Resolver((user, tenant, _) =>
            user == "alice" && tenant == new TenantId("acme") ? AlicePartyId : null);
        var context = new PartyContext(Actor("alice", "acme"), resolver);

        Assert.Equal(AlicePartyId, await context.GetCurrentPartyIdAsync());
    }

    [Fact]
    public async Task AnonymousActorFailsClosed()
    {
        var resolver = Resolver((_, _, _) => AlicePartyId);
        var context = new PartyContext(Actor(string.Empty, "acme"), resolver);

        var exception = await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => context.GetCurrentPartyIdAsync().AsTask());

        Assert.Equal(PrincipalPartyResolutionFailure.NoAuthenticatedPrincipal, exception.Failure);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task WhitespaceActorFailsClosed()
    {
        var resolver = Resolver((_, _, _) => AlicePartyId);
        var context = new PartyContext(Actor("   ", "acme"), resolver);

        var exception = await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => context.GetCurrentPartyIdAsync().AsTask());

        Assert.Equal(PrincipalPartyResolutionFailure.NoAuthenticatedPrincipal, exception.Failure);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task UnresolvedTenantFailsClosed()
    {
        var resolver = Resolver((_, _, _) => AlicePartyId);
        var context = new PartyContext(new FakeActor("alice", [], null), resolver);

        var exception = await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => context.GetCurrentPartyIdAsync().AsTask());

        Assert.Equal(PrincipalPartyResolutionFailure.TenantUnresolved, exception.Failure);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task DefaultTenantFailsClosed()
    {
        var resolver = Resolver((_, _, _) => AlicePartyId);
        var context = new PartyContext(Actor("alice", default(TenantId)), resolver);

        var exception = await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => context.GetCurrentPartyIdAsync().AsTask());

        Assert.Equal(PrincipalPartyResolutionFailure.TenantSentinel, exception.Failure);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task SystemTenantFailsClosed()
    {
        var resolver = Resolver((_, _, _) => AlicePartyId);
        var context = new PartyContext(Actor("alice", TenantId.System), resolver);

        var exception = await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => context.GetCurrentPartyIdAsync().AsTask());

        Assert.Equal(PrincipalPartyResolutionFailure.TenantSentinel, exception.Failure);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task UnmappedPartyFailsClosed()
    {
        var context = new PartyContext(Actor("nobody", "acme"), Resolver((_, _, _) => null));

        var exception = await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => context.GetCurrentPartyIdAsync().AsTask());

        Assert.Equal(PrincipalPartyResolutionFailure.PartyNotProvisioned, exception.Failure);
    }

    [Fact]
    public async Task CrossTenantPartyFailsClosed()
    {
        var resolver = Resolver((user, tenant, _) =>
            user == "alice" && tenant == new TenantId("acme") ? AlicePartyId : null);
        var context = new PartyContext(Actor("alice", "beta"), resolver);

        var exception = await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => context.GetCurrentPartyIdAsync().AsTask());

        Assert.Equal(PrincipalPartyResolutionFailure.PartyNotProvisioned, exception.Failure);
    }

    [Fact]
    public async Task SameActorContextValuesAreForwardedToResolver()
    {
        var resolver = Resolver((_, _, _) => AlicePartyId);
        var context = new PartyContext(Actor("alice", "acme"), resolver);

        await context.GetCurrentPartyIdAsync();

        Assert.Equal("alice", resolver.LastUserId);
        Assert.Equal(new TenantId("acme"), resolver.LastTenantId);
    }

    [Fact]
    public async Task CancellationIsForwardedToResolver()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var resolver = Resolver((_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return AlicePartyId;
        });
        var context = new PartyContext(Actor("alice", "acme"), resolver);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.GetCurrentPartyIdAsync(cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, resolver.LastCancellationToken);
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

    private static FakeActor Actor(
        string userId,
        string tenantId,
        IReadOnlyList<string>? roles = null) =>
        Actor(userId, new TenantId(tenantId), roles);

    private static FakeActor Actor(
        string userId,
        TenantId tenantId,
        IReadOnlyList<string>? roles = null) =>
        new(
            userId,
            roles ?? [],
            new TenantMetadata { Id = tenantId, Name = tenantId.ToString() });

    private static CapturingResolver Resolver(
        Func<string, TenantId, CancellationToken, Guid?> resolve) => new(resolve);

    private sealed record FakeActor(
        string UserId,
        IReadOnlyList<string> Roles,
        TenantMetadata? Tenant) : IAuthenticatedActorContext;

    private sealed class CapturingResolver(
        Func<string, TenantId, CancellationToken, Guid?> resolve) : IPrincipalPartyResolver
    {
        public int Calls { get; private set; }

        public string? LastUserId { get; private set; }

        public TenantId LastTenantId { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask<Guid?> ResolveAsync(
            string userId,
            TenantId tenantId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUserId = userId;
            LastTenantId = tenantId;
            LastCancellationToken = cancellationToken;
            return ValueTask.FromResult(resolve(userId, tenantId, cancellationToken));
        }
    }
}
