using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

/// <summary>
/// Revision 8 (decision D6, ADR-0110 decision 4): the audit evidence carries the originator's provenance as an envelope
/// field beside the actual executor. It travels in the one audit staging call with the authority snapshot, so it commits or
/// rolls back with the record; it is optional, so every existing producer is unchanged; and it can never name the actor
/// itself or be blank. Oracles are literal ids and the port's recorded event order.
/// </summary>
public sealed class KernelAuditProvenanceTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_originator_is_staged_with_the_actor_and_snapshot_in_the_one_audit_call()
    {
        var port = new RecordingPort();
        var audit = Audit() with { AuthoritySnapshot = "worker-decision"u8.ToArray(), OriginatorId = "alice" };

        var result = await KernelTransactionBoundary.ExecuteAsync([new KernelCommand<string>(Operation(), "record", audit)], port);

        Assert.True(result.Committed);
        Assert.Equal(["begin", "record", "audit", "commit", "dispose"], port.Events);
        var staged = Assert.Single(port.StagedAudits);
        Assert.Equal("sys.records-worker", staged.ActorId);
        Assert.Equal("alice", staged.OriginatorId);
        Assert.Equal("worker-decision"u8.ToArray(), staged.AuthoritySnapshot.ToArray());
    }

    [Fact]
    public void Existing_four_member_evidence_has_no_originator_and_still_validates()
    {
        var audit = new KernelAuditEvidence("audit-1", "alice", At, "{}"u8.ToArray());

        Assert.Null(audit.OriginatorId);
        Assert.Same(audit, audit.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("sys.records-worker")]
    public async Task A_blank_or_self_naming_originator_is_refused_before_a_transaction_begins(string originator)
    {
        var port = new RecordingPort();
        var audit = Audit() with { OriginatorId = originator };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await KernelTransactionBoundary.ExecuteAsync([new KernelCommand<string>(Operation(), "record", audit)], port));

        Assert.Empty(port.Events);
    }

    [Fact]
    public async Task A_prepared_command_with_a_self_naming_originator_rolls_back_with_nothing_published()
    {
        var port = new RecordingPort();

        await Assert.ThrowsAsync<ArgumentException>(async () => await KernelTransactionBoundary.ExecutePreparedAsync(
            _ => ValueTask.FromResult(new KernelCommand<string>(Operation(), "record", Audit() with { OriginatorId = "sys.records-worker" })),
            port));

        Assert.Equal(["begin", "rollback", "dispose"], port.Events);
        Assert.Empty(port.StagedAudits);
        Assert.Equal(0, port.Commits);
    }

    private static KernelOperationIdentity Operation() => new("record-1", "key-1", "fingerprint-1");

    private static KernelAuditEvidence Audit() => new("audit-1", "sys.records-worker", At, "{}"u8.ToArray());

    private sealed class RecordingPort : IKernelTransactionPort<string, string>, IKernelPreparedTransactionPort<string, string>
    {
        public List<string> Events { get; } = [];
        public List<KernelAuditEvidence> StagedAudits { get; } = [];
        public int Commits { get; private set; }

        public ValueTask<IKernelTransaction<string, string>> BeginAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return ValueTask.FromResult<IKernelTransaction<string, string>>(new Transaction(this));
        }

        public ValueTask<IKernelPreparedTransaction<string, string>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return ValueTask.FromResult<IKernelPreparedTransaction<string, string>>(new Transaction(this));
        }

        private sealed class Transaction(RecordingPort owner) : IKernelTransaction<string, string>, IKernelPreparedTransaction<string, string>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("operation");
                return ValueTask.CompletedTask;
            }

            public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("record");
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("audit");
                owner.StagedAudits.Add(audit);
                return ValueTask.CompletedTask;
            }

            public ValueTask<string> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("commit");
                owner.Commits++;
                return ValueTask.FromResult("committed");
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("rollback");
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                owner.Events.Add("dispose");
                return ValueTask.CompletedTask;
            }
        }
    }
}
