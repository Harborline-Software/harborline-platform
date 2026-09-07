using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.MultiTenancy;
using Xunit;

namespace Harborline.Foundation.Session.Tests;

public sealed class SessionContractTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Fact]
    public void TouchIsImmutable() { var original = Record(); var touched = original.Touch(Epoch.AddMinutes(1)); Assert.NotSame(original, touched); Assert.Equal(Epoch, original.LastSeenUtc); Assert.Equal(Epoch.AddMinutes(1), touched.LastSeenUtc); }

    [Fact]
    public void AbsoluteExpiryIsEnforced() => Assert.True(Record().IsExpired(Epoch.AddHours(8).AddTicks(1), TimeSpan.FromMinutes(30)));

    [Fact]
    public void IdleExpiryIsEnforced() => Assert.True(Record().IsExpired(Epoch.AddMinutes(30).AddTicks(1), TimeSpan.FromMinutes(30)));

    [Fact]
    public void ExactExpiryBoundariesRemainActive() => Assert.False(Record(absoluteHours: 0.5).IsExpired(Epoch.AddMinutes(30), TimeSpan.FromMinutes(30)));

    [Fact]
    public async Task StoreCreatesAndLoads() { var store = new InMemorySessionStore(); var record = Record(); await store.CreateAsync(record); Assert.Equal(record, await store.GetAsync(record.SessionId)); }

    [Fact]
    public async Task DuplicateSessionIsRejected() { var store = await Store(); await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(Record()).AsTask()); }

    [Fact]
    public async Task StoreTouchAdvancesLastSeen() { var store = await Store(); var touched = await store.TouchAsync("sid-1", Epoch.AddMinutes(2)); Assert.Equal(Epoch.AddMinutes(2), touched?.LastSeenUtc); }

    [Fact]
    public async Task StoreRevocationIsIdempotent() { var store = await Store(); Assert.True(await store.RemoveAsync("sid-1")); Assert.False(await store.RemoveAsync("sid-1")); }

    [Fact]
    public async Task StoreObservesCancellation() { var store = new InMemorySessionStore(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAsync("sid-1", cancellation.Token).AsTask()); }

    [Fact]
    public async Task SessionResolvesCurrentActorAndTouches() { var store = await Store(); var actor = await Resolver(store).ResolveAsync("sid-1", Tenant("tenant-a"), ["inspector"], Epoch.AddMinutes(2), TimeSpan.FromMinutes(30)); Assert.Equal("user-1", actor?.UserId); Assert.Equal("tenant-a", actor?.Tenant?.Id.Value); Assert.Equal(["inspector"], actor?.Roles); Assert.Equal(Epoch.AddMinutes(2), (await store.GetAsync("sid-1"))?.LastSeenUtc); }

    [Fact]
    public async Task UnknownSessionFailsClosed() { var actor = await Resolver(new InMemorySessionStore()).ResolveAsync("missing", Tenant("tenant-a"), [], Epoch, TimeSpan.FromMinutes(30)); Assert.Null(actor); }

    [Fact]
    public async Task CrossTenantSessionFailsClosedWithoutTouch() { var store = await Store(); var actor = await Resolver(store).ResolveAsync("sid-1", Tenant("tenant-b"), [], Epoch.AddMinutes(2), TimeSpan.FromMinutes(30)); Assert.Null(actor); Assert.Equal(Epoch, (await store.GetAsync("sid-1"))?.LastSeenUtc); }

    [Fact]
    public async Task ExpiredSessionIsRevoked() { var store = await Store(); var actor = await Resolver(store).ResolveAsync("sid-1", Tenant("tenant-a"), [], Epoch.AddHours(9), TimeSpan.FromMinutes(30)); Assert.Null(actor); Assert.Null(await store.GetAsync("sid-1")); }

    [Fact]
    public async Task UnresolvedTenantFailsClosed() { var store = await Store(); var actor = await Resolver(store).ResolveAsync("sid-1", new TenantContext(null), [], Epoch, TimeSpan.FromMinutes(30)); Assert.Null(actor); }

    [Fact]
    public async Task InactiveTenantFailsClosed() { var store = await Store(); var actor = await Resolver(store).ResolveAsync("sid-1", Tenant("tenant-a", TenantStatus.Suspended), [], Epoch, TimeSpan.FromMinutes(30)); Assert.Null(actor); }

    [Fact]
    public async Task SentinelTenantFailsClosed() { var store = await Store(); var actor = await Resolver(store).ResolveAsync("sid-1", Tenant(TenantId.System), [], Epoch, TimeSpan.FromMinutes(30)); Assert.Null(actor); }

    [Fact]
    public async Task EmptySessionFailsClosed() { var actor = await Resolver(new InMemorySessionStore()).ResolveAsync("", Tenant("tenant-a"), [], Epoch, TimeSpan.FromMinutes(30)); Assert.Null(actor); }

    [Fact]
    public void ExternalIdentityReasonIsAvailable() => Assert.Contains(SessionEstablishmentReason.ExternalIdentity, Enum.GetValues<SessionEstablishmentReason>());

    private static SessionRecord Record(double absoluteHours = 8) => new() { SessionId = "sid-1", UserId = "user-1", TenantId = new TenantId("tenant-a"), IssuedUtc = Epoch, AbsoluteExpiryUtc = Epoch.AddHours(absoluteHours), LastSeenUtc = Epoch, Reason = SessionEstablishmentReason.ExternalIdentity };

    private static async Task<InMemorySessionStore> Store() { var store = new InMemorySessionStore(); await store.CreateAsync(Record()); return store; }

    private static SessionResolver Resolver(ISessionStore store) => new(store);

    private static TenantContext Tenant(string id, TenantStatus status = TenantStatus.Active) => Tenant(new TenantId(id), status);

    private static TenantContext Tenant(TenantId id, TenantStatus status = TenantStatus.Active) => new(new TenantMetadata { Id = id, Name = id.ToString(), Status = status });

    private sealed record TenantContext(TenantMetadata? Tenant) : ITenantContext;
}
