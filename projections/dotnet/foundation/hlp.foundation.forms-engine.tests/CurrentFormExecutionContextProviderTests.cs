using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Capabilities;
using Harborline.Foundation.MultiTenancy;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class CurrentFormExecutionContextProviderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("current-context-key-for-tests");
    private static readonly TenantMetadata Tenant = new()
    {
        Id = new TenantId("tenant-a"),
        Name = "tenant-a",
        Status = TenantStatus.Active,
    };

    [Fact]
    public async Task ExecuteAsync_MissingActionOrExpiredContext_Denies()
    {
        var actor = new MutableActor("alice", ["reader"], Tenant);
        var keys = new FormCapabilityVerificationTests.TestKeys(Key);
        var actionless = FormMacaroonCodec.EncodeSigned(
            MacaroonFormCapabilityIssuer.DefaultLocation,
            "missing-action",
            ["tenant = tenant-a", "subject = alice", $"expires = {Now.AddMinutes(5):O}"],
            Key);
        var expired = FormMacaroonCodec.EncodeSigned(
            MacaroonFormCapabilityIssuer.DefaultLocation,
            "expired",
            ["tenant = tenant-a", "subject = alice", $"expires = {Now.AddTicks(-1):O}", "action = read"],
            Key);

        foreach (var bearer in new string?[] { null, actionless, expired })
        {
            var provider = Provider(actor, bearer, keys, new RecordingPartyResolver(Guid.NewGuid()));
            var error = await Assert.ThrowsAsync<FormEngineDeniedException>(async () =>
                await provider.GetRequiredAsync(FormEngineAction.Read));
            Assert.Equal("form.engine.denied", error.Code);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UsesCurrentOktaRoles_NotBearerEmbeddedRoles()
    {
        var actor = new MutableActor("alice", ["reader"], Tenant);
        var keys = new FormCapabilityVerificationTests.TestKeys(Key);
        var issuer = new MacaroonFormCapabilityIssuer(keys);
        var bearer = await issuer.IssueAsync(
            Tenant.Id,
            "alice",
            ["bearer-admin"],
            [FormCapabilityAction.Write],
            Now.AddMinutes(5));
        var provider = Provider(actor, bearer, keys, new RecordingPartyResolver(Guid.NewGuid()));

        var first = await provider.GetRequiredAsync(FormEngineAction.Validate);
        actor.Roles = ["writer"];
        var second = await provider.GetRequiredAsync(FormEngineAction.Submit);

        Assert.Equal(["reader"], first.Roles);
        Assert.Equal(["writer"], second.Roles);
        Assert.DoesNotContain("bearer-admin", first.Roles);
        Assert.DoesNotContain("bearer-admin", second.Roles);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesTenantPartyActorAndRolesAtomically()
    {
        var partyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var actor = new MutableActor("alice", ["reader", "inspector"], Tenant);
        var keys = new FormCapabilityVerificationTests.TestKeys(Key);
        var issuer = new MacaroonFormCapabilityIssuer(keys);
        var bearer = await issuer.IssueAsync(
            Tenant.Id,
            "alice",
            [],
            [FormCapabilityAction.Read],
            Now.AddMinutes(5));
        var party = new RecordingPartyResolver(partyId);
        var bearers = new RecordingBearer(bearer);
        var provider = new CurrentFormExecutionContextProvider(
            actor,
            party,
            bearers,
            new MacaroonFormCapabilityVerifier(keys),
            new FixedClock(Now));

        var scope = await provider.GetRequiredAsync(FormEngineAction.Read);

        Assert.Equal(Tenant.Id, scope.Tenant);
        Assert.Equal(partyId, scope.PartyId);
        Assert.Equal("alice", scope.ActorId);
        Assert.Equal(["reader", "inspector"], scope.Roles);
        Assert.Equal(1, party.Calls);
        Assert.Equal(1, bearers.Calls);
        Assert.Equal(("alice", Tenant.Id), party.Requests.Single());

        party.BeforeReturn = () => actor.Roles = ["changed-during-resolution"];
        var atomic = await provider.GetRequiredAsync(FormEngineAction.Read);
        Assert.Equal(["reader", "inspector"], atomic.Roles);
        Assert.Equal("alice", atomic.ActorId);
        Assert.Equal(Tenant.Id, atomic.Tenant);
    }

    [Fact]
    public async Task CurrentRequestContext_RecoveryRequiresSeparateJobAdapter()
    {
        var actor = new MutableActor("alice", ["admin"], Tenant);
        var keys = new FormCapabilityVerificationTests.TestKeys(Key);
        var issuer = new MacaroonFormCapabilityIssuer(keys);
        var bearer = await issuer.IssueAsync(
            Tenant.Id,
            "alice",
            [],
            [FormCapabilityAction.Write],
            Now.AddMinutes(5));
        var provider = Provider(actor, bearer, keys, new RecordingPartyResolver(Guid.NewGuid()));

        await Assert.ThrowsAsync<FormEngineDeniedException>(async () =>
            await provider.GetRequiredAsync(FormEngineAction.RecoverProjections));
    }

    [Fact]
    public async Task ExecuteAsync_CapabilityExpiringDuringPartyResolution_Denies()
    {
        var actor = new MutableActor("alice", ["reader"], Tenant);
        var keys = new FormCapabilityVerificationTests.TestKeys(Key);
        var issuer = new MacaroonFormCapabilityIssuer(keys);
        var bearer = await issuer.IssueAsync(
            Tenant.Id, "alice", [], [FormCapabilityAction.Read], Now.AddSeconds(1));
        var clock = new MutableClock(Now);
        var party = new RecordingPartyResolver(Guid.NewGuid())
        {
            BeforeReturn = () => clock.Now = Now.AddSeconds(2),
        };
        var provider = new CurrentFormExecutionContextProvider(
            actor, party, new RecordingBearer(bearer),
            new MacaroonFormCapabilityVerifier(keys), clock);

        await Assert.ThrowsAsync<FormEngineDeniedException>(async () =>
            await provider.GetRequiredAsync(FormEngineAction.Read));
    }

    private static CurrentFormExecutionContextProvider Provider(
        MutableActor actor,
        string? bearer,
        IFormCapabilityRootKeyProvider keys,
        IPrincipalPartyResolver party) =>
        new(
            actor,
            party,
            new RecordingBearer(bearer),
            new MacaroonFormCapabilityVerifier(keys),
            new FixedClock(Now));

    private sealed class MutableActor(
        string userId,
        IReadOnlyList<string> roles,
        TenantMetadata? tenant) : IAuthenticatedActorContext
    {
        public string UserId { get; set; } = userId;
        public IReadOnlyList<string> Roles { get; set; } = roles;
        public TenantMetadata? Tenant { get; set; } = tenant;
    }

    private sealed class RecordingBearer(string? bearer) : IFormCapabilityBearerProvider
    {
        public int Calls { get; private set; }

        public ValueTask<string?> GetBearerAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(bearer);
        }
    }

    private sealed class RecordingPartyResolver(Guid partyId) : IPrincipalPartyResolver
    {
        public int Calls { get; private set; }
        public Action? BeforeReturn { get; set; }
        public List<(string ActorId, TenantId Tenant)> Requests { get; } = [];

        public ValueTask<Guid?> ResolveAsync(
            string userId,
            TenantId tenantId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add((userId, tenantId));
            BeforeReturn?.Invoke();
            return ValueTask.FromResult<Guid?>(partyId);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
