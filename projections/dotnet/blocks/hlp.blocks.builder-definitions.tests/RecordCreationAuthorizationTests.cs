using System.Text;
using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.Core;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// Decision D1, K9, plan W2/W9: the Records create is authorized on its Record Type's one governing creation policy.
/// The gate is asked once, for a typed target (the requested type), so the host's decider decides the type's creation
/// role; and the bound revision's own gate refuses a principal's direct create of a <c>form_only</c> or <c>engine</c> type
/// whatever the decider answered, so missing form metadata can never stand in for creation authorization. A form invoking
/// a role-gated create uses this same single decision and adds no creation gate.
/// The role-aware fixture decider stands in for the host adapter's documented contract (W10,
/// <see cref="RecordCreationAuthorization"/>): it reads the type's head policy through
/// <see cref="RecordTypeDefinitionStore.GetCreationPolicyAsync"/> and allows only a holder of a role gate's creation role.
/// The permissive gate stands in for a misconfigured host that allows everything. Every expected code, pointer and count
/// is a literal.
/// <para><b>What each test proves</b> (trait <c>Proves</c>): <c>platform</c> tests hold for any decider, because their
/// assertions rest only on Platform code (the request Platform sends, the bound-revision refusal, evidence binding,
/// replay). <c>w10-decider-contract-fixture</c> tests prove only that the fixture implements the stated contract: role
/// enforcement (L152) is not Platform evidence until a W10 adapter passes the same cases.</para>
/// </summary>
public sealed class RecordCreationAuthorizationTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private const string ValidBody = """{"asset_tag":"A-1","quantity":3,"notes":"spare"}""";

    [Fact]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "w10-decider-contract-fixture")]
    public async Task A_holder_of_the_creation_role_creates_through_one_typed_decision_and_no_second_gate()
    {
        var fixture = await Fixture.CreateAsync();
        var decider = fixture.RoleAware(("alice", "asset-author"));

        var outcome = await fixture.Operation(decider).CreateAsync(new(Tenant, "alice"), Request("eam.asset-class", "op-1"));

        Assert.True(outcome.Committed);
        Assert.False(outcome.Replayed);
        // D1: one decision for the create; no forms:submit-gate act and no creation-gate field is added for any origin.
        var asked = Assert.Single(decider.Asked);
        Assert.Equal("records:write", asked.Operation);
        Assert.Equal("record", asked.Record.Kind);
        Assert.Equal("eam.asset-class", asked.Record.RecordTypeId);
        Assert.Empty(asked.Record.Fields);
        var (row, audit) = Assert.Single(fixture.Port.Committed.Values);
        Assert.Equal("eam.asset-class", row.RecordTypeId);
        Assert.Equal("alice", row.ActorId);
        var snapshot = Encoding.UTF8.GetString(audit.AuthoritySnapshot.Span);
        Assert.Contains("\"verdict:allowed\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("\"deciding:role:asset-author\"", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "w10-decider-contract-fixture")]
    public async Task A_principal_without_the_creation_role_is_refused_at_the_gate_with_typed_evidence_and_nothing_staged()
    {
        var fixture = await Fixture.CreateAsync();
        var decider = fixture.RoleAware(("alice", "asset-author"));

        var outcome = await fixture.Operation(decider).CreateAsync(new(Tenant, "bob"), Request("eam.asset-class", "op-1"));

        Assert.Equal(("authorize", "NoEffectiveRole"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.False(outcome.Decision!.Allowed);
        Assert.Equal("eam.asset-class", outcome.Decision.RecordTypeId);
        Assert.Equal("bob", outcome.Decision.Principal);
        Assert.Equal(0, fixture.Port.Lookups);
        Assert.Equal(0, fixture.Port.Begun);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "w10-decider-contract-fixture")]
    public async Task The_creation_role_of_one_type_never_creates_another_type()
    {
        var fixture = await Fixture.CreateAsync();
        var decider = fixture.RoleAware(("alice", "asset-author"), ("carol", "meter-author"));
        var operation = fixture.Operation(decider);

        var aliceMeter = await operation.CreateAsync(new(Tenant, "alice"), Request("eam.meter", "op-1"));
        var carolAsset = await operation.CreateAsync(new(Tenant, "carol"), Request("eam.asset-class", "op-1"));
        var carolMeter = await operation.CreateAsync(new(Tenant, "carol"), Request("eam.meter", "op-2"));

        Assert.Equal(("authorize", "NoEffectiveRole"), (aliceMeter.Refusal!.Stage, aliceMeter.Refusal.Code));
        Assert.Equal("eam.meter", aliceMeter.Decision!.RecordTypeId);
        Assert.Equal(("authorize", "NoEffectiveRole"), (carolAsset.Refusal!.Stage, carolAsset.Refusal.Code));
        Assert.Equal("eam.asset-class", carolAsset.Decision!.RecordTypeId);
        Assert.True(carolMeter.Committed);
        Assert.Equal("eam.meter", carolMeter.Receipt!.RecordTypeId);
        Assert.Equal(["eam.meter", "eam.asset-class", "eam.meter"], decider.Asked.Select(request => request.Record.RecordTypeId));
        Assert.Single(fixture.Port.Committed);
    }

    public static TheoryData<string, string> NonPrincipalTypes => new()
    {
        { "eam.note", "records.write.form_only" },
        { "eam.ledger-line", "records.write.engine_only" },
    };

    [Theory]
    [MemberData(nameof(NonPrincipalTypes))]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "platform")]
    public async Task A_direct_create_of_a_form_only_or_engine_type_is_refused_even_when_the_gate_allows(string type, string code)
    {
        var fixture = await Fixture.CreateAsync();
        var gate = new PermissiveGate();

        // The create carries no form metadata at all; that never stands in for the type's creation gate (K9).
        var outcome = await fixture.Operation(gate).CreateAsync(new(Tenant, "alice"), Request(type, "op-1"));

        // F1: the refusing fact belongs to the bound revision, so it refuses at bind; the allowed decision returned beside it
        // is the authorize stage's own verdict, and no authorize-stage refusal is reported with an allowed decision.
        Assert.Equal(("bind", code), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Equal(["/record_type_id"], outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        // The gate's allow is returned for the host's trace plumbing; nothing was staged or committed.
        Assert.True(outcome.Decision!.Allowed);
        Assert.Equal(type, outcome.Decision.RecordTypeId);
        Assert.Equal(1, gate.Asked);
        Assert.Equal(1, fixture.Port.Lookups);
        Assert.Equal(0, fixture.Port.Begun);
        Assert.Empty(fixture.Port.Committed);
    }

    public static TheoryData<string> NonPrincipalTypeIds => ["eam.note", "eam.ledger-line"];

    [Theory]
    [MemberData(nameof(NonPrincipalTypeIds))]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "w10-decider-contract-fixture")]
    public async Task The_role_aware_decider_also_denies_a_direct_create_of_a_form_only_or_engine_type(string type)
    {
        var fixture = await Fixture.CreateAsync();
        var decider = fixture.RoleAware(("alice", "asset-author"), ("alice", "meter-author"));

        var outcome = await fixture.Operation(decider).CreateAsync(new(Tenant, "alice"), Request(type, "op-1"));

        Assert.Equal(("authorize", "NoEffectiveRole"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Equal(0, fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Committed);
    }

    [Fact]
    [Trait("Holds", "records-eng-6")]
    [Trait("Proves", "platform")]
    public async Task A_pin_to_a_form_only_version_is_refused_although_the_head_is_role_gated()
    {
        var fixture = await Fixture.CreateAsync();
        // eam.note 1.0.0 is form_only; 1.1.0 makes it role-gated on asset-author.
        var head = (await fixture.Host.Records.GetPublishedHeadAsync(Tenant, "eam.note"))!;
        await fixture.Host.Records.SaveDraftAsync("eam.note", head with { CreationGate = RecordCreationGate.Role, CreationRoleId = "asset-author" },
            "1.1.0", 2, "note-1.1.0");
        await fixture.Host.Records.PublishAsync(Tenant, "eam.note", "1.1.0", 3, "note-publish-1.1.0");
        var operation = fixture.Operation(fixture.RoleAware(("alice", "asset-author")));

        var pinned = await operation.CreateAsync(new(Tenant, "alice"), Request("eam.note", "op-pinned", "1.0.0"));
        var unpinned = await operation.CreateAsync(new(Tenant, "alice"), Request("eam.note", "op-head"));

        Assert.Equal(("bind", "records.write.form_only"), (pinned.Refusal!.Stage, pinned.Refusal.Code));
        Assert.True(unpinned.Committed);
        Assert.Equal("1.1.0", unpinned.Receipt!.Version);
        Assert.Single(fixture.Port.Committed);
    }

    [Fact]
    [Trait("Holds", "records-eng-6")]
    [Trait("Proves", "w10-decider-contract-fixture")]
    public async Task A_pin_to_an_older_role_version_cannot_widen_a_newer_form_only_head()
    {
        var fixture = await Fixture.CreateAsync();
        var head = (await fixture.Host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"))!;
        await fixture.Host.Records.SaveDraftAsync("eam.asset-class", head with { CreationGate = null, CreationRoleId = null },
            "1.1.0", 2, "asset-1.1.0");
        await fixture.Host.Records.PublishAsync(Tenant, "eam.asset-class", "1.1.0", 3, "asset-publish-1.1.0");

        var pinned = await fixture.Operation(fixture.RoleAware(("alice", "asset-author")))
            .CreateAsync(new(Tenant, "alice"), Request("eam.asset-class", "op-pinned", "1.0.0"));

        // The decider decides on the newest publication's policy (form_only), so the older role pin is denied.
        Assert.Equal(("authorize", "NoEffectiveRole"), (pinned.Refusal!.Stage, pinned.Refusal.Code));
        Assert.Empty(fixture.Port.Committed);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "platform")]
    public async Task Evidence_decided_for_another_record_type_is_withheld_and_refuses()
    {
        var fixture = await Fixture.CreateAsync();
        var gate = new PermissiveGate(request => request with { Record = request.Record with { RecordTypeId = "eam.meter" } });

        var outcome = await fixture.Operation(gate).CreateAsync(new(Tenant, "alice"), Request("eam.asset-class", "op-1"));

        Assert.Equal(("authorize", "access.decision_mismatch"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Null(outcome.Decision);
        Assert.Equal(0, fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Committed);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "platform")]
    public async Task A_retry_is_decided_again_on_the_same_typed_target_and_replays_without_a_second_record()
    {
        var fixture = await Fixture.CreateAsync();
        var decider = fixture.RoleAware(("alice", "asset-author"));
        var operation = fixture.Operation(decider);

        var created = await operation.CreateAsync(new(Tenant, "alice"), Request("eam.asset-class", "op-1"));
        var replay = await operation.CreateAsync(new(Tenant, "alice"), Request("eam.asset-class", "op-1"));

        Assert.True(created.Committed);
        Assert.True(replay.Replayed);
        Assert.Equal(created.Receipt, replay.Receipt);
        Assert.Equal(2, decider.Asked.Count);
        Assert.All(decider.Asked, request => Assert.Equal("eam.asset-class", request.Record.RecordTypeId));
        Assert.Single(fixture.Port.Committed);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    [Trait("Proves", "platform")]
    public async Task Platform_sends_the_decider_one_typed_request_with_no_pin_role_or_form_even_for_a_pinned_create()
    {
        var fixture = await Fixture.CreateAsync();
        await PublishRoleRevisionAsync(fixture, "eam.asset-class", "meter-author");
        var gate = new RecordingPermissiveGate();

        var pinned = await fixture.Operation(gate).CreateAsync(new(Tenant, "alice"), Request("eam.asset-class", "op-pinned", "1.0.0"));

        // The decider sees the type only: the same request whatever version is pinned, so it can decide only on the type's
        // governing (head) policy, as RecordCreationAuthorization states.
        Assert.True(pinned.Committed);
        Assert.Equal("1.0.0", pinned.Receipt!.Version);
        var asked = Assert.Single(gate.Asked);
        Assert.Equal(RecordCreationAuthorization.Act, asked.Operation);
        Assert.Equal("records:write", asked.Operation);
        Assert.Equal("alice", asked.Principal);
        Assert.Equal(Tenant, asked.Tenant);
        Assert.Equal(Tenant, asked.Record.Tenant);
        Assert.Equal("record", asked.Record.Kind);
        Assert.Equal(RecordCreationAuthorization.RecordKind, asked.Record.Kind);
        Assert.Equal("eam.asset-class", asked.Record.RecordTypeId);
        Assert.Empty(asked.Record.Fields);
        Assert.Equal(At, asked.At);
        Assert.Equal(pinned.Receipt.RecordId, asked.Record.Id);
    }

    [Fact]
    [Trait("Holds", "records-eng-6")]
    [Trait("Proves", "w10-decider-contract-fixture")]
    public async Task A_pin_to_an_older_role_version_under_a_newer_role_head_is_decided_on_the_heads_role()
    {
        var fixture = await Fixture.CreateAsync();
        // eam.asset-class 1.0.0 is role-gated on asset-author; 1.1.0 moves it to meter-author.
        await PublishRoleRevisionAsync(fixture, "eam.asset-class", "meter-author");
        var operation = fixture.Operation(fixture.RoleAware(("alice", "asset-author"), ("carol", "meter-author")));

        var olderRoleHolder = await operation.CreateAsync(new(Tenant, "alice"), Request("eam.asset-class", "op-alice", "1.0.0"));
        var headRoleHolder = await operation.CreateAsync(new(Tenant, "carol"), Request("eam.asset-class", "op-carol", "1.0.0"));

        // The accepted lead convention (E4): the newest publication's role governs every create of the type, including one
        // bound to an older version.
        Assert.Equal(("authorize", "NoEffectiveRole"), (olderRoleHolder.Refusal!.Stage, olderRoleHolder.Refusal.Code));
        Assert.True(headRoleHolder.Committed);
        Assert.Equal("1.0.0", headRoleHolder.Receipt!.Version);
        Assert.Single(fixture.Port.Committed);
    }

    // Publishes version 1.1.0 of an already published 1.0.0 type, role-gated on the given role.
    private static async Task PublishRoleRevisionAsync(Fixture fixture, string type, string role)
    {
        var head = (await fixture.Host.Records.GetPublishedHeadAsync(Tenant, type))!;
        await fixture.Host.Records.SaveDraftAsync(type, head with { CreationGate = RecordCreationGate.Role, CreationRoleId = role },
            "1.1.0", 2, type + "-1.1.0");
        await fixture.Host.Records.PublishAsync(Tenant, type, "1.1.0", 3, type + "-publish-1.1.0");
    }

    private static RecordCreateRequest Request(string type, string operationId, string? version = null)
        => new(type, operationId, JsonNode.Parse(ValidBody)!.AsObject(), version);

    // The test host with four published types: two role-gated on different roles, one form_only and one engine.
    private sealed class Fixture
    {
        private Fixture(TestHost host) => Host = host;

        public TestHost Host { get; }
        public CommittingPort Port { get; } = new();

        public static async Task<Fixture> CreateAsync()
        {
            var host = RecordTypeDefinitionStoreTests.Host();
            await PublishAsync(host, RoleGatedAssetClass("1.0.0"), "eam.asset-class");
            await PublishAsync(host, AssetClass("1.0.0") with { Name = "Meter", CreationGate = RecordCreationGate.Role, CreationRoleId = "meter-author" }, "eam.meter");
            await PublishAsync(host, AssetClass("1.0.0") with { Name = "Note" }, "eam.note");
            await PublishAsync(host, AssetClass("1.0.0") with { Name = "Ledger Line", CreationGate = RecordCreationGate.Engine }, "eam.ledger-line");
            return new(host);
        }

        public RoleAwareDecider RoleAware(params (string Principal, string Role)[] held) => new(Host.Records, held);

        public RecordWriteOperation Operation(IAuthorizationDecider decider)
            => new(new AccessProvider(decider), Host.Records, Host.Registry, Port, Port, new FixedClock(At));

        private static async Task PublishAsync(TestHost host, NewRecordType type, string expectedId)
        {
            var draft = await host.Records.CreateDraftAsync(type, "create-" + expectedId);
            Assert.Equal(expectedId, draft.RecordTypeId);
            await host.Records.PublishAsync(Tenant, expectedId, "1.0.0", 1, "publish-" + expectedId);
        }
    }

    // The host adapter's documented contract for a typed records:write create, as a fixture: read the type's head creation
    // policy and allow only a holder of a role gate's creation role. A real adapter forwards to the host's existing gate.
    private sealed class RoleAwareDecider(RecordTypeDefinitionStore records, (string Principal, string Role)[] held) : IAuthorizationDecider
    {
        public List<AccessRequest> Asked { get; } = [];

        public async ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Asked.Add(request);
            var policy = request.Record.RecordTypeId is { } type
                ? await records.GetCreationPolicyAsync(request.Tenant, type, cancellationToken: cancellationToken)
                : null;
            var allowed = policy is { Gate: RecordCreationGate.Role, RoleId: { } role }
                && held.Contains((request.Principal, role));
            return new AuthorizationDecisionEvidence(request, allowed, allowed ? "None" : "NoEffectiveRole",
                allowed ? "role:" + policy!.RoleId : "none", [], []);
        }
    }

    // A misconfigured host that allows every request; optionally it answers for another request than it was asked.
    private sealed class PermissiveGate(Func<AccessRequest, AccessRequest>? answerFor = null) : IAuthorizationDecider
    {
        public int Asked { get; private set; }

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Asked++;
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(answerFor?.Invoke(request) ?? request, true, "None", "grant:records-writer", [], []));
        }
    }

    // A permissive gate that records every request it is asked.
    private sealed class RecordingPermissiveGate : IAuthorizationDecider
    {
        public List<AccessRequest> Asked { get; } = [];

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Asked.Add(request);
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, true, "None", "grant:records-writer", [], []));
        }
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    // A host transaction that keys committed operations by the staged identity and reads them back from the same state.
    private sealed class CommittingPort : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        private readonly Dictionary<string, RecordWriteCommittedOperation> _operations = new(StringComparer.Ordinal);

        public Dictionary<string, (RecordWriteRow Row, KernelAuditEvidence Audit)> Committed { get; } = new(StringComparer.Ordinal);
        public int Lookups { get; private set; }
        public int Begun { get; private set; }

        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return ValueTask.FromResult(_operations.GetValueOrDefault(key.IdempotencyKey));
        }

        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Begun++;
            return ValueTask.FromResult<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>>(new Transaction(this));
        }

        private sealed class Transaction(CommittingPort owner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            private KernelOperationIdentity? _operation;
            private RecordWriteRow? _row;
            private KernelAuditEvidence? _audit;

            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
            {
                _operation = operation;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageRecordAsync(RecordWriteRow record, CancellationToken cancellationToken = default)
            {
                _row = record;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            {
                _audit = audit;
                return ValueTask.CompletedTask;
            }

            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                var operation = _operation!;
                if (owner._operations.TryGetValue(operation.IdempotencyKey, out var prior))
                    return ValueTask.FromResult(new RecordWriteCommit(prior.IntentDigest == operation.Fingerprint
                        ? RecordWriteCommitDisposition.Replayed : RecordWriteCommitDisposition.Conflict, prior.Receipt) { Stored = prior });
                var row = _row!;
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                    row.DefinitionDigest, row.Schema, row.RecordedAt);
                owner._operations.Add(operation.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                owner.Committed.Add(row.RecordId, (row, _audit!));
                return ValueTask.FromResult(new RecordWriteCommit(RecordWriteCommitDisposition.Created, receipt));
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
