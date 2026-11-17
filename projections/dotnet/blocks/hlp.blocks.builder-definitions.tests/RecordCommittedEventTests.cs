using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTemporalWriteOperationTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-616/postcommit-reactions (ADR-0038 React stage; ADR 0124 decisions 2 and 3; ADR 0131 decision 2; ADR-0154 decision 1;
/// DES-0015 records-run-3, L039, L1252): every committed write is published from the journal it committed into, after
/// commit, as one <c>records.record.created</c> or <c>records.record.updated</c> event naming the principal from the
/// committed row; a reaction creates new work only, through the same authorize, bind and validate path, keeping the source
/// <c>operation_id</c> and adding its reaction key, and never changes the source record.
/// Oracles: the literal event types and fields ADR 0124 decision 2 names; the literal principals (the fixture's caller
/// <c>operator-7</c>); the literal source operation ids and effects the tests send; key bytes hand-framed from the
/// length-framing rule <see cref="RecordWriteOperationKey.IdempotencyKey"/> documents (reaction origin: workflow key,
/// version, trigger id, source event id, then the effect's instance id, iteration, step and purpose, each its own frame);
/// the literal stored bodies the tests submitted; record and event counts as literals; the literal refusal stage and code
/// of a binding mismatch (ADR-0110 open item 7).
/// </summary>
public sealed class RecordCommittedEventTests
{
    private const string EntryType = "temporal.entry";
    private static readonly RecordReactionKey FollowUp = new("wf.follow-up", "1.0.0", "on-created");
    private static readonly RecordReactionEffect Effect1 = new("run-1", 0, "follow-up", "create");

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_create_and_its_amend_publish_created_then_updated_naming_the_caller_from_the_committed_row()
    {
        using var fixture = await Fixture.Create();
        var created = Committed(await fixture.CreateEvent());
        var amended = Committed(await fixture.AmendEvent(created));

        var events = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);

        Assert.Equal(2, events.Count);
        var (first, second) = (events[0], events[1]);
        Assert.Equal((1L, "records.record.created", "tenant-a", "temporal.entry", created.Receipt.RecordId, "1.0.0", 1L, "r1", "operator-7", (string?)null),
            (first.Position, first.EventType, first.Tenant, first.RecordTypeId, first.RecordId, first.Version, first.Revision, first.OperationId, first.Principal, first.Executor));
        Assert.Equal("8:tenant-a10:operator-72:r1", first.EventId);
        Assert.Equal((2L, "records.record.updated", created.Receipt.RecordId, 2L, "r2", "operator-7", (string?)null),
            (second.Position, second.EventType, second.RecordId, second.Revision, second.OperationId, second.Principal, second.Executor));
        Assert.Equal("8:tenant-a10:operator-72:r2", second.EventId);
        Assert.Equal(amended.Receipt.RecordId, second.RecordId);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_redelivery_from_any_earlier_position_or_after_reopening_the_journal_names_the_same_principal_and_event()
    {
        using var fixture = await Fixture.Create();
        var created = Committed(await fixture.CreateEvent());
        _ = Committed(await fixture.AmendEvent(created));

        var delivered = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);
        // A relay that crashed after commit and before recording its hand-off resumes from its last checkpoint.
        var redelivered = await new FileJournalRecordWriteStore(fixture.PathName, fixture.AuthorityClock).ReadCommittedAsync(Tenant, 0);
        var resumed = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 1);

        Assert.Equal(delivered, redelivered);
        var only = Assert.Single(resumed);
        Assert.Equal((2L, "8:tenant-a10:operator-72:r2", "operator-7"), (only.Position, only.EventId, only.Principal));
        Assert.Empty(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 2));
        Assert.Empty(await fixture.Storage.Inner.ReadCommittedAsync("tenant-b", 0));
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task Interleaved_tenants_each_read_only_their_own_events_at_their_journal_positions()
    {
        using var fixture = await Fixture.Create(otherTenant: "tenant-b");
        _ = Committed(await fixture.CreateEvent("r1"));
        // The optional status is omitted: the shared fixture's domain read authority admits tenant-a members only.
        _ = Committed(await fixture.Writer.CreateEffectiveAsync(new("tenant-b", Actor),
            new(EntryType, "r1", new JsonObject { ["value"] = 30 }, Date("2031-04-01T00:00:00-04:00"), null)));
        _ = Committed(await fixture.CreateEvent("r3", value: 40));

        var tenantA = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);
        var tenantB = await fixture.Storage.Inner.ReadCommittedAsync("tenant-b", 0);

        Assert.Equal([(1L, "tenant-a", "8:tenant-a10:operator-72:r1"), (3L, "tenant-a", "8:tenant-a10:operator-72:r3")],
            tenantA.Select(item => (item.Position, item.Tenant, item.EventId)));
        Assert.Equal([(2L, "tenant-b", "8:tenant-b10:operator-72:r1")], tenantB.Select(item => (item.Position, item.Tenant, item.EventId)));
        var resumed = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 2));
        Assert.Equal((3L, "r3"), (resumed.Position, resumed.OperationId));
        Assert.Empty(await fixture.Storage.Inner.ReadCommittedAsync("tenant-b", 2));
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_negative_position_is_refused_naming_the_position_argument()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());

        var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Storage.Inner.ReadCommittedAsync(Tenant, -1).AsTask());

        Assert.Equal("after", refusal.ParamName);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_refused_or_faulted_write_publishes_nothing_and_a_replayed_write_publishes_once()
    {
        using var fixture = await Fixture.Create();
        fixture.Access.Allow = false;
        Assert.False((await fixture.CreateEvent("denied")).Committed);
        fixture.Access.Allow = true;
        Assert.False((await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor),
            new(EntryType, "invalid", new JsonObject { ["status"] = "A" }, Date("2031-04-01T00:00:00-04:00"), null))).Committed);
        fixture.Storage.FaultAt = "audit";
        await Assert.ThrowsAsync<IOException>(() => fixture.CreateEvent("faulted"));
        fixture.Storage.FaultAt = null;
        Assert.Empty(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));

        _ = Committed(await fixture.CreateEvent());
        var replay = await fixture.CreateEvent();

        Assert.True(replay.Committed);
        Assert.Equal("r1", Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0)).OperationId);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_reaction_creates_a_new_record_keeping_the_source_operation_id_and_never_changes_the_source()
    {
        using var fixture = await Fixture.Create();
        var source = Committed(await fixture.CreateEvent(value: 10));
        var trigger = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));

        var outcome = await fixture.Writer.CreateReactionAsync(new(Tenant, trigger.Principal),
            new RecordReactionCreate(trigger, FollowUp, Effect1, EntryType, Body(5, "B")));

        Assert.True(outcome.Committed);
        Assert.False(outcome.Replayed);
        Assert.Equal("r1", outcome.Receipt!.OperationId);
        Assert.NotEqual(source.Receipt.RecordId, outcome.Receipt.RecordId);
        var events = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);
        Assert.Equal(2, events.Count);
        var reacted = events[1];
        // Current two-identity shape. The ADR-0154 shape (principal, executor, Workflow as actor) is not built; T-616/postcommit-reactions acceptance stays open.
        Assert.Equal(("records.record.created", outcome.Receipt.RecordId, "r1", "operator-7", (string?)null),
            (reacted.EventType, reacted.RecordId, reacted.OperationId, reacted.Principal, reacted.Executor));
        Assert.Equal("8:tenant-a10:operator-72:r1103:reaction:12:wf.follow-up5:1.0.010:on-created27:8:tenant-a10:operator-72:r15:run-11:09:follow-up6:create", reacted.EventId);
        // The source stays exactly as validated and committed: one version, its submitted body and its published event unchanged.
        var history = await fixture.Storage.HistoryAsync(Tenant, source.Receipt.RecordId);
        Assert.Equal(["""{"status":"A","value":10}"""], history.Versions.Select(version => Encoding.UTF8.GetString(version.Row.Body.Span)).ToArray());
        Assert.Equal(events[0], trigger);
        var stored = await fixture.Storage.FindAsync(Tenant, outcome.Receipt.RecordId);
        Assert.Equal("""{"status":"B","value":5}""", Encoding.UTF8.GetString(stored!.Body.Span));
        // Current two-identity shape. The ADR-0154 shape (principal, executor, Workflow as actor) is not built; T-616/postcommit-reactions acceptance stays open.
        Assert.Equal(("r1", "operator-7"), (stored.OperationId, stored.ActorId));
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_redelivered_event_replays_its_reaction_and_another_reaction_key_creates_its_own_record()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var trigger = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));
        var first = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, Effect1, EntryType, Body(5)));

        var again = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, Effect1, EntryType, Body(5)));
        var other = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, new RecordReactionKey("wf.follow-up", "1.0.0", "on-created-2"), Effect1, EntryType, Body(5)));
        var nextVersion = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, new RecordReactionKey("wf.follow-up", "1.0.1", "on-created"), Effect1, EntryType, Body(5)));

        Assert.True(again.Replayed);
        Assert.Equal(first.Receipt!.RecordId, again.Receipt!.RecordId);
        Assert.False(other.Replayed);
        Assert.False(nextVersion.Replayed);
        Assert.Equal(3, new[] { first.Receipt.RecordId, other.Receipt!.RecordId, nextVersion.Receipt!.RecordId }.Distinct().Count());
        var events = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);
        Assert.Equal(["r1", "r1", "r1", "r1"], events.Select(item => item.OperationId).ToArray());
        Assert.Equal("8:tenant-a10:operator-72:r1105:reaction:12:wf.follow-up5:1.0.012:on-created-227:8:tenant-a10:operator-72:r15:run-11:09:follow-up6:create", events[2].EventId);
        Assert.Equal("8:tenant-a10:operator-72:r1103:reaction:12:wf.follow-up5:1.0.110:on-created27:8:tenant-a10:operator-72:r15:run-11:09:follow-up6:create", events[3].EventId);
        // Another body under the same reaction key is the same operation with another intent.
        var conflict = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, Effect1, EntryType, Body(6)));
        Assert.Equal(("commit", "records.write.replay_conflict"), (conflict.Refusal!.Stage, conflict.Refusal.Code));
    }

    // One reacting run's effects from one event (DES-0019 K10: each effect has its own step identity and purpose) each create
    // their own record, whatever their bodies: another step, another iteration, another purpose in one step and another run
    // instance; redelivering an effect replays that effect's record only.
    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task Two_effects_of_one_reacting_run_each_create_their_own_record_and_each_replays_on_redelivery()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var trigger = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));

        var notify = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("run-1", 0, "notify", "create"), EntryType, Body(5)));
        var audit = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("run-1", 0, "audit", "create"), EntryType, Body(5)));
        var amount = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("run-1", 1, "notify", "create"), EntryType, Body(6)));
        var mirror = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("run-1", 0, "notify", "mirror"), EntryType, Body(5)));
        var rerun = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("run-2", 0, "notify", "create"), EntryType, Body(5)));
        var notifyAgain = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("run-1", 0, "notify", "create"), EntryType, Body(5)));

        Assert.Equal([false, false, false, false, false], new[] { notify, audit, amount, mirror, rerun }.Select(item => item.Replayed));
        Assert.Equal(5, new[] { notify, audit, amount, mirror, rerun }.Select(item => item.Receipt!.RecordId).Distinct().Count());
        Assert.True(notifyAgain.Replayed);
        Assert.Equal(notify.Receipt!.RecordId, notifyAgain.Receipt!.RecordId);
        Assert.Equal(6, (await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0)).Count);
    }

    // P3-2 (DES-0019 K10): the effect's instance id, iteration and step are framed one by one, not joined as
    // WorkflowStepKey.Value joins them. Instance "a", iteration 1, step "2:b" and instance "a:1", iteration 2, step "b" both
    // join to "a:1:2:b"; they are two effects, so each creates its own record and neither replays the other.
    // Oracles: the two literal keys, hand-framed from the length-framing rule; the event count as a literal.
    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task Two_effect_step_identities_that_join_to_the_same_text_each_create_their_own_record()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var trigger = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));

        var first = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("a", 1, "2:b", "create"), EntryType, Body(5)));
        var second = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, new("a:1", 2, "b", "create"), EntryType, Body(5)));

        Assert.True(first.Committed);
        Assert.Null(second.Refusal);
        Assert.False(second.Replayed);
        Assert.NotEqual(first.Receipt!.RecordId, second.Receipt!.RecordId);
        var events = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);
        Assert.Equal(3, events.Count);
        Assert.Equal("8:tenant-a10:operator-72:r193:reaction:12:wf.follow-up5:1.0.010:on-created27:8:tenant-a10:operator-72:r11:a1:13:2:b6:create", events[1].EventId);
        Assert.Equal("8:tenant-a10:operator-72:r193:reaction:12:wf.follow-up5:1.0.010:on-created27:8:tenant-a10:operator-72:r13:a:11:21:b6:create", events[2].EventId);
    }

    // P3-1 (ADR-0154 decisions 1 and 3; CWE-441): a reaction acts only for its source event's principal, with its source
    // event's executor. A direct write's event has no executor, so its reaction passes none. Any other pair is refused at
    // bind after the gate decided, as an accepted command's binding mismatch is, and nothing is written.
    // Oracles: the literal principals and worker id sent here; the literal stage and code records.command.binding_mismatch
    // (ADR-0110 open item 7); the journal bytes before the refusals.
    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_reaction_to_a_direct_event_acts_only_as_its_principal_with_no_executor_and_a_mismatch_persists_nothing()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var trigger = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));
        var before = fixture.Bytes();

        var otherPrincipal = await fixture.Writer.CreateReactionAsync(new(Tenant, "operator-8"), new(trigger, FollowUp, Effect1, EntryType, Body(5)));
        Assert.Equal("operator-8", fixture.Access.Requests.Last().Principal);
        var withExecutor = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, Effect1, EntryType, Body(5), Executor: "sys.records-worker"));

        foreach (var refused in new[] { otherPrincipal, withExecutor })
        {
            Assert.Equal(("bind", "records.command.binding_mismatch"), (refused.Refusal!.Stage, refused.Refusal.Code));
            Assert.Null(refused.Receipt);
            Assert.NotNull(refused.Decision);
        }
        Assert.Equal(before, fixture.Bytes());

        var direct = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, Effect1, EntryType, Body(5)));

        Assert.True(direct.Committed);
        Assert.Equal(2, (await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0)).Count);
    }

    // A reaction's own output event keeps the source operation id and principal, so it shares the reaction scope with its
    // source. Reacting to it is new work, never a replay of the reaction that wrote it: no key collision stops a reaction
    // chain. The cross-reaction cascade bound belongs to the Workflow event-trigger start (T-488/event-trigger-start).
    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_reaction_to_a_reactions_own_output_event_is_new_work_and_not_a_replay_of_that_reaction()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var source = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));
        var first = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(source, FollowUp, Effect1, EntryType, Body(5)));
        var output = (await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0))[1];
        Assert.Equal(("r1", "operator-7"), (output.OperationId, output.Principal));

        var chained = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(output, FollowUp, Effect1, EntryType, Body(5)));

        Assert.True(first.Committed);
        Assert.Null(chained.Refusal);
        Assert.False(chained.Replayed);
        Assert.NotEqual(first.Receipt!.RecordId, chained.Receipt!.RecordId);
        Assert.Equal(3, (await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0)).Count);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_reaction_write_is_decided_by_the_gate_and_validated_and_a_refusal_persists_nothing()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var trigger = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));
        var before = fixture.Bytes();
        fixture.Access.Allow = false;

        var denied = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, Effect1, EntryType, Body(5)));

        Assert.Equal(("authorize", "TemporalDenied"), (denied.Refusal!.Stage, denied.Refusal.Code));
        var asked = fixture.Access.Requests.Last();
        Assert.Equal(("records:write", "operator-7", "tenant-a", "temporal.entry"), (asked.Operation, asked.Principal, asked.Tenant, asked.Record.RecordTypeId));
        fixture.Access.Allow = true;

        var invalid = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, Effect1, EntryType, new JsonObject { ["status"] = "A" }));

        Assert.Equal(("validate", "entity.validation.body_invalid"), (invalid.Refusal!.Stage, invalid.Refusal.Code));
        Assert.Equal(before, fixture.Bytes());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.CreateReactionAsync(new("tenant-b", Actor),
            new(trigger, FollowUp, Effect1, EntryType, Body(5))).AsTask());
        // A reaction without its effect or its source event identity has no key of its own and is refused before any write.
        Assert.Equal("InstanceId", (await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, Effect1 with { InstanceId = " " }, EntryType, Body(5))).AsTask())).ParamName?.Split('.').Last());
        Assert.Equal("Iteration", (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, Effect1 with { Iteration = -1 }, EntryType, Body(5))).AsTask())).ParamName?.Split('.').Last());
        Assert.Equal("Step", (await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, Effect1 with { Step = "" }, EntryType, Body(5))).AsTask())).ParamName?.Split('.').Last());
        Assert.Equal("Purpose", (await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, Effect1 with { Purpose = " " }, EntryType, Body(5))).AsTask())).ParamName?.Split('.').Last());
        Assert.Equal("Effect", (await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, null!, EntryType, Body(5))).AsTask())).ParamName?.Split('.').Last());
        Assert.Equal("EventId", (await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger with { EventId = "" }, FollowUp, Effect1, EntryType, Body(5))).AsTask())).ParamName?.Split('.').Last());
        Assert.Equal(before, fixture.Bytes());
    }
}
