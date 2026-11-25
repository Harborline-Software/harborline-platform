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
/// R-0229 / ADR-0110 open item 2: qualify the existing no-position handoff, not a production host fence.
/// Each store transition represents the outcome of the host's single fenced check/claim transaction.
/// The API must prove its actual database transaction separately. Oracles are literal states, audit bytes,
/// worker identity and effect counts, and the ruling's order property, independent of both audit clocks.
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
    public async Task FenceDecisionSurvivesBothClockSkewsAndRestart(bool journal, bool revocationFirst, int skewMinutes)
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
            var revocationAt = Now.AddMinutes(skewMinutes);
            var audit = new KernelAuditEvidence("fence-refusal", "sys.forms-worker", revocationAt,
                "{\"op\":\"accepted-command-refused\",\"code\":\"records.command.revoked_compromise\",\"after_claim\":false}"u8.ToArray())
                { AuthoritySnapshot = "host-fence-refusal"u8.ToArray() };
            if (!revocationFirst)
            {
                Assert.True((await commands.ClaimAcceptedCommandAsync(commandId, Worker, Now))!.Changed);
                Assert.False((await commands.RefuseAcceptedCommandAsync(commandId, FormAcceptedCommandState.Pending,
                    "records.command.revoked_compromise", audit))!.Changed);
            }
            (store as IDisposable)?.Dispose();
            store = Open();
            commands = (IFormAcceptedCommandStore)store;
            var retained = (await commands.GetAcceptedCommandAsync(Tenant, commandId))!;
            Assert.Equal(revocationFirst ? FormAcceptedCommandState.Pending : FormAcceptedCommandState.Claimed, retained.State);
            if (!revocationFirst) AssertClaimEvidence(retained);
            Assert.Equal(ExpectedAcceptanceSnapshot(), retained.AcceptanceSnapshot.ToArray());

            // A conforming host looks up the retained claim outcome. Current revocations are deliberately unavailable:
            // they cannot re-decide this command. This port has no clock comparison or synthetic order counter.
            var sink = new RetainedDecisionSink();
            // Keep the row pending until the consumer claims it. The fixture then returns the host's audited refusal.
            var refusingStore = new RefusingClaimStore(store, audit);
            var worker = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(new Decider()),
                recordTypePolicies: new FormOnlyReader(), projectionSink: sink, storeDecorator: _ => revocationFirst ? refusingStore : store,
                options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");
            await worker.Engine.RecoverProjectionsAsync(10);
            await worker.Engine.RecoverProjectionsAsync(10);
            Assert.Equal(revocationFirst ? 1 : 0, refusingStore.ClaimCalls);
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
                Assert.Equal(Now.AddMinutes(skewMinutes), refusalAudit.RecordedAt);
                Assert.Equal("{\"op\":\"accepted-command-refused\",\"code\":\"records.command.revoked_compromise\",\"after_claim\":false}"u8.ToArray(), refusalAudit.Payload.ToArray());
                Assert.Equal("host-fence-refusal"u8.ToArray(), refusalAudit.AuthoritySnapshot.ToArray());
                Assert.Equal("sys.forms-worker", settled.StateActorId);
                Assert.Equal(Now.AddMinutes(skewMinutes), settled.StateChangedAt);
            }
            else
            {
                AssertClaimEvidence(settled);
                Assert.Equal(ExpectedAcceptanceSnapshot(), Assert.Single(audits).AuthoritySnapshot.ToArray());
            }
            Assert.Equal(ExpectedAcceptanceSnapshot(), settled.AcceptanceSnapshot.ToArray());
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
        var sink = new RetainedDecisionSink();
        sink.DenyWorker = true;
        var worker = await Harness.CreateAsync(state: state, definitionFactory: Targeted, submitGates: Port(new Decider()),
            recordTypePolicies: new FormOnlyReader(), projectionSink: sink,
            options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");

        await worker.Engine.RecoverProjectionsAsync(10);

        Assert.Equal("sys.forms-worker", Assert.Single(sink.WorkerAttempts).WorkerId);
        Assert.Empty(sink.Records.Records);
        var claimed = Assert.Single(state.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Claimed, claimed.State);
        Assert.Equal("sys.forms-worker", claimed.StateActorId);
        Assert.Null(claimed.RefusalCode);
        AssertClaimEvidence(claimed);
        Assert.Empty(sink.Records.Executions);
        Assert.Single(state.Pending);

        sink.DenyWorker = false;
        await worker.Engine.RecoverProjectionsAsync(10);
        Assert.Single(sink.Records.Records);
        Assert.Equal(2, sink.WorkerAttempts.Count);
        Assert.Single(sink.Records.Executions);
        Assert.Equal(FormAcceptedCommandState.Completed, Assert.Single(state.Commands.Values).Command.State);
        Assert.Empty(state.Pending);
    }

    // Lifecycle fixture only: the API must prove the actual single-database revocation/check transaction.
    private sealed class RefusingClaimStore(IFormSubmissionTransactionStore inner, KernelAuditEvidence audit)
        : IFormSubmissionTransactionStore, IFormAcceptedCommandStore
    {
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
            return await Commands.RefuseAcceptedCommandAsync(commandId, FormAcceptedCommandState.Pending,
                "records.command.revoked_compromise", audit, cancellationToken);
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

    private sealed class RetainedDecisionSink : IFormProjectionSink, IFormAcceptedRecordBodies, IFormAcceptedCommandRevocations
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
            Assert.Equal(FormAcceptedCommandState.Claimed, query.Command.State);
            return ValueTask.FromResult<IReadOnlyList<FormAcceptedCommandRevocation>>([]);
        }
    }
}
