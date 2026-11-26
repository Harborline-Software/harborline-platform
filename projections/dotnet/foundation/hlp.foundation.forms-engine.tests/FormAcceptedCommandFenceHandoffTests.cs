using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Kernel.Core;
using Xunit;
using static Harborline.Foundation.Forms.Engine.Tests.AcceptedCommandFixtures;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// T-616 handoff acceptance 1-2, ADR-0110 open items 2/6: a serial host fixture commits revocation and claim
/// in both orders, retains applicability independently of the two audit clocks, then reopens before recovery.
/// Literal decisions, lifecycle states, authority bytes and effect counts are the oracles. The built-in stores
/// retain lifecycle evidence; the fixture journal retains the host decision. This models the public handoff,
/// not the API's single-database transaction or crash atomicity, which remain T-619's qualification.
/// </summary>
public sealed class FormAcceptedCommandFenceHandoffTests
{
    private static readonly TenantId Tenant = new("tenant-engine");

    [Theory]
    [InlineData(false, true, -60)]
    [InlineData(false, true, 60)]
    [InlineData(false, false, -60)]
    [InlineData(false, false, 60)]
    [InlineData(true, true, -60)]
    [InlineData(true, true, 60)]
    [InlineData(true, false, -60)]
    [InlineData(true, false, 60)]
    public async Task FencedHostRetainsApplicabilityAcrossCommitOrdersClockSkewsAndRestart(bool journal, bool revocationFirst, int auditSkewMinutes)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"forms-fence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var state = new InMemoryFormSubmissionState();
        IFormSubmissionTransactionStore Open() => journal
            ? new FileJournalFormSubmissionStore(new() { JournalPath = Path.Combine(directory, "submission.hlfj") })
            : new InMemoryFormSubmissionStore(state);
        IFormSubmissionTransactionStore store = Open();
        try
        {
            var submitter = await AcceptingAsync(storeDecorator: _ => store);
            using var body = JsonDocument.Parse("""{"name":"Ada"}""");
            var receipt = await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, body, "fence-op"));
            var leased = Assert.Single(await store.LeasePendingAsync(1));
            var commandId = leased.AcceptedCommand!.CommandId;
            await store.ReleaseProjectionLeaseAsync(leased.OutboxId);
            var commands = (IFormAcceptedCommandStore)store;
            var revocationAt = Now.AddMinutes(auditSkewMinutes);
            var audit = new KernelAuditEvidence("fence-refusal", "sys.forms-worker", Now,
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    op = "accepted-command-refused",
                    command_id = leased.OutboxId,
                    operation_id = receipt.InstanceId.ToString(),
                    record_type_id = "notes.entry",
                    code = "records.command.revoked_compromise",
                    after_claim = false,
                }))
                { AuthoritySnapshot = "host-fence-refusal"u8.ToArray() };
            var hostPath = Path.Combine(directory, "host-fence.jsonl");
            var host = new FencedHostStore(store, hostPath, audit);
            if (revocationFirst) host.CommitRevocation(commandId, revocationAt);
            // Interrupt before any Records effect; the claim decision must already be retained.
            var firstSink = new ContractSink(host) { DenyWorker = true };
            var firstWorker = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(new Decider()),
                recordTypePolicies: new FormOnlyReader(), projectionSink: firstSink, storeDecorator: _ => host,
                options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");
            await firstWorker.Engine.RecoverProjectionsAsync(10);
            if (!revocationFirst) host.CommitRevocation(commandId, revocationAt);
            Assert.Equal(1, host.ClaimCalls);
            Assert.Empty(firstSink.Records.Executions);
            Assert.Equal(revocationFirst ? 0 : 1, firstSink.WorkerAttempts.Count);
            Assert.Equal(revocationFirst ? 0 : 1, firstSink.Lookups);
            AssertFenceDecision(host, commandId, revocationFirst, auditSkewMinutes);

            (store as IDisposable)?.Dispose();
            store = Open();
            commands = (IFormAcceptedCommandStore)store;
            host = new FencedHostStore(store, hostPath, audit);
            AssertFenceDecision(host, commandId, revocationFirst, auditSkewMinutes);
            var retained = (await commands.GetAcceptedCommandAsync(Tenant, commandId))!;
            Assert.Equal(revocationFirst ? FormAcceptedCommandState.Refused : FormAcceptedCommandState.Claimed, retained.State);
            if (!revocationFirst) AssertClaimEvidence(retained);
            Assert.Equal(ExpectedAcceptanceSnapshot(), retained.AcceptanceSnapshot.ToArray());

            var sink = new ContractSink(host);
            var worker = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(new Decider()),
                recordTypePolicies: new FormOnlyReader(), projectionSink: sink, storeDecorator: _ => host,
                options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");
            await worker.Engine.RecoverProjectionsAsync(10);
            await worker.Engine.RecoverProjectionsAsync(10);
            // Consumer oracle: literal claim/execution counts; T-616 acceptance 1 requires honouring terminal refusal.
            Assert.Equal(revocationFirst ? 0 : 1, host.ClaimCalls);
            AssertFenceDecision(host, commandId, revocationFirst, auditSkewMinutes);
            Assert.Equal(revocationFirst ? 0 : 1, sink.Records.Executions.Count);
            (store as IDisposable)?.Dispose();
            store = Open();
            commands = (IFormAcceptedCommandStore)store;
            var settled = (await commands.GetAcceptedCommandAsync(Tenant, commandId))!;
            var audits = store is FileJournalFormSubmissionStore durable
                ? await durable.AuditsAsync()
                : state.Audits.Values.ToArray();
            if (revocationFirst)
            {
                var refusalAudit = Assert.Single(audits, entry => entry.AuditId == "fence-refusal");
                Assert.Equal("sys.forms-worker", refusalAudit.ActorId);
                Assert.Equal(Now, refusalAudit.RecordedAt);
                // Frozen interface revision 16, M1: all six lifecycle-audit members, including false after_claim.
                // Literal member names/values come from the contract; identity binding uses the accepted receipt/outbox.
                using var refusalPayload = JsonDocument.Parse(refusalAudit.Payload);
                var payload = refusalPayload.RootElement;
                Assert.Equal(6, payload.EnumerateObject().Count());
                Assert.Equal("accepted-command-refused", payload.GetProperty("op").GetString());
                Assert.Equal(leased.OutboxId, payload.GetProperty("command_id").GetString());
                Assert.Equal(receipt.InstanceId.ToString(), payload.GetProperty("operation_id").GetString());
                Assert.Equal("notes.entry", payload.GetProperty("record_type_id").GetString());
                Assert.Equal("records.command.revoked_compromise", payload.GetProperty("code").GetString());
                Assert.False(payload.GetProperty("after_claim").GetBoolean());
                Assert.Equal("host-fence-refusal"u8.ToArray(), refusalAudit.AuthoritySnapshot.ToArray());
                Assert.Equal("sys.forms-worker", settled.StateActorId);
                Assert.Equal(Now, settled.StateChangedAt);
            }
            else
            {
                AssertClaimEvidence(settled);
                // ADR-0110 items 2/6: a retained valid claim completes despite the later revocation.
                Assert.Equal(FormAcceptedCommandState.Completed, settled.State);
                Assert.Null(settled.RefusalCode); // No revocation quarantine, irrespective of either audit clock.
                Assert.Single(sink.Records.Records);
                Assert.Equal(ExpectedAcceptanceSnapshot(), Assert.Single(audits).AuthoritySnapshot.ToArray());
            }
            Assert.Equal(ExpectedAcceptanceSnapshot(), settled.AcceptanceSnapshot.ToArray());
            // Consumer oracles: literal terminal states, zero/one effects and lookup counts, worker id/refusal code.
            // ADR-0110 open item 2: commit order decides; either audit clock direction gives the same result.
            Assert.Equal(revocationFirst ? FormAcceptedCommandState.Refused : FormAcceptedCommandState.Completed, settled.State);
            Assert.Equal(revocationFirst ? 0 : 1, sink.Records.Records.Count);
            Assert.Equal(revocationFirst ? 0 : 1, sink.Lookups);
            if (!revocationFirst)
                Assert.Equal("sys.forms-worker", Assert.Single(sink.Records.Executions).WorkerId);
            else
                Assert.Equal("records.command.revoked_compromise", settled.RefusalCode);
        }
        finally
        {
            (store as IDisposable)?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WorkerAuthorizationDenialRetainsClaimAndRetries()
    {
        var state = new InMemoryFormSubmissionState();
        var submitter = await AcceptingAsync(state: state);
        using var body = JsonDocument.Parse("""{"name":"Ada"}""");
        await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, body, "worker-gate-op"));
        var sink = new ContractSink();
        sink.DenyWorker = true;
        var worker = await Harness.CreateAsync(state: state, definitionFactory: Targeted, submitGates: Port(new Decider()),
            recordTypePolicies: new FormOnlyReader(), projectionSink: sink,
            options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");

        await worker.Engine.RecoverProjectionsAsync(10);

        // Consumer oracle: literal worker identity, no effects, Claimed with no refusal and one pending row.
        // The public sink contract throws worker authorization failures, keeping the command retryable.
        Assert.Equal("sys.forms-worker", Assert.Single(sink.WorkerAttempts).WorkerId);
        Assert.Empty(sink.Records.Records);
        var claimed = Assert.Single(state.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Claimed, claimed.State);
        Assert.Equal("sys.forms-worker", claimed.StateActorId);
        Assert.Null(claimed.RefusalCode);
        AssertClaimEvidence(claimed);
        Assert.Empty(sink.Records.Executions);
        Assert.Single(state.Pending);

        // Consumer oracle: literal one effect, two attempts, Completed and no pending row after retry.
        sink.DenyWorker = false;
        await worker.Engine.RecoverProjectionsAsync(10);
        Assert.Single(sink.Records.Records);
        Assert.Equal(2, sink.WorkerAttempts.Count);
        Assert.Single(sink.Records.Executions);
        Assert.Equal(FormAcceptedCommandState.Completed, Assert.Single(state.Commands.Values).Command.State);
        Assert.Empty(state.Pending);
    }

    private static void AssertFenceDecision(FencedHostStore host, string commandId, bool revocationFirst, int skewMinutes)
    {
        // Independent scenario oracle: ADR-0110 open items 2/6 and T-616 handoff acceptance 1-2.
        Assert.Equal(revocationFirst ? ["revocation", "claim"] : new[] { "claim", "revocation" },
            host.Events.Select(entry => entry.Kind).ToArray());
        Assert.All(host.Events, entry => Assert.Equal(commandId, entry.CommandId));
        Assert.Equal(Now.AddMinutes(skewMinutes), Assert.Single(host.Events, entry => entry.Kind == "revocation").AuditAt);
        var decision = Assert.Single(host.Events, entry => entry.Kind == "claim");
        Assert.Equal(Now, decision.AuditAt);
        Assert.Equal(revocationFirst, decision.RevocationApplies);
        Assert.Equal(revocationFirst ? "host-fence-refusal"u8.ToArray() : ExpectedAcceptanceSnapshot(), decision.Evidence);
    }

    private sealed record FenceEvent(string Kind, string CommandId, DateTimeOffset AuditAt, bool? RevocationApplies, byte[] Evidence);

    // Serial commits model one host fence; no concurrent writers or multi-store crash atomicity are claimed here.
    private sealed class FencedHostStore(IFormSubmissionTransactionStore inner, string path, KernelAuditEvidence audit)
        : IFormSubmissionTransactionStore, IFormAcceptedCommandStore, IFormAcceptedCommandRevocations
    {
        internal List<FenceEvent> Events { get; } = File.Exists(path)
            ? File.ReadAllLines(path).Select(line => JsonSerializer.Deserialize<FenceEvent>(line)!).ToList()
            : [];
        private void Commit(FenceEvent entry)
        {
            File.AppendAllText(path, JsonSerializer.Serialize(entry) + "\n");
            Events.Add(entry);
        }
        internal void CommitRevocation(string commandId, DateTimeOffset at) =>
            Commit(new("revocation", commandId, at, null, []));
        public ValueTask<IReadOnlyList<FormAcceptedCommandRevocation>> FindAsync(FormAcceptedCommandRevocationQuery query, CancellationToken cancellationToken = default)
        {
            var decision = Events.Single(entry => entry.Kind == "claim" && entry.CommandId == query.Command.CommandId);
            return ValueTask.FromResult<IReadOnlyList<FormAcceptedCommandRevocation>>(decision.RevocationApplies == true
                ? [new(FormAcceptedCommandRevocationReason.Compromise,
                    Events.Single(entry => entry.Kind == "revocation" && entry.CommandId == query.Command.CommandId).AuditAt)]
                : []);
        }
        private IFormAcceptedCommandStore Commands => (IFormAcceptedCommandStore)inner;
        internal int ClaimCalls { get; private set; }
        public ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);
        public ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default) => inner.CommitAsync(commit, cancellationToken);
        public ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default) => inner.GetAsync(tenant, instanceId, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, FormProjectionLeaseKinds kinds, CancellationToken cancellationToken = default) => Commands.LeasePendingAsync(maximum, kinds, cancellationToken);
        public ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default) => inner.ReleaseProjectionLeaseAsync(outboxId, cancellationToken);
        public ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default) => inner.CompleteProjectionAsync(outboxId, skips, cancellationToken);
        public ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default) => inner.RetryProjectionAsync(outboxId, stableErrorCode, cancellationToken);
        public ValueTask<FormAcceptedRecordCommand?> GetAcceptedCommandAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default) => Commands.GetAcceptedCommandAsync(tenant, commandId, cancellationToken);
        public async ValueTask<FormAcceptedCommandTransition?> ClaimAcceptedCommandAsync(string commandId, string workerId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            ClaimCalls++;
            Assert.Equal("sys.forms-worker", workerId);
            if (Events.Any(entry => entry.Kind == "claim" && entry.CommandId == commandId))
                return await Commands.ClaimAcceptedCommandAsync(commandId, workerId, at, cancellationToken);
            var applies = Events.Any(entry => entry.Kind == "revocation" && entry.CommandId == commandId);
            var result = applies
                ? await Commands.RefuseAcceptedCommandAsync(commandId, FormAcceptedCommandState.Pending,
                    "records.command.revoked_compromise", audit, cancellationToken)
                : await Commands.ClaimAcceptedCommandAsync(commandId, workerId, at, cancellationToken);
            var command = result!.Command;
            Commit(new("claim", commandId, at, applies,
                applies ? audit.AuthoritySnapshot.ToArray() : command.AcceptanceSnapshot.ToArray()));
            return result;
        }
        public ValueTask<FormAcceptedCommandTransition?> CancelAcceptedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.CancelAcceptedCommandAsync(tenant, commandId, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RefuseAcceptedCommandAsync(string commandId, FormAcceptedCommandState expectedState, string refusalCode, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.RefuseAcceptedCommandAsync(commandId, expectedState, refusalCode, audit, cancellationToken);
        public ValueTask<FormProjectionEnvelope?> LeasePendingProjectionAsync(TenantId tenant, string outboxId, CancellationToken cancellationToken = default) => Commands.LeasePendingProjectionAsync(tenant, outboxId, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RecordBindingFailureAsync(string commandId, string code, CancellationToken cancellationToken = default) => Commands.RecordBindingFailureAsync(commandId, code, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> QuarantineAcceptedCommandAsync(string commandId, string code, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.QuarantineAcceptedCommandAsync(commandId, code, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> AbandonQuarantinedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.AbandonQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> ResolveQuarantinedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.ResolveQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);

    }

    private static void AssertClaimEvidence(FormAcceptedRecordCommand command)
    {
        Assert.Equal("sys.forms-worker", command.StateActorId);
        Assert.Equal(DateTimeOffset.Parse("2026-08-08T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), command.StateChangedAt);
        Assert.Equal(ExpectedAcceptanceSnapshot(), command.AcceptanceSnapshot.ToArray());
        Assert.False(string.IsNullOrWhiteSpace(command.AcceptanceAuditId));
    }

    // Literal grammar/decision oracle (ADR-0068, AuthoritySnapshotEncoding's public grammar), not Encode output.
    private static byte[] ExpectedAcceptanceSnapshot() =>
        """{"tenant":"tenant-engine","principal":"alice","version":2,"steps":[{"ordinal":1,"stage":"act","facts":["kind:Gate","act:forms:submit-gate","target:form/inspection@1.0.0","principal:alice","tenant:tenant-engine","at:2026-08-08T12:00:00.0000000\u002B00:00"]},{"ordinal":2,"stage":"effective-roles","facts":["roles:none","deciding:grant:fixture@1"]},{"ordinal":3,"stage":"standings","facts":["standings:none"]},{"ordinal":4,"stage":"verdict","facts":["verdict:allowed","refusal:None","version:2"]}]}"""u8.ToArray();

    private sealed class ContractSink(FencedHostStore? host = null) : IFormProjectionSink, IFormAcceptedRecordBodies, IFormAcceptedCommandRevocations
    {
        internal RecordsSink Records { get; } = new();
        internal int Lookups { get; private set; }
        internal bool DenyWorker { get; set; }
        internal List<FormAcceptedCommandDelivery> WorkerAttempts { get; } = [];
        public bool ExecutesAcceptedCommands => true;
        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            Records.DeliverAsync(envelope, cancellationToken);
        public ValueTask<FormProjectionDeliveryResult> DeliverAcceptedAsync(FormAcceptedCommandDelivery delivery, CancellationToken cancellationToken = default)
        {
            WorkerAttempts.Add(delivery);
            if (DenyWorker) throw new UnauthorizedAccessException("Worker authorization denied.");
            return Records.DeliverAcceptedAsync(delivery, cancellationToken);
        }
        public ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default) =>
            Records.DigestAsync(request, cancellationToken);
        public ValueTask<IReadOnlyList<FormAcceptedCommandRevocation>> FindAsync(FormAcceptedCommandRevocationQuery query, CancellationToken cancellationToken = default)
        {
            Lookups++;
            // Consumer callback oracle: the query contract requires the literal Claimed lifecycle state.
            Assert.Equal(FormAcceptedCommandState.Claimed, query.Command.State);
            // The fence test reads the reopened host decision; the worker-gate-only test supplies an empty lookup.
            return host?.FindAsync(query, cancellationToken)
                ?? ValueTask.FromResult<IReadOnlyList<FormAcceptedCommandRevocation>>([]);
        }
    }
}
