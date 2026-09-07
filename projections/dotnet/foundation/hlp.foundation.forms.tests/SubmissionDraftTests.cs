using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Drafts;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.MultiTenancy;

using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// Pins the D2 save-and-resume submission-draft doctrine (ADR 0135 amendment 2026-07-01):
/// a draft keyed by <c>(TenantId, case/subject id, PartyId)</c>, FAIL-CLOSED party
/// resolution that blocks (never mis-keys across tenants) on an unresolved principal, a
/// client-mintable case id, and the pre-auth capture buffer's promote-or-purge lifecycle.
/// Pure substrate-free unit tests over the real <c>PartyContext</c> + in-memory store/buffer.
/// </summary>
public sealed class SubmissionDraftTests
{
    private static readonly TenantId TenantX = new("tenant-x");
    private static readonly TenantId TenantY = new("tenant-y");
    private static readonly Guid PartyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PartyB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static SubmissionDraftProvenance Provenance() => new(
        SchemaRef: "cid-abc",
        DefinitionId: "equipment-inspection",
        DefinitionVersion: "1.0.0",
        EngineVersion: SubmissionDraftProvenance.HarborlineJsonLogicV1,
        LocaleChain: new[] { "en-US" });

    // Ticket 288 slice 2 renamed the engine identifier. The operator set did not change, so a draft
    // written before the rename records a language this engine still evaluates: it must be accepted on
    // read and preserved verbatim, while Create keeps stamping the current identifier by default.
    [Fact]
    public void Provenance_accepts_the_pre_rename_engine_identifier_and_writes_the_current_one()
    {
        var definition = new FormDefinition(
            new FormDefinitionId("equipment-inspection"),
            new SemanticVersion(1, 0, 0),
            FormDefinitionStatus.Published,
            TenantX,
            IdentityRef.System,
            new SchemaId("cid-abc"),
            new HarborlineOverlay(new Dictionary<string, FieldOverlay>(StringComparer.Ordinal), [], []),
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(
            SubmissionDraftProvenance.HarborlineJsonLogicV1,
            SubmissionDraftProvenance.Create(definition, ["en-US"]).EngineVersion);

        var legacy = SubmissionDraftProvenance.Create(
            definition, ["en-US"], SubmissionDraftProvenance.LegacyHarborlineJsonLogicV1);
        Assert.Equal(SubmissionDraftProvenance.LegacyHarborlineJsonLogicV1, legacy.EngineVersion);
        Assert.NotEqual(SubmissionDraftProvenance.HarborlineJsonLogicV1, legacy.EngineVersion);

        var roundTripped = JsonSerializer.Deserialize<SubmissionDraftProvenance>(JsonSerializer.Serialize(legacy))!;
        Assert.Equal(legacy.EngineVersion, roundTripped.EngineVersion);
        Assert.Equal(legacy.SchemaRef, roundTripped.SchemaRef);
        Assert.Equal(legacy.DefinitionId, roundTripped.DefinitionId);
        Assert.Equal(legacy.DefinitionVersion, roundTripped.DefinitionVersion);
        Assert.Equal(legacy.LocaleChain, roundTripped.LocaleChain);
    }

    private static ReadOnlyMemory<byte> Body(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>Builds a fail-closed draft service for a principal, seeding the (tenant, user)→party map.</summary>
    private static SubmissionDraftService Service(
        ISubmissionDraftStore store,
        TenantId tenant,
        string userId,
        params PrincipalPartyMapping[] mappings)
    {
        var principal = new FakeTenantContext(userId, tenant);
        var resolver = new TestPrincipalPartyResolver(mappings);
        return new SubmissionDraftService(new AuthenticatedFormsActorScope(principal, resolver), store, TimeProvider.System);
    }

    // ── DraftCaseId (client-mintable) ────────────────────────────────────────────

    [Fact]
    public void DraftCaseId_accepts_a_guid_and_a_ulid_but_rejects_junk()
    {
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000",
            DraftCaseId.Create("550e8400-e29b-41d4-a716-446655440000").Value);
        // 26-char Crockford ULID.
        Assert.Equal("01ARZ3NDEKTSV4RRFFQ69G5FAV",
            DraftCaseId.Create("01ARZ3NDEKTSV4RRFFQ69G5FAV").Value);

        Assert.Throws<ArgumentException>(() => DraftCaseId.Create(""));
        Assert.Throws<ArgumentException>(() => DraftCaseId.Create("not-a-valid-id"));
        // Contains a disallowed ULID letter (I/L/O/U) and is not a GUID.
        Assert.Throws<ArgumentException>(() => DraftCaseId.Create("IIARZ3NDEKTSV4RRFFQ69G5FAV"));
    }

    // ── Keyed by the tuple ───────────────────────────────────────────────────────

    [Fact]
    public async Task Save_then_resume_round_trips_by_the_tuple()
    {
        var store = new InMemorySubmissionDraftStore();
        var svc = Service(store, TenantX, "user-a", new PrincipalPartyMapping(TenantX, "user-a", PartyA));
        var caseId = DraftCaseId.NewId();

        var key = await svc.SaveDraftAsync(new FormDefinitionId("f1"), caseId, Provenance(), Body("{\"a\":1}"), subjectId: "subj-1");

        Assert.Equal(TenantX, key.Tenant);
        Assert.Equal(caseId, key.Case);
        Assert.Equal(PartyA, key.PartyId);

        var resumed = await svc.ResumeDraftAsync(new FormDefinitionId("f1"), caseId);
        Assert.NotNull(resumed);
        Assert.Equal(key, resumed!.Key);
        Assert.Equal("subj-1", resumed.SubjectId);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(resumed.Body.Span));
    }

