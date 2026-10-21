using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Models;
using Xunit;
using static Harborline.Foundation.Forms.Engine.Tests.AcceptedCommandFixtures;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// Plan W6 and W7 (ADR-0110 decisions 1 to 4 and 6, rulings D3, D4, K2, K3): a <c>form_only</c> target's submit is a
/// durably accepted Records command. The command, the submit gate's authority snapshot (in the audit and on the command)
/// and its delivery row commit in the one existing submit boundary, or nothing does; the gate is decided inside that
/// boundary, so a revocation that contends on the store's scope is ordered against it. The command executes only as the
/// configured worker, never as the submitter, after every stored binding is validated again and after a durable claim;
/// recovery completes an interrupted command exactly once, and a delegate's recovery never inserts twice. Ordinary
/// projections keep their path, decided at commit by their consumer.
/// Oracles: literal ids, codes and fingerprints, the fixture decider's own evidence, and the simulated Records store's
/// record count keyed exactly as the Records operation keys an accepted command (tenant, submitter, original operation id).
/// </summary>
public sealed class FormAcceptedRecordCommandTests
{
    private const string AdaFingerprint = "88bab6d8f6dc68a877064d584cbb5b6c50e74f617ea50d81d3a53c2ee6ffbc4f";

    // ---- W6: one atomic acceptance ------------------------------------------------------------------------------------

