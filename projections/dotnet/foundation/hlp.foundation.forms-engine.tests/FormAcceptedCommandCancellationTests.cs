using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Xunit;
using static Harborline.Foundation.Forms.Engine.Tests.AcceptedCommandFixtures;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// The owner's cancellation rule (2026-10-10; ADR-0110 decision 5): <c>records.command.cancel</c> is a dedicated
/// permission held by explicit grant, decided against current authority for the exact tenant and command before anything
/// is read; cancellation and execution contend on the store's one durable fence, so exactly one wins; a cancel after
/// execution claimed the command is <c>records.command.cancel_too_late</c>; retries are idempotent; every consumer honours
/// <c>cancelled</c>; an infrastructure failure stays pending and retryable. A quarantined command (review R2-M1) is claimed,
/// so a cancel is too late unless the host's receipt lookup establishes DES-0029's abandonment condition, and it is completed
/// only through its audited resolution once the same lookup finds its effect committed (review R3-L1). Oracles: literal
/// codes, states and request members, and the fixture decider's own evidence.
/// </summary>
public sealed class FormAcceptedCommandCancellationTests
{
    private const string Admin = "tenant-admin";
    private static readonly TenantId Tenant = new("tenant-engine");

    [Fact(DisplayName = "Cancel: a granted administrator cancels a pending command, the snapshot commits with it, and no consumer executes it")]
    public async Task GrantedCancelBeforeExecutionWins()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var (harness, commandId) = await AcceptedAsync(decider);
        var cancellation = Cancellation(harness, decider);

