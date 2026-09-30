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
    [InlineData(" ", "key-one", "fingerprint-one")]
    [InlineData("one", " ", "fingerprint-one")]
    [InlineData("one", "key-one", " ")]
    public async Task InvalidOperationIdentityIsRejectedBeforeTransactionBegins(
        string commandId, string idempotencyKey, string fingerprint)
    {
        var port = new RecordingPort();
        var invalid = Command() with { Operation = new(commandId, idempotencyKey, fingerprint) };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await KernelTransactionBoundary.ExecuteAsync([invalid], port));

        Assert.Empty(port.Events);
    }

    [Theory]
    [InlineData(" ", "actor")]
    [InlineData("audit-one", " ")]
    public async Task InvalidAuditEvidenceIsRejectedBeforeTransactionBegins(string auditId, string actorId)
    {
        var port = new RecordingPort();
        var invalid = Command() with { Audit = Command().Audit with { AuditId = auditId, ActorId = actorId } };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await KernelTransactionBoundary.ExecuteAsync([invalid], port));

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

    [Fact]
    public async Task PreparedCommand_BeginsBeforePreparationAndStagesTheResultingIdentity()
    {
        var port = new PreparedRecordingPort();

        var result = await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(
            _ =>
            {
                port.Events.Add("prepare");
                return ValueTask.FromResult(Command());
            },
            port);

        Assert.True(result.Committed);
        Assert.Equal("committed", result.Value);
        Assert.Equal(["begin", "prepare", "operation", "record", "audit", "commit", "dispose"], port.Events);
    }

    [Fact]
    public async Task InvalidPreparedOperationRollsBackBeforeStaging()
    {
        var port = new PreparedRecordingPort();
        var invalid = Command() with { Operation = new(" ", "key-one", "fingerprint-one") };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(
                _ => ValueTask.FromResult(invalid), port));

        Assert.Equal(["begin", "rollback", "dispose"], port.Events);
    }

    [Fact]
    public async Task InvalidPreparedAuditRollsBackBeforeStaging()
    {
        var port = new PreparedRecordingPort();
        var invalid = Command() with { Audit = Command().Audit with { AuditId = " " } };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(
                _ => ValueTask.FromResult(invalid), port));

        Assert.Equal(["begin", "rollback", "dispose"], port.Events);
    }

    [Fact]
    public async Task PreparedTransactionRejectsNullProducerBeforeBegin()
    {
        var port = new PreparedRecordingPort();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(null!, port));

        Assert.Empty(port.Events);
    }

    [Fact]
    public async Task PreparedTransactionRejectsNullPort()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(
                _ => ValueTask.FromResult(Command()), null!));
    }

    [Fact]
    public async Task PreparedTransactionRollsBackWhenProducerReturnsNullCommand()
    {
        var port = new PreparedRecordingPort();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(
                _ => ValueTask.FromResult<KernelCommand<string>>(null!), port));

        Assert.Equal(["begin", "rollback", "dispose"], port.Events);
    }

    [Fact]
    public async Task TransactionRejectsNullCommandsBeforeBegin()
    {
        var port = new RecordingPort();

        var error = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.ExecuteAsync<string, string>(null!, port));

        Assert.Equal("commands", error.ParamName);
        Assert.Empty(port.Events);
    }

    [Fact]
    public async Task TransactionRejectsNullPort()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.ExecuteAsync<string, string>([Command()], null!));
    }

    [Fact]
    public async Task TransactionRejectsNullCommandBeforeBegin()
    {
        var port = new RecordingPort();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.ExecuteAsync<string, string>([null!], port));

        Assert.Empty(port.Events);
    }

    [Fact]
    public async Task AReadOnlySingleCommandListIsIndexedWithoutEnumeration()
    {
        var port = new RecordingPort();
        var commands = new NonEnumerableSingleCommandList(Command());

        var result = await KernelTransactionBoundary.ExecuteAsync(commands, port);

        Assert.True(result.Committed);
        Assert.Equal("committed", result.Value);
        Assert.False(commands.WasEnumerated);
    }

    [Fact]
    public async Task JoinOutsideAnEnclosingExecutionIsRefused()
    {
        var participant = new RecordingParticipant([]);

        var error = await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
            await KernelTransactionBoundary.JoinAsync(Command("joined"), participant));

        Assert.Equal(KernelTransactionErrors.JoinWithoutEnclosingExecution, error.Code);
        Assert.Empty(participant.Events);
    }

    [Fact]
    public async Task JoinAfterTheEnclosingExecutionEndsIsRefused()
    {
        var port = new PreparedRecordingPort();
        var release = new TaskCompletionSource();
        Task<KernelTransactionStateException>? late = null;

        await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(_ =>
        {
            // The captured flow still carries the ambient execution after it commits.
            late = Task.Run(async () =>
            {
                await release.Task;
                return await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
                    await KernelTransactionBoundary.JoinAsync(Command("joined"), new RecordingParticipant(port.Events)));
            });
            return ValueTask.FromResult(Command());
        }, port);
        release.SetResult();

        Assert.Equal(KernelTransactionErrors.JoinWithoutEnclosingExecution, (await late!).Code);
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "dispose"], port.Events);
    }

    [Fact]
    public async Task JoinedParticipantStagesIntoTheEnclosingCommitAndNeverCommits()
    {
        var port = new PreparedRecordingPort();

        var result = await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(async ct =>
        {
            await KernelTransactionBoundary.JoinAsync(Command("joined"), new RecordingParticipant(port.Events), ct);
            port.Events.Add("joined");
            return Command();
        }, port);

        Assert.True(result.Committed);
        Assert.Equal(
            ["begin", "join-operation:joined", "join-record:record-joined", "join-audit:audit-joined", "joined",
             "operation", "record", "audit", "commit", "dispose"],
            port.Events);
    }

    [Fact]
    public async Task JoinedParticipantJoinsAnExecuteAsyncFromItsPort()
    {
        var port = new RecordingPort();
        port.OnRecord = () => KernelTransactionBoundary.JoinAsync(Command("joined"), new RecordingParticipant(port.Events));

        var result = await KernelTransactionBoundary.ExecuteAsync([Command()], port);

        Assert.True(result.Committed);
        Assert.Equal(
            ["begin", "record", "join-operation:joined", "join-record:record-joined", "join-audit:audit-joined",
             "audit", "commit", "dispose"],
            port.Events);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("record")]
    [InlineData("audit")]
    public async Task JoinedFailureRollsBackTheEnclosingExecution(string fault)
    {
        var port = new PreparedRecordingPort();

        await Assert.ThrowsAsync<InjectedFault>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(async ct =>
            {
                await KernelTransactionBoundary.JoinAsync(Command("joined"), new RecordingParticipant(port.Events, fault), ct);
                return Command();
            }, port));

        Assert.DoesNotContain("commit", port.Events);
        Assert.Equal(["rollback", "dispose"], port.Events[^2..]);
    }

    [Fact]
    public async Task SwallowedJoinedFailureStillDoomsThePreparedCommit()
    {
        var port = new PreparedRecordingPort();

        var error = await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(async ct =>
            {
                await SwallowAsync(KernelTransactionBoundary.JoinAsync(Command("joined"), new RecordingParticipant(port.Events, "record"), ct));
                return Command();
            }, port));

        Assert.Equal(KernelTransactionErrors.EnclosingExecutionDoomed, error.Code);
        Assert.DoesNotContain("commit", port.Events);
        Assert.Equal(["rollback", "dispose"], port.Events[^2..]);
    }

    [Fact]
    public async Task SwallowedJoinedFailureStillDoomsTheExecuteCommit()
    {
        var port = new RecordingPort();
        port.OnRecord = () => SwallowAsync(
            KernelTransactionBoundary.JoinAsync(Command("joined"), new RecordingParticipant(port.Events, "audit")));

        var error = await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
            await KernelTransactionBoundary.ExecuteAsync([Command()], port));

        Assert.Equal(KernelTransactionErrors.EnclosingExecutionDoomed, error.Code);
        Assert.DoesNotContain("commit", port.Events);
        Assert.Equal(0, port.Published);
    }

    [Theory]
    [InlineData(" ", "key-joined", "fingerprint-joined", "audit-joined", "actor")]
    [InlineData("joined", " ", "fingerprint-joined", "audit-joined", "actor")]
    [InlineData("joined", "key-joined", " ", "audit-joined", "actor")]
    [InlineData("joined", "key-joined", "fingerprint-joined", " ", "actor")]
    [InlineData("joined", "key-joined", "fingerprint-joined", "audit-joined", " ")]
    public async Task JoinedCommandNeedsValidOperationAndAuditOrDoomsTheCommit(
        string commandId, string idempotencyKey, string fingerprint, string auditId, string actorId)
    {
        var port = new PreparedRecordingPort();
        var invalid = Command("joined") with
        {
            Operation = new(commandId, idempotencyKey, fingerprint),
            Audit = Command("joined").Audit with { AuditId = auditId, ActorId = actorId },
        };
        ArgumentException? joinError = null;

        var error = await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(async ct =>
            {
                joinError = await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await KernelTransactionBoundary.JoinAsync(invalid, new RecordingParticipant(port.Events), ct));
                return Command();
            }, port));

        Assert.NotNull(joinError);
        Assert.Equal(KernelTransactionErrors.EnclosingExecutionDoomed, error.Code);
        Assert.DoesNotContain(port.Events, e => e.StartsWith("join-", StringComparison.Ordinal));
        Assert.DoesNotContain("commit", port.Events);
    }

    [Fact]
    public async Task JoinRejectsNullArguments()
    {
        var participant = new RecordingParticipant([]);

        Assert.Equal("command", (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.JoinAsync(null!, participant))).ParamName);
        Assert.Equal("participant", (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await KernelTransactionBoundary.JoinAsync<string>(Command("joined"), null!))).ParamName);
    }

    [Fact]
    public async Task ExecutionInsideAnEnclosingExecutionIsRefusedBeforeItBegins()
    {
        var outer = new PreparedRecordingPort();
        var inner = new RecordingPort();
        var innerPrepared = new PreparedRecordingPort();
        KernelTransactionStateException? execute = null, prepared = null;

        await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(async ct =>
        {
            execute = await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
                await KernelTransactionBoundary.ExecuteAsync([Command("inner")], inner, ct));
            prepared = await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
                await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(_ => ValueTask.FromResult(Command("inner")), innerPrepared, ct));
            return Command();
        }, outer);

        Assert.Equal(KernelTransactionErrors.NestedExecution, execute!.Code);
        Assert.Equal(KernelTransactionErrors.NestedExecution, prepared!.Code);
        Assert.Empty(inner.Events);
        Assert.Empty(innerPrepared.Events);
    }

    [Fact]
    public async Task SequentialExecutionsAreNotNested()
    {
        var port = new PreparedRecordingPort();
        await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(_ => ValueTask.FromResult(Command()), port);
        await Assert.ThrowsAsync<InjectedFault>(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(_ => throw new InjectedFault(), port));

        var result = await KernelTransactionBoundary.ExecuteAsync([Command("two")], new RecordingPort());

        Assert.True(result.Committed);
    }

    private static async ValueTask SwallowAsync(ValueTask join)
    {
        try
        {
            await join;
        }
        catch (Exception)
        {
            // The caller hides the participant failure; the enclosing commit must still refuse.
        }
    }

    private static KernelCommand<string> Command(string id = "one") => new(
        new(id, $"key-{id}", $"fingerprint-{id}"),
        $"record-{id}",
        new($"audit-{id}", "actor", DateTimeOffset.Parse("2030-01-02T03:04:05Z"), "evidence"u8.ToArray()));

    private sealed class InjectedFault : Exception { }

    private sealed class NonEnumerableSingleCommandList(KernelCommand<string> command) : IReadOnlyList<KernelCommand<string>>
    {
        public bool WasEnumerated { get; private set; }
        public int Count => 1;
        public KernelCommand<string> this[int index] => index == 0 ? command : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KernelCommand<string>> GetEnumerator()
        {
            WasEnumerated = true;
            throw new InvalidOperationException("The boundary only needs Count and [0] for an IReadOnlyList.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingPort(string? fault = null) : IKernelTransactionPort<string, string>
    {
        public List<string> Events { get; } = [];
        public Func<ValueTask>? OnRecord { get; set; }
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

            public async ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("record");
                if (fault == "record") throw new InjectedFault();
                _record = record;
                if (owner.OnRecord is { } onRecord) await onRecord();
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

    private sealed class PreparedRecordingPort : IKernelPreparedTransactionPort<string, string>
    {
        public List<string> Events { get; } = [];

        public ValueTask<IKernelPreparedTransaction<string, string>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return ValueTask.FromResult<IKernelPreparedTransaction<string, string>>(new Transaction(Events));
        }

        private sealed class Transaction(List<string> events) : IKernelPreparedTransaction<string, string>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
            {
                events.Add("operation");
                return ValueTask.CompletedTask;
            }

            public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default)
            {
                events.Add("record");
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            {
                events.Add("audit");
                return ValueTask.CompletedTask;
            }

            public ValueTask<string> CommitAsync(CancellationToken cancellationToken = default)
            {
                events.Add("commit");
                return ValueTask.FromResult("committed");
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            {
                events.Add("rollback");
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                events.Add("dispose");
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingParticipant(List<string> events, string? fault = null) : IKernelTransactionParticipant<string>
    {
        public List<string> Events => events;

        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) =>
            Stage("operation", operation.CommandId);

        public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default) =>
            Stage("record", record);

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) =>
            Stage("audit", audit.AuditId);

        private ValueTask Stage(string step, string value)
        {
            if (fault == step) throw new InjectedFault();
            events.Add($"join-{step}:{value}");
            return ValueTask.CompletedTask;
        }
    }
}
