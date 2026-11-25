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
            if (revocationFirst)
            {
                Assert.True((await commands.RefuseAcceptedCommandAsync(commandId, FormAcceptedCommandState.Pending,
                    "records.command.revoked_compromise", audit))!.Changed);
                var refused = (await commands.ClaimAcceptedCommandAsync(commandId, Worker, Now))!;
                Assert.False(refused.Changed);
                Assert.Equal(FormAcceptedCommandState.Refused, refused.Command.State);
                Assert.Equal(revocationAt, refused.Command.StateChangedAt);
            }
            else
            {
                Assert.True((await commands.ClaimAcceptedCommandAsync(commandId, Worker, Now))!.Changed);
                // The late revocation cannot retroactively refuse a claimed command, even if its audit clock is earlier.
                Assert.False((await commands.RefuseAcceptedCommandAsync(commandId, FormAcceptedCommandState.Pending,
                    "records.command.revoked_compromise", audit))!.Changed);
            }
            var before = (await commands.GetAcceptedCommandAsync(Tenant, commandId))!;
            (store as IDisposable)?.Dispose();
            store = Open();
            commands = (IFormAcceptedCommandStore)store;
            var retained = (await commands.GetAcceptedCommandAsync(Tenant, commandId))!;
            Assert.Equal(revocationFirst ? FormAcceptedCommandState.Refused : FormAcceptedCommandState.Claimed, retained.State);
            Assert.Equal(before.StateActorId, retained.StateActorId);
            Assert.Equal(before.StateChangedAt, retained.StateChangedAt);
            Assert.Equal(Acceptance("alice", true, Now), retained.AcceptanceSnapshot.ToArray());
            Assert.Equal(before.AcceptanceAuditId, retained.AcceptanceAuditId);
            Assert.Equal(receipt.SubmittedAt, retained.AcceptedAt);

            // A conforming host looks up the retained claim outcome. Current revocations are deliberately unavailable:
            // they cannot re-decide this command. This port has no clock comparison or synthetic order counter.
            var sink = new RetainedDecisionSink();
            var worker = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(new Decider()),
                recordTypePolicies: new FormOnlyReader(), projectionSink: sink, storeDecorator: _ => store,
                options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");
            await worker.Engine.RecoverProjectionsAsync(10);
            await worker.Engine.RecoverProjectionsAsync(10);
            var settled = (await commands.GetAcceptedCommandAsync(Tenant, commandId))!;
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
    public async Task ValidFenceDecisionStillHonoursWorkerExecutionRefusal()
    {
        var state = new InMemoryFormSubmissionState();
        var submitter = await AcceptingAsync(state: state);
        using var body = JsonDocument.Parse("""{"name":"Ada"}""");
        await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, body, "worker-gate-op"));
        var sink = new RetainedDecisionSink();
        sink.Records.TerminalRefusal = "records.write.worker_denied";
        var worker = await Harness.CreateAsync(state: state, definitionFactory: Targeted, submitGates: Port(new Decider()),
            recordTypePolicies: new FormOnlyReader(), projectionSink: sink,
            options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");

        await worker.Engine.RecoverProjectionsAsync(10);

        Assert.Equal("sys.forms-worker", Assert.Single(sink.Records.Executions).WorkerId);
        Assert.Empty(sink.Records.Records);
        var refused = Assert.Single(state.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Refused, refused.State);
        Assert.Equal("records.write.worker_denied", refused.RefusalCode);
    }

    private sealed class RetainedDecisionSink : IFormProjectionSink, IFormAcceptedRecordBodies, IFormAcceptedCommandRevocations
    {
        internal RecordsSink Records { get; } = new();
        internal int Lookups { get; private set; }
        public bool ExecutesAcceptedCommands => true;
        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            Records.DeliverAsync(envelope, cancellationToken);
        public ValueTask<FormProjectionDeliveryResult> DeliverAcceptedAsync(FormAcceptedCommandDelivery delivery, CancellationToken cancellationToken = default) =>
            Records.DeliverAcceptedAsync(delivery, cancellationToken);
        public ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default) =>
            Records.DigestAsync(request, cancellationToken);
        public ValueTask<IReadOnlyList<FormAcceptedCommandRevocation>> FindAsync(FormAcceptedCommandRevocationQuery query, CancellationToken cancellationToken = default)
        {
            Assert.Equal(FormAcceptedCommandState.Claimed, query.Command.State);
            Lookups++;
            return ValueTask.FromResult<IReadOnlyList<FormAcceptedCommandRevocation>>([]);
        }
    }
}
