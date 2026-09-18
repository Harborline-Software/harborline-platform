using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class KernelTransactionBoundaryTests
{
    [Fact]
    public async Task ValidCommandCommitsExactlyOneCompleteSet()
    {
        var port = new RecordingPort();
        var result = await KernelTransactionBoundary.ExecuteAsync([Command()], port);

        Assert.True(result.Committed);
        Assert.Equal("committed", result.Value);
        Assert.Equal(["begin", "record", "audit", "commit", "dispose"], port.Events);
        Assert.Equal("one", port.PublishedSet!.Value.Operation.CommandId);
        Assert.Equal("record-one", port.PublishedSet.Value.Record);
        Assert.Equal("audit-one", port.PublishedSet.Value.Audit.AuditId);
    }

    [Fact]
    public async Task ReplayReturnsTheOriginalResultWithoutASecondPublishedSet()
    {
        var port = new RecordingPort();
        var first = await KernelTransactionBoundary.ExecuteAsync([Command()], port);
        var replay = await KernelTransactionBoundary.ExecuteAsync([Command()], port);

        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(1, port.Published);
    }

    [Fact]
    public async Task TwoCommandBatchRefusesBeforeMutation()
    {
        var port = new RecordingPort();
        var result = await KernelTransactionBoundary.ExecuteAsync([Command(), Command("two")], port);

        Assert.False(result.Committed);
        Assert.Equal(KernelTransactionErrors.MultiCommandBatch, result.Refusal!.Code);
        Assert.Equal(2, result.Refusal.ObservedCommandCount);
        Assert.Empty(port.Events);
    }

    [Theory]
    [InlineData("record")]
    [InlineData("audit")]
    [InlineData("commit")]
    public async Task FaultBeforeEachCommitStepRollsBackAndPublishesNone(string fault)
    {
        var port = new RecordingPort(fault);
        await Assert.ThrowsAsync<InjectedFault>(async () =>
            await KernelTransactionBoundary.ExecuteAsync([Command()], port));

        Assert.Contains("rollback", port.Events);
        Assert.Equal(0, port.Published);
    }

    private static KernelCommand<string> Command(string id = "one") => new(
        new(id, $"key-{id}", $"fingerprint-{id}"),
        $"record-{id}",
        new($"audit-{id}", "actor", DateTimeOffset.Parse("2030-01-02T03:04:05Z"), "evidence"u8.ToArray()));

    private sealed class InjectedFault : Exception { }

    private sealed class RecordingPort(string? fault = null) : IKernelTransactionPort<string, string>
    {
        public List<string> Events { get; } = [];
        public int Published { get; private set; }
        public (KernelOperationIdentity Operation, string Record, KernelAuditEvidence Audit)? PublishedSet { get; private set; }

        public ValueTask<IKernelTransaction<string, string>> BeginAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return ValueTask.FromResult<IKernelTransaction<string, string>>(new Transaction(this, operation, fault));
        }

        private sealed class Transaction(
            RecordingPort owner,
            KernelOperationIdentity operation,
            string? fault) : IKernelTransaction<string, string>
        {
            private KernelOperationIdentity Operation { get; } = operation;
            private string? _record;
            private KernelAuditEvidence? _audit;

            public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("record");
                if (fault == "record") throw new InjectedFault();
                _record = record;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("audit");
                if (fault == "audit") throw new InjectedFault();
                _audit = audit;
                return ValueTask.CompletedTask;
            }

            public ValueTask<string> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("commit");
                if (fault == "commit") throw new InjectedFault();
                if (owner.PublishedSet is null)
                {
                    owner.PublishedSet = (Operation, _record!, _audit!);
                    owner.Published++;
                }
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