        var result = await cancellation.CancelAsync(new(Tenant, Admin), commandId);
        var sink = new RecordsSink();
        var worker = await AcceptingAsync(state: harness.State, worker: Worker, sink: sink, decider: decider, actorId: "sys.forms-recovery");
        var recovered = await worker.Engine.RecoverProjectionsAsync(10);

        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, "records.command.cancelled", false), (result.Status, result.Code, result.Replayed));
        var evidence = Assert.IsType<AuthorizationDecisionEvidence>(result.Decision);
        Assert.True(evidence.Allowed);
        var asked = decider.Requests[^1];
        Assert.Equal("records.command.cancel", asked.Operation);
        Assert.Equal(Admin, asked.Principal);
        Assert.Equal("tenant-engine", asked.Tenant);
        Assert.Equal("accepted-command", asked.Record.Kind);
        Assert.Equal(commandId, asked.Record.Id);
        var command = Assert.Single(harness.State.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Cancelled, command.State);
        Assert.Equal(Admin, command.StateActorId);
        Assert.Equal(AuthoritySnapshotEncoding.Encode(evidence), command.StateSnapshot.ToArray());
        // Review M1: the won cancel is an audit record of the submission, committed with the transition, carrying the cancel
        // decision's snapshot; the canceller is its actor.
        var cancelAudit = Assert.Single(harness.State.Audits.Values, audit => audit.ActorId == Admin);
        Assert.Equal(AuthoritySnapshotEncoding.Encode(evidence), cancelAudit.AuthoritySnapshot.ToArray());
        Assert.Equal("accepted-command-cancelled", System.Text.Json.Nodes.JsonNode.Parse(cancelAudit.Payload.Span)!["op"]!.GetValue<string>());
        Assert.Equal(2, harness.State.Audits.Count);
        Assert.Equal(FormProjectionStatus.Cancelled, Assert.Single(harness.State.Idempotency.Values).Receipt.ProjectionStatus);
        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), recovered);
        Assert.Empty(sink.Executions);
        Assert.Empty(sink.Records);
    }

    [Fact(DisplayName = "Cancel: a retry is idempotent and keeps the first cancel's snapshot")]
    public async Task CancelRetryIsIdempotent()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var (harness, commandId) = await AcceptedAsync(decider);
        var cancellation = Cancellation(harness, decider);

        var first = await cancellation.CancelAsync(new(Tenant, Admin), commandId);
        var retry = await cancellation.CancelAsync(new(Tenant, Admin), commandId);

        Assert.False(first.Replayed);
        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, "records.command.cancelled", true), (retry.Status, retry.Code, retry.Replayed));
        // The idempotent retry adds no second audit.
        Assert.Equal(2, harness.State.Audits.Count);
        Assert.Equal(AuthoritySnapshotEncoding.Encode(first.Decision!), Assert.Single(harness.State.Commands.Values).Command.StateSnapshot.ToArray());
    }

    [Fact(DisplayName = "Cancel: the permission is dedicated; the submitter without an explicit grant is denied and nothing is read or changed")]
    public async Task SubmitterWithoutTheGrantIsDenied()
    {
        var decider = new Decider();
        var (harness, commandId) = await AcceptedAsync(decider);
        var counting = new CountingCommands(harness.Store);
        var cancellation = new FormAcceptedCommandCancellation(counting, new AccessProvider(decider), new FixedClock(Now));

        var result = await cancellation.CancelAsync(new(Tenant, "alice"), commandId);

        Assert.Equal((FormAcceptedCommandCancelStatus.Denied, "NoGrant"), (result.Status, result.Code));
        Assert.False(result.Decision!.Allowed);
        Assert.Equal(0, counting.Cancels);
        Assert.Equal(FormAcceptedCommandState.Pending, Assert.Single(harness.State.Commands.Values).Command.State);
    }

    [Fact(DisplayName = "Cancel: another tenant's command or an unknown one is not found, and nothing changes")]
    public async Task AnotherTenantsCommandIsNotFound()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var (harness, commandId) = await AcceptedAsync(decider);
        var cancellation = Cancellation(harness, decider);

        var foreign = await cancellation.CancelAsync(new(new TenantId("tenant-other"), Admin), commandId);
        var unknown = await cancellation.CancelAsync(new(Tenant, Admin), "no-such-command");

        Assert.Equal((FormAcceptedCommandCancelStatus.NotFound, "records.command.not_found"), (foreign.Status, foreign.Code));
        Assert.Equal((FormAcceptedCommandCancelStatus.NotFound, "records.command.not_found"), (unknown.Status, unknown.Code));
        Assert.Equal(FormAcceptedCommandState.Pending, Assert.Single(harness.State.Commands.Values).Command.State);
    }

    [Fact(DisplayName = "Cancel: once execution claimed the command, a cancel is too late and the interrupted execution still completes")]
    public async Task CancelAfterTheClaimIsTooLate()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var sink = new RecordsSink { FailuresBeforeEffect = 1 };
        var harness = await AcceptingAsync(worker: Worker, sink: sink, decider: decider);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        var commandId = Assert.Single(harness.State.Commands.Keys);
        var cancellation = Cancellation(harness, decider);

        var late = await cancellation.CancelAsync(new(Tenant, Admin), commandId);
        var recovered = await harness.Engine.RecoverProjectionsAsync(10);

        Assert.Equal((FormAcceptedCommandCancelStatus.TooLate, "records.command.cancel_too_late"), (late.Status, late.Code));
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        Assert.Single(sink.Records);
        Assert.Equal(FormAcceptedCommandState.Completed, Assert.Single(harness.State.Commands.Values).Command.State);
    }

    [Fact(DisplayName = "Cancel: a completed command is too late to cancel; amending the record is a separate action")]
    public async Task CancelAfterCompletionIsTooLate()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var harness = await AcceptingAsync(worker: Worker, decider: decider);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));

        var late = await Cancellation(harness, decider).CancelAsync(new(Tenant, Admin), Assert.Single(harness.State.Commands.Keys));

        Assert.Equal(FormAcceptedCommandCancelStatus.TooLate, late.Status);
        Assert.Equal(FormAcceptedCommandState.Completed, Assert.Single(harness.State.Commands.Values).Command.State);
    }

    [Fact(DisplayName = "Cancel and execute race on the one fence: in every run exactly one of them wins")]
    public async Task CancelAndExecuteNeverBothSucceed()
    {
        for (var run = 0; run < 40; run++)
        {
            var decider = new Decider();
            decider.CancelGrants.Add(Admin);
            var (harness, commandId) = await AcceptedAsync(decider);
            var sink = new RecordsSink();
            var worker = await AcceptingAsync(state: harness.State, worker: Worker, sink: sink, decider: decider, actorId: "sys.forms-recovery");
            var cancellation = Cancellation(harness, decider);

            var cancel = Task.Run(async () => await cancellation.CancelAsync(new(Tenant, Admin), commandId));
            var execute = Task.Run(async () => await worker.Engine.RecoverProjectionsAsync(10));
            await Task.WhenAll(cancel, execute);
            var cancelResult = await cancel;

            var cancelled = cancelResult.Status == FormAcceptedCommandCancelStatus.Cancelled;
            var executed = sink.Records.Count == 1;
            Assert.True(cancelled ^ executed, $"run {run}: cancelled={cancelled}, executed={executed}");
            Assert.Equal(cancelled ? FormAcceptedCommandState.Cancelled : FormAcceptedCommandState.Completed,
                Assert.Single(harness.State.Commands.Values).Command.State);
            if (!cancelled) Assert.Equal(FormAcceptedCommandCancelStatus.TooLate, cancelResult.Status);
        }
    }

    [Fact(DisplayName = "Cancel: an infrastructure failure stays pending and retryable, never a terminal refusal")]
    public async Task InfrastructureFailureIsRetryable()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var (harness, commandId) = await AcceptedAsync(decider);
        var failing = new CountingCommands(harness.Store) { FailNextCancel = true };
        var cancellation = new FormAcceptedCommandCancellation(failing, new AccessProvider(decider), new FixedClock(Now));

        await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(
            async () => await cancellation.CancelAsync(new(Tenant, Admin), commandId));
        var pending = Assert.Single(harness.State.Commands.Values).Command.State;
        var retried = await cancellation.CancelAsync(new(Tenant, Admin), commandId);

        Assert.Equal(FormAcceptedCommandState.Pending, pending);
        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, false), (retried.Status, retried.Replayed));
    }

    public static TheoryData<string> UnestablishedEffects => ["no-lookup", "unknown", "committed"];

    [Theory(DisplayName = "R2-M1: a quarantined command is too late to cancel unless its effect is established absent; nothing changes and no audit is added")]
    [MemberData(nameof(UnestablishedEffects))]
    public async Task QuarantinedCommandIsTooLateUnlessItsEffectIsAbsent(string lookup)
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var (harness, commandId) = await QuarantinedAsync(decider);
        var effects = lookup == "no-lookup" ? null
            : new Effects(lookup == "unknown" ? FormAcceptedCommandEffect.Unknown : FormAcceptedCommandEffect.Committed);
        var cancellation = new FormAcceptedCommandCancellation(harness.Store, new AccessProvider(decider), new FixedClock(Now), effects);

        var late = await cancellation.CancelAsync(new(Tenant, Admin), commandId);

        Assert.Equal((FormAcceptedCommandCancelStatus.TooLate, "records.command.cancel_too_late"), (late.Status, late.Code));
        Assert.Equal(FormAcceptedCommandState.Quarantined, Assert.Single(harness.State.Commands.Values).Command.State);
        Assert.Equal(2, harness.State.Audits.Count);
        if (effects is not null) Assert.Equal(commandId, Assert.Single(effects.Queries).Command.CommandId);
    }

    [Fact(DisplayName = "R2-M1: once the host establishes that a quarantined command's effect did not and cannot commit, a granted cancel abandons it, audited after the claim, and a retry replays")]
    public async Task QuarantinedCommandWithAnAbsentEffectIsAbandoned()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var (harness, commandId) = await QuarantinedAsync(decider);
        var effects = new Effects(FormAcceptedCommandEffect.Absent);
        var cancellation = new FormAcceptedCommandCancellation(harness.Store, new AccessProvider(decider), new FixedClock(Now), effects);

        var won = await cancellation.CancelAsync(new(Tenant, Admin), commandId);
        var retry = await cancellation.CancelAsync(new(Tenant, Admin), commandId);

        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, "records.command.cancelled", false), (won.Status, won.Code, won.Replayed));
        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, true), (retry.Status, retry.Replayed));
        var command = Assert.Single(harness.State.Commands.Values).Command;
        Assert.Equal(FormAcceptedCommandState.Cancelled, command.State);
        Assert.Equal(Admin, command.StateActorId);
        Assert.Equal(AuthoritySnapshotEncoding.Encode(won.Decision!), command.StateSnapshot.ToArray());
        var audit = System.Text.Json.Nodes.JsonNode.Parse(Assert.Single(harness.State.Audits.Values, envelope => envelope.ActorId == Admin).Payload.Span)!;
        Assert.Equal("accepted-command-cancelled", audit["op"]!.GetValue<string>());
        Assert.True(audit["after_claim"]!.GetValue<bool>());
        // Acceptance, quarantine and abandonment; the replayed cancel added none and asked the lookup only once.
        Assert.Equal(3, harness.State.Audits.Count);
        Assert.Single(effects.Queries);
        Assert.Empty(harness.State.Pending);
        Assert.Equal(FormProjectionStatus.Cancelled, Assert.Single(harness.State.Idempotency.Values).Receipt.ProjectionStatus);
    }

    [Fact(DisplayName = "R2-M1: a receipt lookup that fails leaves the quarantined command unchanged and the cancel retryable")]
    public async Task FailingEffectLookupIsRetryable()
    {
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var (harness, commandId) = await QuarantinedAsync(decider);
        var effects = new Effects(FormAcceptedCommandEffect.Absent) { FailNext = true };
        var cancellation = new FormAcceptedCommandCancellation(harness.Store, new AccessProvider(decider), new FixedClock(Now), effects);

        await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(
            async () => await cancellation.CancelAsync(new(Tenant, Admin), commandId));
        var unchanged = Assert.Single(harness.State.Commands.Values).Command.State;
        var retried = await cancellation.CancelAsync(new(Tenant, Admin), commandId);

        Assert.Equal(FormAcceptedCommandState.Quarantined, unchanged);
        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, false), (retried.Status, retried.Replayed));
        Assert.Equal(3, harness.State.Audits.Count);
    }

    [Fact(DisplayName = "R3-L1: completing a quarantined row directly is refused; the command stays quarantined and nothing is added")]
    public async Task DirectCompletionOfAQuarantinedRowIsRefused()
    {
        var (harness, commandId) = await QuarantinedAsync(new Decider());

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Store.CompleteProjectionAsync(commandId, []));

        Assert.Equal(FormAcceptedCommandState.Quarantined, Assert.Single(harness.State.Commands.Values).Command.State);
        Assert.Single(harness.State.Pending);
        Assert.Equal(2, harness.State.Audits.Count);
        Assert.Equal(FormProjectionStatus.Pending, Assert.Single(harness.State.Idempotency.Values).Receipt.ProjectionStatus);
    }

    public static TheoryData<string> UnresolvedEffects => ["unknown", "absent"];

    [Theory(DisplayName = "R3-L1: a quarantined command is not resolved unless the receipt lookup finds its effect committed; nothing changes")]
    [MemberData(nameof(UnresolvedEffects))]
    public async Task QuarantinedCommandIsNotResolvedWithoutACommittedEffect(string lookup)
    {
        var (harness, commandId) = await QuarantinedAsync(new Decider());
        var answer = lookup == "unknown" ? FormAcceptedCommandEffect.Unknown : FormAcceptedCommandEffect.Absent;
        var effects = new Effects(answer);

        var result = await new FormAcceptedCommandResolution(harness.Store, effects, new FixedClock(Now), Worker).ResolveAsync(Tenant, commandId);

        Assert.Equal(new FormAcceptedCommandResolveResult(FormAcceptedCommandResolveStatus.Unresolved, answer), result);
        Assert.Equal(FormAcceptedCommandState.Quarantined, Assert.Single(harness.State.Commands.Values).Command.State);
        Assert.Equal(2, harness.State.Audits.Count);
        Assert.Single(effects.Queries);
    }

    [Fact(DisplayName = "R3-L1: a quarantined command whose effect the receipt lookup finds committed is completed with an audit in the same transition; a retry changes nothing")]
    public async Task QuarantinedCommandWithACommittedEffectIsResolved()
    {
        var (harness, commandId) = await QuarantinedAsync(new Decider());
        var effects = new Effects(FormAcceptedCommandEffect.Committed);
        var resolution = new FormAcceptedCommandResolution(harness.Store, effects, new FixedClock(Now), Worker);

        var resolved = await resolution.ResolveAsync(Tenant, commandId);
        var retry = await resolution.ResolveAsync(Tenant, commandId);

        Assert.Equal(new FormAcceptedCommandResolveResult(FormAcceptedCommandResolveStatus.Completed, FormAcceptedCommandEffect.Committed), resolved);
        Assert.Equal(FormAcceptedCommandResolveStatus.NotQuarantined, retry.Status);
        var command = Assert.Single(harness.State.Commands.Values).Command;
        Assert.Equal((FormAcceptedCommandState.Completed, Worker), (command.State, command.StateActorId));
        var audit = System.Text.Json.Nodes.JsonNode.Parse(Assert.Single(harness.State.Audits.Values,
            envelope => System.Text.Json.Nodes.JsonNode.Parse(envelope.Payload.Span)!["op"]?.GetValue<string>() == "accepted-command-resolved").Payload.Span)!;
        Assert.Equal("records.command.binding_mismatch", audit["code"]!.GetValue<string>());
        Assert.True(audit["after_claim"]!.GetValue<bool>());
        // Acceptance, quarantine and resolution; the retry asked nothing and added nothing.
        Assert.Equal(3, harness.State.Audits.Count);
        Assert.Single(effects.Queries);
        Assert.Empty(harness.State.Pending);
        Assert.Equal(FormProjectionStatus.Complete, Assert.Single(harness.State.Idempotency.Values).Receipt.ProjectionStatus);
    }

    [Fact(DisplayName = "R3-L1: resolving a command that is not quarantined, or another tenant's, asks nothing and changes nothing")]
    public async Task ResolvingAnUnquarantinedOrForeignCommandChangesNothing()
    {
        var decider = new Decider();
        var (pendingHarness, pendingId) = await AcceptedAsync(decider);
        var (quarantinedHarness, quarantinedId) = await QuarantinedAsync(decider);
        var effects = new Effects(FormAcceptedCommandEffect.Committed);

        var pending = await new FormAcceptedCommandResolution(pendingHarness.Store, effects, new FixedClock(Now), Worker).ResolveAsync(Tenant, pendingId);
        var foreign = await new FormAcceptedCommandResolution(quarantinedHarness.Store, effects, new FixedClock(Now), Worker)
            .ResolveAsync(new TenantId("tenant-other"), quarantinedId);

        Assert.Equal(new FormAcceptedCommandResolveResult(FormAcceptedCommandResolveStatus.NotQuarantined, FormAcceptedCommandEffect.Unknown), pending);
        Assert.Equal(new FormAcceptedCommandResolveResult(FormAcceptedCommandResolveStatus.NotFound, FormAcceptedCommandEffect.Unknown), foreign);
        Assert.Empty(effects.Queries);
        Assert.Equal(FormAcceptedCommandState.Pending, Assert.Single(pendingHarness.State.Commands.Values).Command.State);
        Assert.Equal(FormAcceptedCommandState.Quarantined, Assert.Single(quarantinedHarness.State.Commands.Values).Command.State);
    }

    public static TheoryData<string> BuiltInStores => ["in-memory", "file-journal"];

    [Theory(DisplayName = "F3 (revision 16, M1): a pending cancel's audit and an abandonment's audit each carry the command id, operation id, Record Type, code and after_claim, false included, on both built-in stores and across a journal reopen")]
    [MemberData(nameof(BuiltInStores))]
    public async Task CancelAudit_PendingAndAbandon_CarryCommandOperationTypeCodeAndAfterClaim(string store)
    {
        using var directory = new TemporaryDirectory();
        var journalPath = Path.Combine(directory.Path, "submissions.hlfj");
        var state = new InMemoryFormSubmissionState();
        using var journal = store == "file-journal" ? new FileJournalFormSubmissionStore(new() { JournalPath = journalPath }) : null;
        IFormSubmissionTransactionStore submissions = (IFormSubmissionTransactionStore?)journal ?? new InMemoryFormSubmissionStore(state);
        var commands = (IFormAcceptedCommandStore)submissions;
        var decider = new Decider();
        decider.CancelGrants.Add(Admin);
        var harness = await AcceptingAsync(decider: decider, storeDecorator: _ => submissions);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        var pendingReceipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-pending"));
        var abandonReceipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-abandon"));
        var rows = await commands.LeasePendingAsync(10, FormProjectionLeaseKinds.AcceptedCommands);
        foreach (var row in rows) await submissions.ReleaseProjectionLeaseAsync(row.OutboxId);
        var pendingId = Assert.Single(rows, row => row.InstanceId == pendingReceipt.InstanceId).OutboxId;
        var abandonId = Assert.Single(rows, row => row.InstanceId == abandonReceipt.InstanceId).OutboxId;
        await commands.ClaimAcceptedCommandAsync(abandonId, Worker, Now);
        await commands.QuarantineAcceptedCommandAsync(abandonId, "records.command.binding_mismatch",
            new Harborline.Kernel.Core.KernelAuditEvidence("quarantine-audit", Worker, Now, "{\"op\":\"accepted-command-quarantined\"}"u8.ToArray()));
        var cancellation = new FormAcceptedCommandCancellation(submissions, new AccessProvider(decider), new FixedClock(Now),
            new Effects(FormAcceptedCommandEffect.Absent));

        var pending = await cancellation.CancelAsync(new(Tenant, Admin), pendingId);
        var abandoned = await cancellation.CancelAsync(new(Tenant, Admin), abandonId);
        IReadOnlyList<FormMutationAuditEnvelope> audits;
        if (journal is null) audits = state.Audits.Values.ToArray();
        else
        {
            // The round trip: the frames are written, the journal is closed, and a reopened store replays them.
            journal.Dispose();
            using var reopened = new FileJournalFormSubmissionStore(new() { JournalPath = journalPath });
            audits = await reopened.AuditsAsync();
        }

        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, false), (pending.Status, pending.Replayed));
        Assert.Equal((FormAcceptedCommandCancelStatus.Cancelled, false), (abandoned.Status, abandoned.Replayed));
        // Two acceptances, the quarantine, the pending cancel and the abandonment.
        Assert.Equal(5, audits.Count);
        AssertCancelAudit(audits, pendingId, pendingReceipt.InstanceId.ToString(), afterClaim: false);
        AssertCancelAudit(audits, abandonId, abandonReceipt.InstanceId.ToString(), afterClaim: true);
    }

    // Revision 16, line 60: the canceller's audit names the command, its original operation (the committed instance, open item
    // 3) and its Record Type, the cancelled code and after_claim, in exactly these members (R2-L5 spellings).
    private static void AssertCancelAudit(IReadOnlyList<FormMutationAuditEnvelope> audits, string commandId, string operationId, bool afterClaim)
    {
        var audit = Assert.Single(audits, envelope =>
            System.Text.Json.Nodes.JsonNode.Parse(envelope.Payload.Span)!["command_id"]?.GetValue<string>() == commandId
            && envelope.ActorId == Admin);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(audit.Payload.Span)!.AsObject();
        Assert.Equal(["after_claim", "code", "command_id", "op", "operation_id", "record_type_id"],
            payload.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("accepted-command-cancelled", payload["op"]!.GetValue<string>());
        Assert.Equal(operationId, payload["operation_id"]!.GetValue<string>());
        Assert.Equal("notes.entry", payload["record_type_id"]!.GetValue<string>());
        Assert.Equal("records.command.cancelled", payload["code"]!.GetValue<string>());
        Assert.Equal(afterClaim, payload["after_claim"]!.GetValue<bool>());
    }

    // A fresh directory under the temp path, removed with everything in it.
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"harborline-forms-cancel-audit-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    // One form_only submit accepted by alice, claimed by the worker and then quarantined (review R2-M1), with its audit.
    private static async Task<(FormEngineOrchestrationTests.Harness Harness, string CommandId)> QuarantinedAsync(Decider decider)
    {
        var (harness, commandId) = await AcceptedAsync(decider);
        await harness.Store.ClaimAcceptedCommandAsync(commandId, Worker, Now);
        var quarantined = await harness.Store.QuarantineAcceptedCommandAsync(commandId, "records.command.binding_mismatch",
            new Harborline.Kernel.Core.KernelAuditEvidence("quarantine-audit", Worker, Now, "{\"op\":\"accepted-command-quarantined\"}"u8.ToArray()));
        Assert.True(quarantined!.Changed);
        return (harness, commandId);
    }

    // The host's Records receipt lookup with a literal answer, recording each query.
    private sealed class Effects(FormAcceptedCommandEffect answer) : IFormAcceptedCommandEffects
    {
        public List<FormAcceptedCommandEffectQuery> Queries { get; } = [];
        public bool FailNext { get; set; }

        public ValueTask<FormAcceptedCommandEffect> FindAsync(FormAcceptedCommandEffectQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (!FailNext) return ValueTask.FromResult(answer);
            FailNext = false;
            return ValueTask.FromException<FormAcceptedCommandEffect>(new IOException("records store unavailable"));
        }
    }

    // One form_only submit accepted by the submitter alice and left pending (no worker).
    private static async Task<(FormEngineOrchestrationTests.Harness Harness, string CommandId)> AcceptedAsync(Decider decider)
    {
        var harness = await AcceptingAsync(decider: decider);
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        return (harness, Assert.Single(harness.State.Commands.Keys));
    }

    private static FormAcceptedCommandCancellation Cancellation(FormEngineOrchestrationTests.Harness harness, Decider decider) =>
        new(harness.Store, new AccessProvider(decider), new FixedClock(Now));

    // Forwards to the in-memory store, counting cancels and optionally failing the next one on infrastructure.
    private sealed class CountingCommands(InMemoryFormSubmissionStore inner) : IFormSubmissionTransactionStore, IFormAcceptedCommandStore
    {
        public int Cancels { get; private set; }
        public bool FailNextCancel { get; set; }

        public ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);
        public ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default) => inner.CommitAsync(commit, cancellationToken);
        public ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default) => inner.GetAsync(tenant, instanceId, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, FormProjectionLeaseKinds kinds, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, kinds, cancellationToken);
        public ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default) => inner.ReleaseProjectionLeaseAsync(outboxId, cancellationToken);
        public ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default) => inner.CompleteProjectionAsync(outboxId, skips, cancellationToken);
        public ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default) => inner.RetryProjectionAsync(outboxId, stableErrorCode, cancellationToken);
        public ValueTask<FormAcceptedRecordCommand?> GetAcceptedCommandAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default) => inner.GetAcceptedCommandAsync(tenant, commandId, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> ClaimAcceptedCommandAsync(string commandId, string workerId, DateTimeOffset at, CancellationToken cancellationToken = default) => inner.ClaimAcceptedCommandAsync(commandId, workerId, at, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RefuseAcceptedCommandAsync(string commandId, FormAcceptedCommandState expectedState, string refusalCode,
            Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) =>
            inner.RefuseAcceptedCommandAsync(commandId, expectedState, refusalCode, audit, cancellationToken);
        public ValueTask<FormProjectionEnvelope?> LeasePendingProjectionAsync(TenantId tenant, string outboxId, CancellationToken cancellationToken = default) =>
            inner.LeasePendingProjectionAsync(tenant, outboxId, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> RecordBindingFailureAsync(string commandId, string code, CancellationToken cancellationToken = default) =>
            inner.RecordBindingFailureAsync(commandId, code, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> QuarantineAcceptedCommandAsync(string commandId, string code,
            Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) =>
            inner.QuarantineAcceptedCommandAsync(commandId, code, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> AbandonQuarantinedCommandAsync(TenantId tenant, string commandId,
            Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) =>
            inner.AbandonQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);
        public ValueTask<FormAcceptedCommandTransition?> ResolveQuarantinedCommandAsync(TenantId tenant, string commandId,
            Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default) =>
            inner.ResolveQuarantinedCommandAsync(tenant, commandId, audit, cancellationToken);

        public ValueTask<FormAcceptedCommandTransition?> CancelAcceptedCommandAsync(TenantId tenant, string commandId,
            Harborline.Kernel.Core.KernelAuditEvidence audit, CancellationToken cancellationToken = default)
        {
            Cancels++;
            if (FailNextCancel)
            {
                FailNextCancel = false;
                return ValueTask.FromException<FormAcceptedCommandTransition?>(new IOException("journal unavailable"));
            }
            return inner.CancelAcceptedCommandAsync(tenant, commandId, audit, cancellationToken);
        }
    }
}