    [Fact(DisplayName = "W6: a form_only submit commits the command, its acceptance snapshot and its delivery row together, server-derived and bound")]
    public async Task AcceptanceCommitsCommandEvidenceAndRowTogether()
    {
        var decider = new Decider();
        var sink = new RecordsSink();
        var harness = await AcceptingAsync(decider: decider, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "client-key-1"));

        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        var stored = Assert.Single(harness.State.Idempotency.Values);
        var command = Assert.IsType<FormAcceptedRecordCommand>(stored.Projection.AcceptedCommand);
        Assert.Equal(stored.Projection.OutboxId, command.CommandId);
        // Open item 3 reading: the operation id is the committed instance, never the client's key.
        Assert.Equal(receipt.InstanceId.ToString(), command.OperationId);
        Assert.NotEqual("client-key-1", command.OperationId);
        Assert.Equal(Target, command.RecordTypeId);
        Assert.Equal(AdaFingerprint, command.PayloadFingerprint);
        Assert.Equal(receipt.SubmittedAt, command.AcceptedAt);
        Assert.Equal(FormAcceptedCommandState.Pending, command.State);
        // The acceptance snapshot is the decider's own evidence, persisted on the audit and on the command alike.
        Assert.Equal(AuthoritySnapshotEncoding.Encode(Assert.Single(decider.Produced)), command.AcceptanceSnapshot.ToArray());
        var acceptanceAudit = Assert.Single(harness.State.Audits.Values);
        Assert.Equal(command.AcceptanceSnapshot.ToArray(), acceptanceAudit.AuthoritySnapshot.ToArray());
        // Review M3: the command names its acceptance audit and the exact decision instant; review M4: the mapped-body digest
        // the host computed from the plaintext accepted values for the target type is bound now.
        Assert.Equal(acceptanceAudit.AuditId, command.AcceptanceAuditId);
        Assert.Equal(Now, command.AcceptanceDecidedAt);
        Assert.Equal("records-write-intent/1", command.BodyDigestEncoding);
        Assert.Equal("body-digest:notes.entry:{\"name\":\"Ada\"}", command.BodyDigest);
        Assert.Equal([(Target, "{\"name\":\"Ada\"}")], sink.BodyRequests);
        var (tenant, held) = Assert.Single(harness.State.Commands.Values);
        Assert.Equal(new TenantId("tenant-engine"), tenant);
        Assert.Equal(FormAcceptedCommandState.Pending, held.State);
        // No worker is configured: nothing executes and nothing is delivered as the submitter.
        Assert.Empty(sink.Executions);
        Assert.Empty(sink.Delivered);
        Assert.Equal((1, 1, 1, 0), await harness.Store.CountsAsync());
        Assert.Null(stored.Receipt.Authorization);
    }

    [Fact(DisplayName = "W6: a failure at the one commit leaves no submission, audit, command, snapshot or row")]
    public async Task CommitFailureLeavesNothing()
    {
        var state = new InMemoryFormSubmissionState();
        var harness = await Harness.CreateAsync(state: state, definitionFactory: Targeted,
            submitGates: Port(new Decider()), recordTypePolicies: new FormOnlyReader(), projectionSink: new RecordsSink(),
            commitFailure: _ => new IOException("disk full"));
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")));

        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
        Assert.Empty(state.Commands);
        Assert.Empty(state.Idempotency);
    }

    [Fact(DisplayName = "W6: a store that cannot hold the command and its fence refuses a form_only submit before the gate is asked")]
    public async Task StoreWithoutTheCommandPortRefusesBeforeAnything()
    {
        var decider = new Decider();
        var harness = await AcceptingAsync(decider: decider, storeDecorator: inner => new PlainStore(inner));
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")));

        Assert.Empty(decider.Requests);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "M4: a form_only submit with no mapped-body digest port is refused before the gate is asked")]
    public async Task AcceptanceWithoutABodyDigestPortRefusesBeforeAnything()
    {
        var decider = new Decider();
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider),
            recordTypePolicies: new FormOnlyReader(), projectionSink: new NoBodiesSink());
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")));

        Assert.Empty(decider.Requests);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "M3: an acceptance whose decider spells the act differently is refused at acceptance, since no Records completion could bind it")]
    public async Task NonCanonicalAcceptanceEvidenceIsRefused()
    {
        var harness = await AcceptingAsync(decider: new Decider { ActOverride = "api:forms.submit" });
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")));

        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    public static TheoryData<string> InconsistentCommands =>
        ["command-id", "fingerprint", "instant", "snapshot-differs", "snapshot-missing", "target", "not-pending", "operation-id",
         "acceptance-audit", "decided-after-acceptance", "no-body-digest"];

    [Theory(DisplayName = "W6: the store refuses a command that disagrees with its submission, audit or row, and stores nothing")]
    [MemberData(nameof(InconsistentCommands))]
    public async Task StoreRefusesAnInconsistentCommand(string field)
    {
        var store = new InMemoryFormSubmissionStore();
        var commit = Commit();
        var command = commit.Projection.AcceptedCommand!;
        command = field switch
        {
            "command-id" => command with { CommandId = "outbox-other" },
            "fingerprint" => command with { PayloadFingerprint = "other" },
            "instant" => command with { AcceptedAt = Now.AddSeconds(1) },
            "snapshot-differs" => command with { AcceptanceSnapshot = "{}"u8.ToArray() },
            "snapshot-missing" => command with { AcceptanceSnapshot = ReadOnlyMemory<byte>.Empty },
            "target" => command with { RecordTypeId = "notes.other" },
            "not-pending" => command with { State = FormAcceptedCommandState.Claimed, StateActorId = Worker },
            "acceptance-audit" => command with { AcceptanceAuditId = "audit-other" },
            "decided-after-acceptance" => command with { AcceptanceDecidedAt = Now.AddTicks(1) },
            "no-body-digest" => command with { BodyDigest = null },
            _ => command with { OperationId = " " },
        };
        commit = commit with { Projection = commit.Projection with { AcceptedCommand = command } };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await using var scope = await store.BeginTransactionAsync();
            await scope.CommitAsync(commit);
        });

        Assert.Null(await store.GetAsync(commit.Submission.Tenant, commit.Submission.InstanceId));
        Assert.Null(await store.GetAcceptedCommandAsync(commit.Submission.Tenant, "outbox-1"));
    }

    // ---- decision 1: acceptance ordered against revocation (reading, accepted 2026-10-12) -------------------------------

    [Fact(DisplayName = "Decision 1 reading: an accepted command's gate is decided inside the store scope; an ordinary submit's before it")]
    public async Task AcceptanceIsDecidedInsideTheStoreScope()
    {
        var events = new List<string>();
        var accepted = await AcceptingAsync(decider: new Decider { Events = events }, storeDecorator: inner => new ObservedStore(inner, events));
        var ordinaryEvents = new List<string>();
        var ordinary = await Harness.CreateAsync(definitionFactory: Gated, submitGates: Port(new Decider { Events = ordinaryEvents }),
            storeDecorator: inner => new ObservedStore(inner, ordinaryEvents));
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        await accepted.Engine.SubmitAsync(new(accepted.Definition.Id, ada, "op-1"));
        await ordinary.Engine.SubmitAsync(new(ordinary.Definition.Id, ada, "op-1"));

        Assert.Equal(["begin", "decide:alice", "commit"], events);
        // ADR-0100 decision 3 is untouched for ordinary work: its gate is decided first, its delivery decided by its consumer.
        Assert.Equal(["decide:alice", "begin", "commit"], ordinaryEvents);
    }

    [Fact(DisplayName = "Decision 1 reading (discriminating): a submit arriving while a revocation holds the store scope waits, is decided only after the revocation commits, and is refused")]
    public async Task RevocationCommittedFirstRefusesAcceptance()
    {
        var decider = new Decider();
        var harness = await AcceptingAsync(decider: decider);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        Task<FormSubmitReceipt> submit;
        var askedWhileFenced = -1;
        await using (var fence = await harness.Store.BeginTransactionAsync())
        {
            submit = Task.Run(async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")));
            await Task.Delay(200);
            // Under the reading the gate is not asked before the scope is acquired; deciding before it would have allowed.
            askedWhileFenced = decider.Requests.Count;
            decider.Allowed = false;
        }

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(async () => await submit);

        Assert.Equal(0, askedWhileFenced);
        Assert.False(Assert.IsType<AuthorizationDecisionEvidence>(denied.Evidence).Allowed);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
        Assert.Empty(harness.State.Commands);
    }

    [Fact(DisplayName = "Decision 1 reading and decision 3: a revocation racing an acceptance waits for its commit, and the accepted command still completes")]
    public async Task RevocationRacingAnAcceptanceIsOrderedAfterItAndDoesNotCancel()
    {
        var events = new List<string>();
        var decider = new Decider { Events = events };
        var harness = await AcceptingAsync(decider: decider);
        var deciding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The revoker is another actor: it starts from the test's own flow, then contends on the store scope once the
        // acceptance is deciding inside it.
        var revoker = Task.Run(async () =>
        {
            await deciding.Task;
            await using var fence = await harness.Store.BeginTransactionAsync();
            decider.Allowed = false;
            lock (events) events.Add("revoked");
        });
        var revokedDuringAcceptance = true;
        decider.Before = async request =>
        {
            if (deciding.Task.IsCompleted) return;
            deciding.SetResult();
            await Task.WhenAny(revoker, Task.Delay(200));
            revokedDuringAcceptance = revoker.IsCompleted;
        };
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        await revoker;
        var sink = new RecordsSink();
        var worker = await AcceptingAsync(state: harness.State, worker: Worker, sink: sink, decider: decider, actorId: "sys.forms-recovery");
        var recovered = await worker.Engine.RecoverProjectionsAsync(10);

        Assert.False(revokedDuringAcceptance);
        Assert.Equal(["decide:alice", "revoked"], events);
        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        // Decision 3: the later ordinary revocation does not cancel the accepted command, and the submitter is not asked again.
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        Assert.Single(sink.Records);
        Assert.Single(decider.Requests);
    }

    // ---- W7: execution as the worker, recovery exactly once ----------------------------------------------------------

    [Fact(DisplayName = "W7: a configured worker executes the command at submit as itself, with the submitter only as provenance")]
    public async Task WorkerExecutesAtSubmitWithSubmitterProvenance()
    {
        var sink = new RecordsSink();
        var harness = await AcceptingAsync(worker: Worker, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));

        Assert.Equal(FormProjectionStatus.Complete, receipt.ProjectionStatus);
        var execution = Assert.Single(sink.Executions);
        Assert.Equal(Worker, execution.WorkerId);
        Assert.Equal("alice", execution.Envelope.ActorId);
        Assert.Equal(FormAcceptedCommandState.Claimed, execution.Command.State);
        Assert.Equal(Worker, execution.Command.StateActorId);
        AssertRecord(Assert.Single(sink.Records), receipt.InstanceId.ToString(), Worker);
        Assert.Empty(sink.Delivered);
        Assert.Equal(FormAcceptedCommandState.Completed, Assert.Single(harness.State.Commands.Values).Command.State);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "W7: process loss between acceptance and materialization; a restarted worker's recovery completes the command exactly once")]
    public async Task RecoveryAfterAcceptanceCompletesExactlyOnce()
    {
        var state = new InMemoryFormSubmissionState();
        var submitter = await AcceptingAsync(state: state);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, "op-1"));
        var sink = new RecordsSink();
        var restarted = await AcceptingAsync(state: state, worker: Worker, sink: sink, actorId: "sys.forms-recovery");

        var first = await restarted.Engine.RecoverProjectionsAsync(10);
        var second = await restarted.Engine.RecoverProjectionsAsync(10);
        var replay = await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, "op-1"));

        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), first);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), second);
        AssertRecord(Assert.Single(sink.Records), receipt.InstanceId.ToString(), Worker);
        Assert.Single(sink.Executions);
        Assert.Equal(FormProjectionStatus.Complete, replay.ProjectionStatus);
        Assert.Equal(receipt.InstanceId, replay.InstanceId);
    }

    [Fact(DisplayName = "W7: an execution interrupted after the Records effect stays claimed; recovery replays it and never inserts twice")]
    public async Task InterruptedExecutionReplaysAndNeverInsertsTwice()
    {
        var sink = new RecordsSink { FailuresAfterEffect = 1 };
        var harness = await AcceptingAsync(worker: Worker, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        var claimed = Assert.Single(harness.State.Commands.Values).Command;
        // The interrupted row as the inline attempt left it; recovery's completion removes it from the pending set.
        var interrupted = Assert.Single(harness.State.Pending.Values);
        var recovered = await harness.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        Assert.Equal(FormAcceptedCommandState.Claimed, claimed.State);
        Assert.Equal("form.engine.projection-pending", interrupted.LastErrorCode);
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        Assert.Equal(2, sink.Executions.Count);
        Assert.Single(sink.Records);
        Assert.Equal(FormAcceptedCommandState.Completed, Assert.Single(harness.State.Commands.Values).Command.State);
        Assert.Empty(harness.State.Pending.Values);
    }

    [Fact(DisplayName = "W7 delegate recovery: another worker recovers an interrupted command as itself, keyed by the submitter, and never inserts twice")]
    public async Task DelegateRecoveryNeverInsertsTwice()
    {
        var state = new InMemoryFormSubmissionState();
        var firstSink = new RecordsSink { FailuresAfterEffect = 1 };
        var first = await AcceptingAsync(state: state, worker: Worker, sink: firstSink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await first.Engine.SubmitAsync(new(first.Definition.Id, ada, "op-1"));
        // The delegate's store holds what the first worker's Records effect committed (one shared Records store).
        var delegateSink = new RecordsSink(firstSink.Records);
        var delegated = await AcceptingAsync(state: state, worker: DelegateWorker, sink: delegateSink, actorId: "sys.forms-recovery");

        var recovered = await delegated.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        var execution = Assert.Single(delegateSink.Executions);
        Assert.Equal(DelegateWorker, execution.WorkerId);
        Assert.Equal("alice", execution.Envelope.ActorId);
        Assert.Equal(receipt.InstanceId.ToString(), execution.Command.OperationId);
        // The first worker's record stands; the delegate replayed it.
        AssertRecord(Assert.Single(delegateSink.Records), receipt.InstanceId.ToString(), Worker);
    }

    [Fact(DisplayName = "W7: two recoveries over one store lease a command once; a duplicate run finds nothing to execute")]
    public async Task DuplicateRecoveryDoesNotExecuteTwice()
    {
        var state = new InMemoryFormSubmissionState();
        var submitter = await AcceptingAsync(state: state);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, "op-1"));
        var sink = new RecordsSink();
        var one = await AcceptingAsync(state: state, worker: Worker, sink: sink, actorId: "sys.forms-recovery");
        var two = await AcceptingAsync(state: state, worker: DelegateWorker, sink: sink, actorId: "sys.forms-recovery");

        var results = await Task.WhenAll(
            Task.Run(async () => await one.Engine.RecoverProjectionsAsync(10)),
            Task.Run(async () => await two.Engine.RecoverProjectionsAsync(10)));

        Assert.Equal(1, results.Sum(result => result.Completed));
        Assert.Single(sink.Executions);
        Assert.Single(sink.Records);
    }

    [Fact(DisplayName = "W7: a worker that is the submitter never executes the command; it stays pending")]
    public async Task WorkerEqualToTheSubmitterNeverExecutes()
    {
        var sink = new RecordsSink();
        var harness = await AcceptingAsync(worker: "alice", sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));

        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        Assert.Empty(sink.Executions);
        Assert.Equal("form.engine.accepted-command.worker-is-submitter", Assert.Single(harness.State.Pending.Values).LastErrorCode);
        Assert.Equal(FormAcceptedCommandState.Pending, Assert.Single(harness.State.Commands.Values).Command.State);
    }

    [Fact(DisplayName = "W7: recovery without a configured worker delivers ordinary projections and does not lease accepted commands")]
    public async Task RecoveryWithoutWorkerLeavesAcceptedCommandsPending()
    {
        var state = new InMemoryFormSubmissionState();
        var sink = new RecordsSink { FailOrdinary = 1 };
        var accepting = await AcceptingAsync(state: state, sink: sink);
        var ordinary = await Harness.CreateAsync(state: state, projectionSink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await accepting.Engine.SubmitAsync(new(accepting.Definition.Id, ada, "op-accepted"));
        await ordinary.Engine.SubmitAsync(new(ordinary.Definition.Id, ada, "op-ordinary"));

        var recovered = await ordinary.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        Assert.Null(Assert.Single(sink.Delivered).AcceptedCommand);
        Assert.Empty(sink.Executions);
        Assert.Equal(FormAcceptedCommandState.Pending, Assert.Single(state.Commands.Values).Command.State);
        Assert.Contains(state.Pending.Values, row => row.AcceptedCommand is not null);
    }

    [Fact(DisplayName = "W7: pending accepted commands never fill a recovery batch and starve an ordinary projection when no worker is configured")]
    public async Task AcceptedCommandsDoNotStarveOrdinaryRecovery()
    {
        var state = new InMemoryFormSubmissionState();
        var sink = new RecordsSink { FailOrdinary = 1 };
        var accepting = await AcceptingAsync(state: state, sink: sink);
        var ordinary = await Harness.CreateAsync(state: state, projectionSink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        for (var index = 0; index < 3; index++)
            await accepting.Engine.SubmitAsync(new(accepting.Definition.Id, ada, $"op-accepted-{index}"));
        await ordinary.Engine.SubmitAsync(new(ordinary.Definition.Id, ada, "op-ordinary"));

        var recovered = await ordinary.Engine.RecoverProjectionsAsync(1);

        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        Assert.Single(sink.Delivered);
        Assert.Equal(3, state.Commands.Count);
    }

    [Fact(DisplayName = "M2: a sink that cannot execute accepted commands never claims one, so the command stays pending and a cancel still wins")]
    public async Task SinkWithoutAcceptedSupportNeverClaimsAndTheCommandStaysCancellable()
    {
        var legacy = new FormEngineOrchestrationTests.RecordingProjection();
        var decider = new Decider();
        decider.CancelGrants.Add("tenant-admin");
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider),
            recordTypePolicies: new FormOnlyReader(), projection: legacy, options: new FormEngineOptions { AcceptedCommandWorkerId = Worker });
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        var recovered = await harness.Engine.RecoverProjectionsAsync(10);
        var pending = Assert.Single(harness.State.Commands.Values).Command;
        var cancel = await new FormAcceptedCommandCancellation(harness.Store, new AccessProvider(decider), new FixedClock(Now))
            .CancelAsync(new(new TenantId("tenant-engine"), "tenant-admin"), pending.CommandId);

        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), recovered);
        Assert.Empty(legacy.Delivered);
        Assert.Equal(FormAcceptedCommandState.Pending, pending.State);
        Assert.Equal(FormAcceptedCommandCancelStatus.Cancelled, cancel.Status);
    }

    [Fact(DisplayName = "M2: with a worker, stalled accepted commands that keep failing never starve a later one; attempts order the lease")]
    public async Task StalledAcceptedCommandsDoNotStarveALaterOne()
    {
        var state = new InMemoryFormSubmissionState();
        var clock = new SteppingClock(Now);
        var submitter = await AcceptingAsync(state: state, clock: clock);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        for (var index = 0; index < 3; index++)
            await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, $"op-stalled-{index}"));
        var healthy = await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, "op-healthy"));
        var sink = new RecordsSink();
        foreach (var entry in state.Commands.Values.Where(entry => entry.Command.OperationId != healthy.InstanceId.ToString()))
            sink.FailingOperations.Add(entry.Command.OperationId);
        var worker = await AcceptingAsync(state: state, worker: Worker, sink: sink, actorId: "sys.forms-recovery", clock: clock);

        for (var run = 0; run < 4; run++) await worker.Engine.RecoverProjectionsAsync(1);

        // Leasing by submit instant alone would retry the oldest stalled command in every one-row batch forever.
        AssertRecord(Assert.Single(sink.Records), healthy.InstanceId.ToString(), Worker);
        Assert.Equal(3, state.Pending.Values.Count(row => row.AcceptedCommand is { State: FormAcceptedCommandState.Claimed }));
    }

    [Fact(DisplayName = "Commit 5 fix 4 (isolated): a cancel that wins between the commit and the submit-time claim makes the receipt report Cancelled")]
    public async Task CancelWinningBeforeTheSubmitTimeClaimReportsCancelled()
    {
        var sink = new RecordsSink();
        var harness = await AcceptingAsync(worker: Worker, sink: sink, storeDecorator: inner => new CancelBeforeClaimStore((InMemoryFormSubmissionStore)inner));
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));

        Assert.Equal(FormProjectionStatus.Cancelled, receipt.ProjectionStatus);
        Assert.Empty(sink.Executions);
        Assert.Equal(FormAcceptedCommandState.Cancelled, Assert.Single(harness.State.Commands.Values).Command.State);
    }

    [Fact(DisplayName = "I-1, I-2: a role target's submit is delivered inline in its submitter's request, and recovery never replays one as the submitter")]
    public async Task RoleTargetIsDeliveredInlineAndNeverRecovered()
    {
        var state = new InMemoryFormSubmissionState();
        var delivered = new RecordsSink();
        var role = await Harness.CreateAsync(state: state, definitionFactory: UngatedTargeted, recordTypePolicies: new RoleReader(),
            projectionSink: delivered);
        var failing = new RecordsSink { FailOrdinary = 1 };
        var roleFailing = await Harness.CreateAsync(state: state, definitionFactory: UngatedTargeted, recordTypePolicies: new RoleReader(),
            projectionSink: failing);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var inline = await role.Engine.SubmitAsync(new(role.Definition.Id, ada, "op-role"));
        var pendingReceipt = await roleFailing.Engine.SubmitAsync(new(roleFailing.Definition.Id, ada, "op-role-failing"));
        var recovered = await roleFailing.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(FormProjectionStatus.Complete, inline.ProjectionStatus);
        Assert.Equal(Target, Assert.Single(delivered.Delivered).RecordTypeId);
        Assert.Equal(FormProjectionStatus.Pending, pendingReceipt.ProjectionStatus);
        // Fail closed (review I-1): the targeted row without a command is not delivered outside its submitter's request.
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), recovered);
        Assert.Empty(failing.Delivered);
        Assert.Empty(state.Commands);
    }

    // ---- W7: runtime binding validation and terminal refusal ---------------------------------------------------------

    public static TheoryData<string, string> StoredSubstitutions => new()
    {
        { "protected-values", "records.command.binding_mismatch" },
        { "operation-id", "records.command.binding_mismatch" },
        { "record-type", "records.command.binding_mismatch" },
        { "form-version", "records.command.binding_mismatch" },
        { "fingerprint", "records.command.binding_mismatch" },
        { "acceptance-principal", "records.command.not_accepted" },
        { "acceptance-denied", "records.command.not_accepted" },
        { "acceptance-late", "records.command.not_accepted" },
        { "acceptance-malformed", "records.command.not_accepted" },
        { "acceptance-act-stage-principal", "records.command.not_accepted" },
        { "acceptance-other-instant", "records.command.not_accepted" },
        { "acceptance-other-form", "records.command.not_accepted" },
        { "acceptance-no-digest", "records.command.not_accepted" },
    };

    [Theory(DisplayName = "W7 (D3): a stored row or command that no longer binds the accepted write is refused terminally before any claim or effect")]
    [MemberData(nameof(StoredSubstitutions))]
    public async Task TamperedStoredCommandIsRefusedTerminally(string field, string code)
    {
        var state = new InMemoryFormSubmissionState();
        var submitter = await AcceptingAsync(state: state);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, "op-1"));
        var (outboxId, row) = Assert.Single(state.Pending);
        var command = row.AcceptedCommand!;
        var tampered = field switch
        {
            "protected-values" => row with { ProtectedAcceptedValues = """{"name":"Mallory"}"""u8.ToArray() },
            "operation-id" => row with { AcceptedCommand = command with { OperationId = "op-other" } },
            "record-type" => row with { RecordTypeId = "notes.other", AcceptedCommand = command with { RecordTypeId = "notes.other" } },
            "form-version" => row with { DefinitionVersion = new SemanticVersion(2, 0, 0) },
            "fingerprint" => row with { AcceptedCommand = command with { PayloadFingerprint = "other" } },
            "acceptance-principal" => row with { AcceptedCommand = command with { AcceptanceSnapshot = Acceptance("mallory", allowed: true, Now) } },
            "acceptance-denied" => row with { AcceptedCommand = command with { AcceptanceSnapshot = Acceptance("alice", allowed: false, Now) } },
            "acceptance-late" => row with { AcceptedCommand = command with { AcceptanceSnapshot = Acceptance("alice", allowed: true, receipt.SubmittedAt.AddTicks(1)) } },
            // Commit 5 fix 5, isolated: the top-level principal still names alice; only the act-stage fact names another.
            "acceptance-act-stage-principal" => row with
            {
                AcceptedCommand = command with
                {
                    AcceptanceSnapshot = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(Acceptance("alice", allowed: true, Now))
                        .Replace("\"principal:alice\"", "\"principal:mallory\"", StringComparison.Ordinal)),
                },
            },
            "acceptance-other-instant" => row with { AcceptedCommand = command with { AcceptanceSnapshot = Acceptance("alice", allowed: true, Now.AddMinutes(-1)) } },
            "acceptance-other-form" => row with { AcceptedCommand = command with { AcceptanceSnapshot = Acceptance("alice", allowed: true, Now, "other@1.0.0") } },
            "acceptance-no-digest" => row with { AcceptedCommand = command with { BodyDigest = null } },
            _ => row with { AcceptedCommand = command with { AcceptanceSnapshot = "not a snapshot"u8.ToArray() } },
        };
        state.Pending[outboxId] = tampered;
        var sink = new RecordsSink();
        var worker = await AcceptingAsync(state: state, worker: Worker, sink: sink, actorId: "sys.forms-recovery");

        var first = await worker.Engine.RecoverProjectionsAsync(10);
        var again = await worker.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(new FormProjectionRecoveryResult(1, 0, 0) { Settled = 1 }, first);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), again);
        Assert.Empty(sink.Executions);
        var settled = Assert.Single(state.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Refused, settled.State);
        Assert.Equal(code, settled.RefusalCode);
        Assert.Equal(Worker, settled.StateActorId);
        Assert.Equal(FormProjectionStatus.Refused, Assert.Single(state.Idempotency.Values).Receipt.ProjectionStatus);
        // Review M1: the terminal refusal is an audit record, committed with the transition.
        AssertTerminalAudit(state, "accepted-command-refused", Worker, code, afterClaim: false);
    }

    [Fact(DisplayName = "R2-M1: a claimed command whose stored binding breaks is never executed or made terminal; after the bounded binding failures it is quarantined, audited, visible and never leased again")]
    public async Task ClaimedCommandWithBrokenBindingIsQuarantinedAfterBoundedBindingFailures()
    {
        var sink = new RecordsSink { FailuresAfterEffect = 1 };
        var harness = await AcceptingAsync(worker: Worker, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        var (outboxId, row) = Assert.Single(harness.State.Pending);
        // The interrupted run's Records effect committed; a later tamper of the stored row must never make it run again.
        harness.State.Pending[outboxId] = row with { ProtectedAcceptedValues = """{"name":"Mallory"}"""u8.ToArray() };

        var runs = new List<FormProjectionRecoveryResult>();
        var failures = new List<int>();
        for (var run = 0; run < 3; run++)
        {
            runs.Add(await harness.Engine.RecoverProjectionsAsync(10));
            failures.Add(Assert.Single(harness.State.Commands.Values).Command.BindingFailures);
        }
        var afterQuarantine = await harness.Engine.RecoverProjectionsAsync(10);

        // Default limit three: two counted binding failures, then the third run quarantines; none is a terminal refusal.
        Assert.All(runs, result => Assert.Equal(new FormProjectionRecoveryResult(1, 0, 1), result));
        Assert.Equal([1, 2, 2], failures);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), afterQuarantine);
        var quarantined = Assert.Single(harness.State.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Quarantined, quarantined.State);
        Assert.Equal("records.command.binding_mismatch", quarantined.RefusalCode);
        Assert.Equal(Worker, quarantined.StateActorId);
        // Still visible: the row stays in the pending pool and the receipt stays pending, because the outcome is unknown.
        Assert.Equal(FormAcceptedCommandState.Quarantined, Assert.Single(harness.State.Pending.Values).AcceptedCommand!.State);
        Assert.Equal(FormProjectionStatus.Pending, Assert.Single(harness.State.Idempotency.Values).Receipt.ProjectionStatus);
        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        Assert.Single(sink.Executions);
        Assert.Single(sink.Records);
        AssertTerminalAudit(harness.State, "accepted-command-quarantined", Worker, "records.command.binding_mismatch", afterClaim: true);
    }

    [Fact(DisplayName = "R2-L1: infrastructure failures of a claimed command never count toward the binding bound; its first binding failure is counted, not quarantined")]
    public async Task InfrastructureFailuresDoNotCountTowardTheBindingBound()
    {
        var sink = new RecordsSink { FailuresAfterEffect = 1 };
        var harness = await AcceptingAsync(worker: Worker, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        sink.FailuresBeforeEffect = 3;
        for (var run = 0; run < 3; run++) await harness.Engine.RecoverProjectionsAsync(10);
        var attemptsBefore = Assert.Single(harness.State.Pending.Values).Attempts;
        var (outboxId, row) = Assert.Single(harness.State.Pending);
        harness.State.Pending[outboxId] = row with { ProtectedAcceptedValues = """{"name":"Mallory"}"""u8.ToArray() };

        var recovered = await harness.Engine.RecoverProjectionsAsync(10);

        // Four attempts already (one at submit, three on infrastructure); counting every attempt would quarantine now.
        Assert.Equal(4, attemptsBefore);
        Assert.Equal(new FormProjectionRecoveryResult(1, 0, 1), recovered);
        var command = Assert.Single(harness.State.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Claimed, command.State);
        Assert.Equal(1, command.BindingFailures);
        var pending = Assert.Single(harness.State.Pending.Values);
        Assert.Equal(5, pending.Attempts);
        Assert.Equal("records.command.binding_mismatch", pending.LastErrorCode);
        Assert.Single(harness.State.Audits);
    }

    [Fact(DisplayName = "R2-L1: a form revision that is not found at execution is an infrastructure failure; the command stays pending and later completes")]
    public async Task TransientDefinitionNotFoundStaysPending()
    {
        var state = new InMemoryFormSubmissionState();
        var submitter = await AcceptingAsync(state: state);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, "op-1"));
        // A worker whose definition store does not (yet) hold revision 1.0.0: its read answers not found.
        var sink = new RecordsSink();
        var lagging = await Harness.CreateAsync(state: state,
            definitionFactory: (schema, tenant) => Targeted(schema, tenant) with { Version = new SemanticVersion(2, 0, 0) },
            submitGates: Port(new Decider()), recordTypePolicies: new FormOnlyReader(), projectionSink: sink,
            options: new FormEngineOptions { AcceptedCommandWorkerId = Worker }, actorId: "sys.forms-recovery");
        var worker = await AcceptingAsync(state: state, worker: Worker, sink: sink, actorId: "sys.forms-recovery");

        var notFound = await lagging.Engine.RecoverProjectionsAsync(10);
        var pending = Assert.Single(state.Commands.Values).Command;
        var row = Assert.Single(state.Pending.Values);
        var recovered = await worker.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(new FormProjectionRecoveryResult(1, 0, 1), notFound);
        Assert.Equal(FormAcceptedCommandState.Pending, pending.State);
        Assert.Equal(0, pending.BindingFailures);
        Assert.Equal("form.engine.projection-pending", row.LastErrorCode);
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        AssertRecord(Assert.Single(sink.Records), receipt.InstanceId.ToString(), Worker);
    }

    [Fact(DisplayName = "R2-M1: a Records refusal after a resumed claim (an earlier run may have committed) quarantines the command instead of refusing it")]
    public async Task TerminalRefusalAfterAResumedClaimIsQuarantined()
    {
        var sink = new RecordsSink { FailuresAfterEffect = 1 };
        var harness = await AcceptingAsync(worker: Worker, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        // The host's mapping changed between the interrupted run and its retry; the Records operation now refuses it.
        sink.TerminalRefusal = "records.command.binding_mismatch";

        var recovered = await harness.Engine.RecoverProjectionsAsync(10);
        var again = await harness.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(new FormProjectionRecoveryResult(1, 0, 1), recovered);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), again);
        var command = Assert.Single(harness.State.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Quarantined, command.State);
        Assert.Equal("records.command.binding_mismatch", command.RefusalCode);
        Assert.Equal(2, sink.Executions.Count);
        Assert.Single(sink.Records);
        Assert.Equal(FormProjectionStatus.Pending, Assert.Single(harness.State.Idempotency.Values).Receipt.ProjectionStatus);
        AssertTerminalAudit(harness.State, "accepted-command-quarantined", Worker, "records.command.binding_mismatch", afterClaim: true);
    }

    [Fact(DisplayName = "R2-L4: a waiting row only moves forward, so a sustained inflow of fresh rows that keep failing never holds an older row with more attempts back")]
    public async Task SustainedInflowNeverHoldsAnOlderRowBack()
    {
        var store = new InMemoryFormSubmissionStore();
        await CommitOrdinaryAsync(store, "aging-old", Now);
        for (var attempt = 0; attempt < 3; attempt++) await store.RetryProjectionAsync("outbox-aging-old", "form.engine.projection-pending");
        var leasedOrder = new List<string>();
        for (var cycle = 0; cycle < 4; cycle++)
        {
            // Each cycle one fresh row arrives, and the leased row fails again unless it is the old one.
            await CommitOrdinaryAsync(store, $"aging-fresh-{cycle}", Now.AddMinutes(cycle + 1));
            var leased = Assert.Single(await store.LeasePendingAsync(1));
            leasedOrder.Add(leased.OutboxId);
            if (leased.OutboxId == "outbox-aging-old") await store.CompleteProjectionAsync(leased.OutboxId, []);
            else await store.RetryProjectionAsync(leased.OutboxId, "form.engine.projection-pending");
        }

        // Fewest attempts first would lease a fresh row (0 attempts) ahead of the old one (3) in every cycle. In cycle 3,
        // fresh-0 (failed in cycle 1, before fresh-2 arrived) is ahead of fresh-2: the queue rotates in enqueue order.
        Assert.Equal(["outbox-aging-old", "outbox-aging-fresh-0", "outbox-aging-fresh-1", "outbox-aging-fresh-0"], leasedOrder);
    }

    [Fact(DisplayName = "R2-L4 and M2: a row that keeps failing goes to the back of the queue, so older poison rows never starve a fresh row")]
    public async Task PoisonRowsNeverStarveAFreshRow()
    {
        var store = new InMemoryFormSubmissionStore();
        for (var index = 0; index < 3; index++) await CommitOrdinaryAsync(store, $"aging-poison-{index}", Now);
        // The fresh row arrives an hour later; aging by submit instant would favour the old poison rows for an hour of retries.
        await CommitOrdinaryAsync(store, "aging-fresh", Now.AddHours(1));
        var leasedOrder = new List<string>();
        for (var lease = 0; lease < 7; lease++)
        {
            var leased = Assert.Single(await store.LeasePendingAsync(1));
            leasedOrder.Add(leased.OutboxId);
            await store.RetryProjectionAsync(leased.OutboxId, "form.engine.projection-pending");
        }

        // A rotation: every row is leased once before any is leased again.
        Assert.Equal(
            ["outbox-aging-poison-0", "outbox-aging-poison-1", "outbox-aging-poison-2", "outbox-aging-fresh",
             "outbox-aging-poison-0", "outbox-aging-poison-1", "outbox-aging-poison-2"],
            leasedOrder);
    }

    [Fact(DisplayName = "R2-L2 (D4, K2): a role target whose inline delivery failed is redelivered only by its own submitter's replay, never by recovery or another caller")]
    public async Task RoleTargetFailedInlineIsRedeliveredOnlyByItsSubmittersReplay()
    {
        var state = new InMemoryFormSubmissionState();
        var sink = new RecordsSink { FailOrdinary = 2 };
        var role = await Harness.CreateAsync(state: state, definitionFactory: UngatedTargeted, recordTypePolicies: new RoleReader(), projectionSink: sink);
        var other = await Harness.CreateAsync(state: state, definitionFactory: UngatedTargeted, recordTypePolicies: new RoleReader(),
            projectionSink: sink, actorId: "mallory");
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var first = await role.Engine.SubmitAsync(new(role.Definition.Id, ada, "op-role"));
        var recovered = await role.Engine.RecoverProjectionsAsync(10);
        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(
            async () => await other.Engine.SubmitAsync(new(other.Definition.Id, ada, "op-role")));
        var failedReplay = await role.Engine.SubmitAsync(new(role.Definition.Id, ada, "op-role"));
        var attempts = Assert.Single(state.Pending.Values).Attempts;
        var replay = await role.Engine.SubmitAsync(new(role.Definition.Id, ada, "op-role"));
        var settled = await role.Engine.SubmitAsync(new(role.Definition.Id, ada, "op-role"));

        Assert.Equal(FormProjectionStatus.Pending, first.ProjectionStatus);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), recovered);
        // A failed redelivery is recorded as an attempt and stays pending and retryable by the submitter.
        Assert.Equal(FormProjectionStatus.Pending, failedReplay.ProjectionStatus);
        Assert.Equal(2, attempts);
        Assert.Equal(FormProjectionStatus.Complete, replay.ProjectionStatus);
        Assert.Equal(first.InstanceId, replay.InstanceId);
        Assert.Equal(FormProjectionStatus.Complete, settled.ProjectionStatus);
        // Delivered once, inside alice's own replay, never as anyone else.
        Assert.Equal("alice", Assert.Single(sink.Delivered).ActorId);
        Assert.Empty(state.Pending);
        Assert.Empty(state.Commands);
    }

    [Fact(DisplayName = "F1 (fail closed, I-1): a role target's submit to a store without the command port is refused provider-unavailable before the gate is asked, and nothing persists")]
    public async Task RoleTargetOnStoreWithoutCommandPort_IsRefusedAtSubmit()
    {
        var decider = new Decider();
        var sink = new RecordsSink();
        var plain = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: new RoleReader(),
            projectionSink: sink, storeDecorator: inner => new PlainStore(inner));
        var withPort = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: new RoleReader(),
            projectionSink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var refused = await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(
            async () => await plain.Engine.SubmitAsync(new(plain.Definition.Id, ada, "op-role")));
        var askedBeforeRefusal = decider.Requests.Count;
        var delivered = await withPort.Engine.SubmitAsync(new(withPort.Definition.Id, ada, "op-role"));

        Assert.Equal("form.engine.provider-unavailable", refused.Code);
        Assert.Equal(0, askedBeforeRefusal);
        Assert.Equal((0, 0, 0, 0), await plain.Store.CountsAsync());
        // The control: the same role-targeted form on a store with the port is ordinary work, gated and delivered inline (D1).
        Assert.Single(decider.Requests);
        Assert.Equal(FormProjectionStatus.Complete, delivered.ProjectionStatus);
        Assert.Equal(Target, Assert.Single(sink.Delivered).RecordTypeId);
        Assert.Empty(withPort.State.Commands);
    }

    public static TheoryData<string> StoredRoleSubmissions => ["completed", "pending"];

    [Theory(DisplayName = "F1 (fail closed, I-1, revision 17): replaying an existing role-targeted submission on a store without the command port is refused provider-unavailable before the gate or the store's replay detection, and nothing changes")]
    [MemberData(nameof(StoredRoleSubmissions))]
    public async Task RoleTargetReplayOnStoreWithoutCommandPort_IsRefusedBeforeReplayDetection(string stored)
    {
        var events = new List<string>();
        var decider = new Decider { Events = events };
        var sink = new RecordsSink();
        var plain = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: new RoleReader(),
            projectionSink: sink, storeDecorator: inner => new PlainStore(new ObservedStore(inner, events)));
        // The submission already exists in the port-less store under alice's key, written through the store's own API: a
        // completed one (delivered inline), or a pending one whose inline delivery failed (a submission from before the repair).
        await SeedRoleSubmissionAsync(plain, "op-role", stored);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var refused = await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(
            async () => await plain.Engine.SubmitAsync(new(plain.Definition.Id, ada, "op-role")));

        Assert.Equal("form.engine.provider-unavailable", refused.Code);
        // No gate decision and no store scope: the store's commit, its only replay detector, never ran, so no stored receipt
        // was disclosed and the stranded row was not answered.
        Assert.Empty(events);
        Assert.Empty(decider.Requests);
        // Nothing written: the one submission, its audit and its row are exactly as seeded, and nothing was delivered.
        var stillStored = Assert.Single(plain.State.Idempotency.Values);
        Assert.Equal(new EntityId("harborline", "forms", "seeded-role"), stillStored.Submission.InstanceId);
        Assert.Empty(plain.State.Leased);
        Assert.Empty(plain.State.Commands);
        Assert.Empty(sink.Delivered);
        if (stored == "completed")
        {
            Assert.Equal((1, 1, 0, 1), await plain.Store.CountsAsync());
            Assert.Equal("outbox-seeded-role", Assert.Single(plain.State.Completed));
            Assert.Equal(FormProjectionStatus.Complete, stillStored.Receipt.ProjectionStatus);
        }
        else
        {
            Assert.Equal((1, 1, 1, 0), await plain.Store.CountsAsync());
            var row = Assert.Single(plain.State.Pending.Values);
            Assert.Equal(("outbox-seeded-role", 1, "form.engine.projection-pending"), (row.OutboxId, row.Attempts, row.LastErrorCode));
            Assert.Equal(FormProjectionStatus.Pending, stillStored.Receipt.ProjectionStatus);
        }
    }

    [Fact(DisplayName = "F1 (revision 17) control: the same replay on a store with the command port is gated, detected by the store's commit and answered with the stored receipt, and nothing is written or delivered")]
    public async Task RoleTargetReplayOnStoreWithCommandPort_ReturnsTheStoredReceipt()
    {
        var events = new List<string>();
        var decider = new Decider { Events = events };
        var sink = new RecordsSink();
        var withPort = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: new RoleReader(),
            projectionSink: sink, storeDecorator: inner => new ObservedStore(inner, events));
        await SeedRoleSubmissionAsync(withPort, "op-role", "completed");
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var replay = await withPort.Engine.SubmitAsync(new(withPort.Definition.Id, ada, "op-role"));

        // Ordinary work's order (ADR-0100 decision 3): the gate first, then the store scope whose commit detects the replay.
        Assert.Equal(["decide:alice", "begin", "commit"], events);
        // The stored receipt: the seeded instance, its own instant and its completed status, not a fresh submission.
        Assert.Equal(new EntityId("harborline", "forms", "seeded-role"), replay.InstanceId);
        Assert.Equal(Now.AddDays(-1), replay.SubmittedAt);
        Assert.Equal(FormProjectionStatus.Complete, replay.ProjectionStatus);
        // The gate's evidence is attached only when the stored write is exactly the one this request authorizes (D3), so the
        // seeded submission is the engine's own write for this key, payload and head, and the replay is a real one.
        var authorization = Assert.IsType<FormSubmitAuthorization>(replay.Authorization);
        Assert.Equal(new EntityId("harborline", "forms", "seeded-role"), authorization.InstanceId);
        Assert.Equal(AdaFingerprint, authorization.PayloadFingerprint);
        Assert.Single(decider.Requests);
        Assert.Equal((1, 1, 0, 1), await withPort.Store.CountsAsync());
        Assert.Single(withPort.State.Idempotency);
        Assert.Empty(withPort.State.Commands);
        Assert.Empty(sink.Delivered);
    }

    [Fact(DisplayName = "F5: on a store without the command port, recovery rotates stranded targeted rows undelivered, so more of them than a batch holds never starve an untargeted row")]
    public async Task Recovery_StoreWithoutCommandPort_StrandedTargetedRowsDoNotStarveUntargetedRows()
    {
        var state = new InMemoryFormSubmissionState();
        var store = new InMemoryFormSubmissionStore(state);
        // Three targeted rows without a command (rows written before revision 16) wait ahead of one untargeted row.
        for (var index = 0; index < 3; index++) await CommitOrdinaryAsync(store, $"stranded-{index}", Now, recordTypeId: Target);
        await CommitOrdinaryAsync(store, "deliverable", Now);
        var sink = new RecordsSink();
        var recovery = await Harness.CreateAsync(state: state, projectionSink: sink, storeDecorator: inner => new PlainStore(inner));

        var first = await recovery.Engine.RecoverProjectionsAsync(2);
        var second = await recovery.Engine.RecoverProjectionsAsync(2);

        // A release would keep stranded-0 and stranded-1 at the head and lease them again in every two-row batch.
        Assert.Equal(new FormProjectionRecoveryResult(2, 0, 2), first);
        Assert.Equal(new FormProjectionRecoveryResult(2, 1, 1), second);
        Assert.Equal("outbox-deliverable", Assert.Single(sink.Delivered).OutboxId);
        // Fail closed (review I-1): no targeted row is delivered; each leased one recorded one attempt and stays pending.
        Assert.Equal(["outbox-stranded-0", "outbox-stranded-1", "outbox-stranded-2"], state.Pending.Keys.Order(StringComparer.Ordinal));
        Assert.All(state.Pending.Values, row => Assert.Equal((1, "form.engine.projection-pending"), (row.Attempts, row.LastErrorCode)));
    }

    [Fact(DisplayName = "L2: a refusal names the state it saw; a stale expected state changes nothing and adds no audit")]
    public async Task RefusalWithAStaleExpectedStateChangesNothing()
    {
        var harness = await AcceptingAsync();
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        var commandId = Assert.Single(harness.State.Commands.Keys);
        await harness.Store.ClaimAcceptedCommandAsync(commandId, Worker, Now);

        var stale = await harness.Store.RefuseAcceptedCommandAsync(commandId, FormAcceptedCommandState.Pending, "records.command.binding_mismatch",
            new Harborline.Kernel.Core.KernelAuditEvidence("refusal-audit", Worker, Now, "{}"u8.ToArray()));

        Assert.False(stale!.Changed);
        Assert.Equal(FormAcceptedCommandState.Claimed, stale.Command.State);
        Assert.Single(harness.State.Audits);
    }

    [Fact(DisplayName = "Decision 1 reading, host constraint: a decider that opens its own kernel transaction inside the acceptance is refused, and nothing persists")]
    public async Task DeciderOpeningANestedKernelTransactionFailsClosed()
    {
        var decider = new Decider
        {
            Before = async _ => await Harborline.Kernel.Core.KernelTransactionBoundary.ExecuteAsync(
                [new Harborline.Kernel.Core.KernelCommand<string>(new("nested", "nested-key", "nested-fingerprint"), "record",
                    new("nested-audit", "decider", Now, ReadOnlyMemory<byte>.Empty))],
                new NeverPort()),
        };
        var harness = await AcceptingAsync(decider: decider);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")));

        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "W7: a terminal refusal from the Records execution on the run that made the claim settles the command refused and it is never retried")]
    public async Task TerminalRefusalFromExecutionIsNeverRetried()
    {
        var sink = new RecordsSink { TerminalRefusal = "records.write.replay_conflict" };
        var harness = await AcceptingAsync(worker: Worker, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        var recovered = await harness.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(FormProjectionStatus.Refused, receipt.ProjectionStatus);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), recovered);
        Assert.Single(sink.Executions);
        Assert.Equal("records.write.replay_conflict", Assert.Single(harness.State.Commands.Values).Command.RefusalCode);
        AssertTerminalAudit(harness.State, "accepted-command-refused", Worker, "records.write.replay_conflict", afterClaim: true);
    }

    [Fact(DisplayName = "R2-M1: a Records refusal on the recovery run that made the claim is terminal, because no earlier run existed")]
    public async Task TerminalRefusalOnARecoveryMadeClaimIsTerminal()
    {
        var state = new InMemoryFormSubmissionState();
        var submitter = await AcceptingAsync(state: state);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await submitter.Engine.SubmitAsync(new(submitter.Definition.Id, ada, "op-1"));
        var sink = new RecordsSink { TerminalRefusal = "records.command.creation_gate_changed" };
        var worker = await AcceptingAsync(state: state, worker: Worker, sink: sink, actorId: "sys.forms-recovery");

        var recovered = await worker.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(new FormProjectionRecoveryResult(1, 0, 0) { Settled = 1 }, recovered);
        var command = Assert.Single(state.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Refused, command.State);
        Assert.Equal("records.command.creation_gate_changed", command.RefusalCode);
        Assert.Empty(sink.Records);
        AssertTerminalAudit(state, "accepted-command-refused", Worker, "records.command.creation_gate_changed", afterClaim: true);
    }

    [Fact(DisplayName = "W7: an infrastructure failure before the effect stays pending and claimed, and recovery completes it")]
    public async Task InfrastructureFailureStaysPending()
    {
        var sink = new RecordsSink { FailuresBeforeEffect = 1 };
        var harness = await AcceptingAsync(worker: Worker, sink: sink);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        var recovered = await harness.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        Assert.Single(sink.Records);
    }

    // One simulated Records record, keyed by tenant, submitter and the original operation id, created by its first executor.
    private static void AssertRecord(KeyValuePair<(string Tenant, string Submitter, string OperationId), string> record, string operationId, string executor)
    {
        Assert.Equal(("tenant-engine", "alice", operationId), record.Key);
        Assert.Equal(executor, record.Value);
    }

    // The one terminal or quarantining transition's audit envelope beside the acceptance audit (review M1, R2-M1).
    private static void AssertTerminalAudit(InMemoryFormSubmissionState state, string operation, string actor, string code, bool afterClaim)
    {
        var audit = Assert.Single(state.Audits.Values, envelope => JsonNode.Parse(envelope.Payload.Span)!["op"]?.GetValue<string>() == operation);
        var payload = JsonNode.Parse(audit.Payload.Span)!.AsObject();
        Assert.Equal(actor, audit.ActorId);
        Assert.Equal(code, payload["code"]!.GetValue<string>());
        Assert.Equal(afterClaim, payload["after_claim"]!.GetValue<bool>());
        Assert.Equal(2, state.Audits.Count);
    }

    // One ordinary submission committed at the given instant, its lease released: untargeted, or, given a Record Type, a
    // targeted row without a command (a role target, or a row written before revision 16).
    private static async Task CommitOrdinaryAsync(InMemoryFormSubmissionStore store, string id, DateTimeOffset at, string? recordTypeId = null)
    {
        var tenant = new TenantId("tenant-engine");
        var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instance = new EntityId("harborline", "forms", id);
        var form = new FormDefinitionId("inspection");
        var version = new SemanticVersion(1, 0, 0);
        var schema = recordTypeId is null ? (SchemaId?)null : new SchemaId("sha256:targeted");
        var bytes = "{}"u8.ToArray();
        await using (var scope = await store.BeginTransactionAsync())
        {
            await scope.CommitAsync(new FormSubmissionCommit(
                "key-" + id,
                new FormSubmissionRecord(instance, tenant, party, "alice", form, version, "fingerprint-" + id, bytes, at, schema, recordTypeId),
                new FormMutationAuditEnvelope("audit-" + id, instance, tenant, "alice", bytes, at),
                new FormProjectionEnvelope("outbox-" + id, instance, tenant, party, "alice", form, version, null, bytes, at,
                    SchemaRef: schema, RecordTypeId: recordTypeId),
                new FormSubmitReceipt(instance, at, FormProjectionStatus.Pending, [])));
        }
        await store.ReleaseProjectionLeaseAsync("outbox-" + id);
    }

    // One existing role-targeted submission of alice's {"name":"Ada"} under the given key, committed through the harness's
    // in-memory store as the engine commits ordinary work: the head's form, version, schema and target, Ada's accepted-payload
    // fingerprint, no case reference and no command. "completed" was delivered inline; "pending" failed its inline delivery once.
    private static async Task SeedRoleSubmissionAsync(Harness harness, string idempotencyKey, string stored)
    {
        var tenant = new TenantId("tenant-engine");
        var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instance = new EntityId("harborline", "forms", "seeded-role");
        var head = harness.Definition;
        var at = Now.AddDays(-1);
        var bytes = "{}"u8.ToArray();
        await using (var scope = await harness.Store.BeginTransactionAsync())
        {
            var result = await scope.CommitAsync(new FormSubmissionCommit(
                idempotencyKey,
                new FormSubmissionRecord(instance, tenant, party, "alice", head.Id, head.Version, AdaFingerprint, bytes, at, head.SchemaRef, Target),
                new FormMutationAuditEnvelope("audit-seeded-role", instance, tenant, "alice", bytes, at),
                new FormProjectionEnvelope("outbox-seeded-role", instance, tenant, party, "alice", head.Id, head.Version, null, bytes, at,
                    SchemaRef: head.SchemaRef, RecordTypeId: Target),
                new FormSubmitReceipt(instance, at, FormProjectionStatus.Pending, [])));
            Assert.Equal(FormSubmissionCommitDisposition.Created, result.Disposition);
        }
        if (stored == "completed") await harness.Store.CompleteProjectionAsync("outbox-seeded-role", []);
        else await harness.Store.RetryProjectionAsync("outbox-seeded-role", "form.engine.projection-pending");
    }

    private static FormDefinition UngatedTargeted(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { RecordTypeId = Target };

    private static FormSubmissionCommit Commit()
    {
        var tenant = new TenantId("tenant-engine");
        var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instance = new EntityId("harborline", "forms", "instance-1");
        var form = new FormDefinitionId("inspection");
        var version = new SemanticVersion(1, 0, 0);
        var schema = new SchemaId("sha256:accepted");
        var bytes = "{}"u8.ToArray();
        var snapshot = Acceptance("alice", allowed: true, Now);
        return new FormSubmissionCommit(
            "op-1",
            new FormSubmissionRecord(instance, tenant, party, "alice", form, version, "fingerprint-1", bytes, Now, schema, Target),
            new FormMutationAuditEnvelope("audit-1", instance, tenant, "alice", bytes, Now) { AuthoritySnapshot = snapshot },
            new FormProjectionEnvelope("outbox-1", instance, tenant, party, "alice", form, version, null, bytes, Now,
                SchemaRef: schema, RecordTypeId: Target)
            {
                AcceptedCommand = new FormAcceptedRecordCommand("outbox-1", instance.ToString(), Target, "fingerprint-1", Now, snapshot)
                {
                    AcceptanceAuditId = "audit-1", AcceptanceDecidedAt = Now, BodyDigestEncoding = "records-write-intent/1", BodyDigest = "digest-1",
                },
            },
            new FormSubmitReceipt(instance, Now, FormProjectionStatus.Pending, []));
    }

    // A kernel port that must never be reached: the nested execution is refused before it begins.
    private sealed class NeverPort : Harborline.Kernel.Core.IKernelTransactionPort<string, string>
    {
        public ValueTask<Harborline.Kernel.Core.IKernelTransaction<string, string>> BeginAsync(
            Harborline.Kernel.Core.KernelOperationIdentity operation, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("a nested execution never begins");
    }

    // A sink with no mapped-body digest port.
    private sealed class NoBodiesSink : IFormProjectionSink
    {
        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormProjectionDeliveryResult([]));
    }

    private sealed class RoleReader : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RecordTypeCreationGate?>(RecordTypeCreationGate.Role);
    }

    // Each read is one second after the previous one, so submissions are ordered by instant.
    private sealed class SteppingClock(DateTimeOffset start) : TimeProvider
    {
        private int _reads;

        public override DateTimeOffset GetUtcNow() => start.AddSeconds(Interlocked.Increment(ref _reads) - 1);
    }

    // A cancel commits on the inner store just before the claim it forwards, so the cancel wins the fence first.
    private sealed class CancelBeforeClaimStore(InMemoryFormSubmissionStore inner) : IFormSubmissionTransactionStore, IFormAcceptedCommandStore
    {
        public ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);
        public ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default) => inner.CommitAsync(commit, cancellationToken);
        public ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default) => inner.GetAsync(tenant, instanceId, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, FormProjectionLeaseKinds kinds, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, kinds, cancellationToken);
        public ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default) => inner.ReleaseProjectionLeaseAsync(outboxId, cancellationToken);
        public ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default) => inner.CompleteProjectionAsync(outboxId, skips, cancellationToken);
        public ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default) => inner.RetryProjectionAsync(outboxId, stableErrorCode, cancellationToken);
        public ValueTask<FormAcceptedRecordCommand?> GetAcceptedCommandAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default) => inner.GetAcceptedCommandAsync(tenant, commandId, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> CancelAcceptedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.CancelAcceptedCommandAsync(tenant, commandId, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RefuseAcceptedCommandAsync(string commandId, FormAcceptedCommandState expectedState, string refusalCode, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.RefuseAcceptedCommandAsync(commandId, expectedState, refusalCode, audit, cancellationToken);
        public ValueTask<FormProjectionEnvelope?> LeasePendingProjectionAsync(TenantId tenant, string outboxId, CancellationToken cancellationToken = default) => inner.LeasePendingProjectionAsync(tenant, outboxId, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RecordBindingFailureAsync(string commandId, string code, CancellationToken cancellationToken = default) => inner.RecordBindingFailureAsync(commandId, code, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> QuarantineAcceptedCommandAsync(string commandId, string code, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.QuarantineAcceptedCommandAsync(commandId, code, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> AbandonQuarantinedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.AbandonQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> ResolveQuarantinedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.ResolveQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);

        public async ValueTask<FormAcceptedCommandTransition?> ClaimAcceptedCommandAsync(string commandId, string workerId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            await inner.CancelAcceptedCommandAsync(new TenantId("tenant-engine"), commandId,
                new Harborline.Kernel.Core.KernelAuditEvidence("cancel-audit", "tenant-admin", at, "{}"u8.ToArray()) { AuthoritySnapshot = "cancel-decision"u8.ToArray() },
                cancellationToken);
            return await inner.ClaimAcceptedCommandAsync(commandId, workerId, at, cancellationToken);
        }
    }

    // Passes every call through but does not implement the accepted-command port.
    private sealed class PlainStore(IFormSubmissionTransactionStore inner) : IFormSubmissionTransactionStore
    {
        public ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);
        public ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default) => inner.CommitAsync(commit, cancellationToken);
        public ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default) => inner.GetAsync(tenant, instanceId, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, cancellationToken);
        public ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default) => inner.ReleaseProjectionLeaseAsync(outboxId, cancellationToken);
        public ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default) => inner.CompleteProjectionAsync(outboxId, skips, cancellationToken);
        public ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default) => inner.RetryProjectionAsync(outboxId, stableErrorCode, cancellationToken);
    }
}