    [Fact]
    public async Task Resave_preserves_created_at_and_advances_updated_at()
    {
        var store = new InMemorySubmissionDraftStore();
        var svc = Service(store, TenantX, "user-a", new PrincipalPartyMapping(TenantX, "user-a", PartyA));
        var caseId = DraftCaseId.NewId();

        await svc.SaveDraftAsync(new FormDefinitionId("f1"), caseId, Provenance(), Body("{\"a\":1}"));
        var first = await svc.ResumeDraftAsync(new FormDefinitionId("f1"), caseId);
        await Task.Delay(5);
        await svc.SaveDraftAsync(new FormDefinitionId("f1"), caseId, Provenance(), Body("{\"a\":2}"));
        var second = await svc.ResumeDraftAsync(new FormDefinitionId("f1"), caseId);

        Assert.Equal(first!.CreatedAt, second!.CreatedAt);
        Assert.True(second.UpdatedAt >= first.UpdatedAt);
        Assert.Equal("{\"a\":2}", Encoding.UTF8.GetString(second.Body.Span));
    }

    // ── FAIL-CLOSED: unresolved party blocks, never mis-keys ──────────────────────

    [Fact]
    public async Task Unresolved_party_blocks_save_and_resume_fail_closed()
    {
        var store = new InMemorySubmissionDraftStore();
        // No mapping for (TenantX, "ghost") → resolver returns null → facade throws.
        var svc = Service(store, TenantX, "ghost" /* no mappings */);
        var caseId = DraftCaseId.NewId();

        await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => svc.SaveDraftAsync(new FormDefinitionId("f1"), caseId, Provenance(), Body("{\"a\":1}")));
        await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => svc.ResumeDraftAsync(new FormDefinitionId("f1"), caseId));
        await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => svc.ListMyDraftsAsync());
    }

    [Fact]
    public async Task No_authenticated_principal_blocks_fail_closed()
    {
        var store = new InMemorySubmissionDraftStore();
        // Empty user + null tenant → PartyContext throws NoAuthenticatedPrincipal.
        var principal = new FakeTenantContext(userId: "", tenant: null);
        var svc = new SubmissionDraftService(
            new AuthenticatedFormsActorScope(principal, new TestPrincipalPartyResolver(Array.Empty<PrincipalPartyMapping>())),
            store, TimeProvider.System);

        await Assert.ThrowsAsync<PrincipalPartyResolutionException>(
            () => svc.SaveDraftAsync(new FormDefinitionId("f1"), DraftCaseId.NewId(), Provenance(), Body("{}")));
    }

    [Fact]
    public async Task A_draft_is_never_readable_across_tenant_or_party()
    {
        var store = new InMemorySubmissionDraftStore();
        var caseId = DraftCaseId.NewId();

        // Party A in tenant X saves a draft for the case.
        var svcAX = Service(store, TenantX, "user-a", new PrincipalPartyMapping(TenantX, "user-a", PartyA));
        await svcAX.SaveDraftAsync(new FormDefinitionId("f1"), caseId, Provenance(), Body("{\"secret\":true}"));

        // Same user id, DIFFERENT tenant (Y) → different party mapping → different key → not found.
        var svcAY = Service(store, TenantY, "user-a", new PrincipalPartyMapping(TenantY, "user-a", PartyB));
        Assert.Null(await svcAY.ResumeDraftAsync(new FormDefinitionId("f1"), caseId));

        // Different party (B) in the SAME tenant X → different key → not found.
        var svcBX = Service(store, TenantX, "user-b", new PrincipalPartyMapping(TenantX, "user-b", PartyB));
        Assert.Null(await svcBX.ResumeDraftAsync(new FormDefinitionId("f1"), caseId));

        // The owner still resumes it.
        Assert.NotNull(await svcAX.ResumeDraftAsync(new FormDefinitionId("f1"), caseId));
    }

    // ── Store round-trip + party-scoped list ──────────────────────────────────────

    [Fact]
    public async Task List_my_drafts_is_scoped_by_party_and_tenant()
    {
        var store = new InMemorySubmissionDraftStore();
        var svcAX = Service(store, TenantX, "user-a", new PrincipalPartyMapping(TenantX, "user-a", PartyA));
        var svcBX = Service(store, TenantX, "user-b", new PrincipalPartyMapping(TenantX, "user-b", PartyB));

        await svcAX.SaveDraftAsync(new FormDefinitionId("f1"), DraftCaseId.NewId(), Provenance(), Body("{}"));
        await svcAX.SaveDraftAsync(new FormDefinitionId("f2"), DraftCaseId.NewId(), Provenance(), Body("{}"));
        await svcBX.SaveDraftAsync(new FormDefinitionId("f1"), DraftCaseId.NewId(), Provenance(), Body("{}"));

        var mineA = await svcAX.ListMyDraftsAsync();
        var mineB = await svcBX.ListMyDraftsAsync();
        Assert.Equal(2, mineA.Count);
        Assert.Single(mineB);
        Assert.All(mineA, d => Assert.Equal(PartyA, d.Key.PartyId));
    }

    [Fact]
    public async Task Abandon_deletes_the_draft()
    {
        var store = new InMemorySubmissionDraftStore();
        var svc = Service(store, TenantX, "user-a", new PrincipalPartyMapping(TenantX, "user-a", PartyA));
        var caseId = DraftCaseId.NewId();

        await svc.SaveDraftAsync(new FormDefinitionId("f1"), caseId, Provenance(), Body("{}"));
        Assert.True(await svc.AbandonDraftAsync(new FormDefinitionId("f1"), caseId));
        Assert.Null(await svc.ResumeDraftAsync(new FormDefinitionId("f1"), caseId));
        Assert.False(await svc.AbandonDraftAsync(new FormDefinitionId("f1"), caseId));
    }

    // ── Pre-auth capture buffer: promote-or-purge ─────────────────────────────────

    [Fact]
    public async Task Capture_then_promote_on_identify_creates_keyed_drafts_and_clears_buffer()
    {
        var store = new InMemorySubmissionDraftStore();
        var buffer = new InMemoryPreAuthCaptureBuffer();
        var draftSvc = Service(store, TenantX, "user-a", new PrincipalPartyMapping(TenantX, "user-a", PartyA));
        var capture = new PreAuthCaptureService(buffer, draftSvc, TimeProvider.System);

        var session = new CaptureSessionId("anon-session-1");
        var caseId = DraftCaseId.NewId();
        await capture.CaptureAsync(session, new FormDefinitionId("f1"), caseId, Provenance(), Body("{\"draft\":1}"), subjectId: "subj-9");

        var keys = await capture.PromoteOnIdentifyAsync(session);

        Assert.Single(keys);
        Assert.Equal(new SubmissionDraftKey(TenantX, caseId, PartyA), keys[0]);
        // The promoted draft is now resumable as a keyed draft.
        var resumed = await draftSvc.ResumeDraftAsync(new FormDefinitionId("f1"), caseId);
        Assert.NotNull(resumed);
        Assert.Equal("subj-9", resumed!.SubjectId);
        // The buffer no longer holds it (promoted, not lingering).
        Assert.Empty(await buffer.ReadSessionAsync(session));
    }

    [Fact]
    public async Task Promote_while_still_unidentified_throws_and_keeps_captures()
    {
        var store = new InMemorySubmissionDraftStore();
        var buffer = new InMemoryPreAuthCaptureBuffer();
        // Unidentified: no party mapping for the principal → SaveDraftAsync fails closed.
        var draftSvc = Service(store, TenantX, "ghost" /* no mappings */);
        var capture = new PreAuthCaptureService(buffer, draftSvc, TimeProvider.System);

        var session = new CaptureSessionId("anon-session-2");
        await capture.CaptureAsync(session, new FormDefinitionId("f1"), DraftCaseId.NewId(), Provenance(), Body("{\"draft\":2}"));

        await Assert.ThrowsAsync<PrincipalPartyResolutionException>(() => capture.PromoteOnIdentifyAsync(session));
        // Nothing was promoted OR purged — the capture survives for a later identified promote.
        Assert.Single(await buffer.ReadSessionAsync(session));
    }

    [Fact]
    public async Task Discard_purges_captures_on_abandon()
    {
        var buffer = new InMemoryPreAuthCaptureBuffer();
        var draftSvc = Service(new InMemorySubmissionDraftStore(), TenantX, "user-a", new PrincipalPartyMapping(TenantX, "user-a", PartyA));
        var capture = new PreAuthCaptureService(buffer, draftSvc, TimeProvider.System);

        var session = new CaptureSessionId("anon-session-3");
        await capture.CaptureAsync(session, new FormDefinitionId("f1"), DraftCaseId.NewId(), Provenance(), Body("{}"));

        Assert.Equal(1, await capture.DiscardAsync(session));
        Assert.Empty(await buffer.ReadSessionAsync(session));
    }

    [Fact]
    public async Task Sweep_purges_expired_captures_so_no_pii_lingers()
    {
        var buffer = new InMemoryPreAuthCaptureBuffer();
        var now = DateTimeOffset.UtcNow;

        // One already-expired capture, one live.
        await buffer.CaptureAsync(new PreAuthCapture(
            new CaptureSessionId("s-old"), new FormDefinitionId("f1"), DraftCaseId.NewId(), Provenance(),
            Body("{}"), null, now.AddHours(-2), now.AddHours(-1)));
        await buffer.CaptureAsync(new PreAuthCapture(
            new CaptureSessionId("s-live"), new FormDefinitionId("f1"), DraftCaseId.NewId(), Provenance(),
            Body("{}"), null, now, now.AddHours(1)));

        var purged = await buffer.SweepExpiredAsync(now);

        Assert.Equal(1, purged);
        Assert.Empty(await buffer.ReadSessionAsync(new CaptureSessionId("s-old")));
        Assert.Single(await buffer.ReadSessionAsync(new CaptureSessionId("s-live")));
    }

    // ── Test double ───────────────────────────────────────────────────────────────

    /// <summary>Minimal Authorization sum-interface for the tests: a fixed operator + tenant (or none).</summary>
    private sealed class FakeTenantContext : IAuthenticatedActorContext
    {
        private readonly TenantMetadata? _tenant;

        public FakeTenantContext(string userId, TenantId? tenant)
        {
            UserId = userId;
            _tenant = tenant is { } t ? new TenantMetadata { Id = t, Name = t.Value } : null;
        }

        public string UserId { get; }
        public IReadOnlyList<string> Roles => Array.Empty<string>();
        public bool HasPermission(string permission) => false;
        public TenantMetadata? Tenant => _tenant;
    }

    private sealed record PrincipalPartyMapping(TenantId Tenant, string UserId, Guid PartyId);

    private sealed class TestPrincipalPartyResolver : IPrincipalPartyResolver
    {
        private readonly IReadOnlyList<PrincipalPartyMapping> _mappings;

        public TestPrincipalPartyResolver(IEnumerable<PrincipalPartyMapping> mappings)
            => _mappings = mappings.ToArray();

        public ValueTask<Guid?> ResolveAsync(
            string userId,
            TenantId tenantId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = _mappings.FirstOrDefault(candidate =>
                candidate.Tenant == tenantId &&
                string.Equals(candidate.UserId, userId, StringComparison.Ordinal));
            return ValueTask.FromResult<Guid?>(match?.PartyId);
        }
    }

}