/// <summary>Shared fixtures for the accepted-command tests: a decider, a simulated Records store sink, and the harness shape.</summary>
internal static class AcceptedCommandFixtures
{
    internal const string Worker = "sys.forms-worker";
    internal const string DelegateWorker = "sys.forms-worker-2";
    internal const string Target = "notes.entry";
    internal static readonly DateTimeOffset Now = FormEngineOrchestrationTests.Now;
    internal static readonly SubmitGate InspectorGate = new(Role: RoleReference.Domain("inspector"));

    internal static FormDefinition Targeted(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate, RecordTypeId = Target };

    internal static FormDefinition Gated(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate };

    internal static AccessProviderFormSubmitGateAccess Port(Decider decider) => new(new AccessProvider(decider), new FixedClock(Now));

    // A form_only target's engine over a shared state, with an optional configured worker and the given scope actor.
    internal static Task<Harness> AcceptingAsync(InMemoryFormSubmissionState? state = null, string? worker = null, RecordsSink? sink = null,
        Decider? decider = null, string actorId = "alice",
        Func<IFormSubmissionTransactionStore, IFormSubmissionTransactionStore>? storeDecorator = null, TimeProvider? clock = null,
        FormEngineOptions? options = null) =>
        Harness.CreateAsync(state: state, definitionFactory: Targeted, submitGates: Port(decider ?? new Decider()),
            recordTypePolicies: new FormOnlyReader(), projectionSink: sink ?? new RecordsSink(),
            options: options ?? new FormEngineOptions { AcceptedCommandWorkerId = worker }, actorId: actorId, storeDecorator: storeDecorator,
            clock: clock);

    // An acceptance snapshot in the shared grammar, for a literal submit-gate request.
    internal static byte[] Acceptance(string principal, bool allowed, DateTimeOffset at, string formRevision = "inspection@1.0.0") =>
        AuthoritySnapshotEncoding.Encode(new AuthorizationDecisionEvidence(
            new AccessRequest("forms:submit-gate", principal, "tenant-engine",
                new AccessRecord("tenant-engine", "form", formRevision, new Dictionary<string, JsonNode?>()), at),
            allowed, allowed ? "None" : "NoGrant", "grant:fixture@1", [], []));

    // Stands in for the host's decider: the form's submit gate is allowed while Allowed holds; the cancel permission only
    // for principals holding an explicit grant. The verdict is literal state, never computed from grants.
    internal sealed class Decider : IAuthorizationDecider
    {
        public bool Allowed { get; set; } = true;
        public HashSet<string> CancelGrants { get; } = new(StringComparer.Ordinal);
        public List<AccessRequest> Requests { get; } = [];
        public List<AuthorizationDecisionEvidence> Produced { get; } = [];
        public List<string>? Events { get; set; }
        public Func<AccessRequest, Task>? Before { get; set; }
        // When set, the decider's evidence spells its act this way (a host adapter's own scoped act spelling).
        public string? ActOverride { get; set; }
        private readonly object _lock = new();

        public async ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            if (Before is not null) await Before(request);
            var allowed = request.Operation == "records.command.cancel" ? CancelGrants.Contains(request.Principal) : Allowed;
            lock (_lock)
            {
                Requests.Add(request);
                if (Events is { } events) lock (events) events.Add("decide:" + request.Principal);
                var evidence = new AuthorizationDecisionEvidence(request, allowed, allowed ? "None" : "NoGrant", "grant:fixture@1", [], [],
                    act: ActOverride);
                Produced.Add(evidence);
                return evidence;
            }
        }
    }

    internal sealed class FormOnlyReader : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RecordTypeCreationGate?>(RecordTypeCreationGate.FormOnly);
    }

    // The host sink as a conforming W10 adapter would behave, over a simulated Records store keyed exactly as the Records
    // operation keys an accepted command: (tenant, submitter, original operation id). The value is the first executor; a
    // second execution of the same operation replays and inserts nothing.
    internal sealed class RecordsSink(Dictionary<(string Tenant, string Submitter, string OperationId), string>? records = null)
        : IFormProjectionSink, IFormAcceptedRecordBodies
    {
        private readonly object _lock = new();

        // A conforming host sink executes accepted commands (review M2).
        public bool ExecutesAcceptedCommands => true;
        public List<(string RecordTypeId, string Values)> BodyRequests { get; } = [];
        // Operations whose execution always fails on infrastructure before any effect.
        public HashSet<string> FailingOperations { get; } = new(StringComparer.Ordinal);

        // Review M4: the host's mapping and digest, stable for the same type and values.
        public ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default)
        {
            var values = request.AcceptedCandidate.RootElement.GetRawText();
            lock (_lock) BodyRequests.Add((request.RecordTypeId, values));
            return ValueTask.FromResult(new FormAcceptedRecordBodyDigest("records-write-intent/1", $"body-digest:{request.RecordTypeId}:{values}"));
        }

        public Dictionary<(string Tenant, string Submitter, string OperationId), string> Records { get; } = records ?? new();
        public List<FormAcceptedCommandDelivery> Executions { get; } = [];
        public List<FormProjectionEnvelope> Delivered { get; } = [];
        public int FailuresBeforeEffect { get; set; }
        public int FailuresAfterEffect { get; set; }
        public int FailOrdinary { get; set; }
        public string? TerminalRefusal { get; set; }

        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (FailOrdinary-- > 0) return ValueTask.FromException<FormProjectionDeliveryResult>(new IOException("projection unavailable"));
                Delivered.Add(envelope);
                return ValueTask.FromResult(new FormProjectionDeliveryResult([]));
            }
        }

        public ValueTask<FormProjectionDeliveryResult> DeliverAcceptedAsync(FormAcceptedCommandDelivery delivery, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Executions.Add(delivery);
                if (FailingOperations.Contains(delivery.Command.OperationId) || FailuresBeforeEffect-- > 0)
                    return ValueTask.FromException<FormProjectionDeliveryResult>(new IOException("records unavailable"));
                if (TerminalRefusal is { } code) return ValueTask.FromResult(new FormProjectionDeliveryResult([]) { TerminalRefusal = code });
                Records.TryAdd((delivery.Envelope.Tenant.Value, delivery.Envelope.ActorId, delivery.Command.OperationId), delivery.WorkerId);
                if (FailuresAfterEffect-- > 0) return ValueTask.FromException<FormProjectionDeliveryResult>(new IOException("completion mark lost"));
                return ValueTask.FromResult(new FormProjectionDeliveryResult([]));
            }
        }
    }

    // Logs each store scope begin and commit into a shared event list; forwards everything, including the command port.
    internal sealed class ObservedStore(IFormSubmissionTransactionStore inner, List<string> events) : IFormSubmissionTransactionStore, IFormAcceptedCommandStore
    {
        private IFormAcceptedCommandStore Commands => (IFormAcceptedCommandStore)inner;

        public async ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var scope = await inner.BeginTransactionAsync(cancellationToken);
            lock (events) events.Add("begin");
            return new Scope(scope, events);
        }
        public ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default) => inner.CommitAsync(commit, cancellationToken);
        public ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default) => inner.GetAsync(tenant, instanceId, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, FormProjectionLeaseKinds kinds, CancellationToken cancellationToken = default) => Commands.LeasePendingAsync(maximum, kinds, cancellationToken);
        public ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default) => inner.ReleaseProjectionLeaseAsync(outboxId, cancellationToken);
        public ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default) => inner.CompleteProjectionAsync(outboxId, skips, cancellationToken);
        public ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default) => inner.RetryProjectionAsync(outboxId, stableErrorCode, cancellationToken);
        public ValueTask<FormAcceptedRecordCommand?> GetAcceptedCommandAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default) => Commands.GetAcceptedCommandAsync(tenant, commandId, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> ClaimAcceptedCommandAsync(string commandId, string workerId, DateTimeOffset at, CancellationToken cancellationToken = default) => Commands.ClaimAcceptedCommandAsync(commandId, workerId, at, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> CancelAcceptedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.CancelAcceptedCommandAsync(tenant, commandId, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RefuseAcceptedCommandAsync(string commandId, FormAcceptedCommandState expectedState, string refusalCode, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.RefuseAcceptedCommandAsync(commandId, expectedState, refusalCode, audit, cancellationToken);
        public ValueTask<FormProjectionEnvelope?> LeasePendingProjectionAsync(TenantId tenant, string outboxId, CancellationToken cancellationToken = default) => Commands.LeasePendingProjectionAsync(tenant, outboxId, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RecordBindingFailureAsync(string commandId, string code, CancellationToken cancellationToken = default) => Commands.RecordBindingFailureAsync(commandId, code, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> QuarantineAcceptedCommandAsync(string commandId, string code, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.QuarantineAcceptedCommandAsync(commandId, code, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> AbandonQuarantinedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.AbandonQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> ResolveQuarantinedCommandAsync(TenantId tenant, string commandId, Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Commands.ResolveQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);

        private sealed class Scope(IFormSubmissionTransactionScope inner, List<string> events) : IFormSubmissionTransactionScope
        {
            public async ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default)
            {
                var result = await inner.CommitAsync(commit, cancellationToken);
                lock (events) events.Add("commit");
                return result;
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    internal sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
