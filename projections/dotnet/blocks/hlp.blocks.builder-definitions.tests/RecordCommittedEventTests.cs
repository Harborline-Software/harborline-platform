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
/// <c>operator-7</c>); the literal source operation ids the tests send; key bytes hand-framed from the length-framing rule
/// <see cref="RecordWriteOperationKey.IdempotencyKey"/> documents; the literal stored bodies the tests submitted.
/// </summary>
public sealed class RecordCommittedEventTests
{
    private const string EntryType = "temporal.entry";
    private static readonly RecordReactionKey FollowUp = new("wf.follow-up", "1.0.0", "on-created");

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
            new RecordReactionCreate(trigger, FollowUp, EntryType, Body(5, "B")));

        Assert.True(outcome.Committed);
        Assert.False(outcome.Replayed);
        Assert.Equal("r1", outcome.Receipt!.OperationId);
        Assert.NotEqual(source.Receipt.RecordId, outcome.Receipt.RecordId);
        var events = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);
        Assert.Equal(2, events.Count);
        var reacted = events[1];
        Assert.Equal(("records.record.created", outcome.Receipt.RecordId, "r1", "operator-7", (string?)null),
            (reacted.EventType, reacted.RecordId, reacted.OperationId, reacted.Principal, reacted.Executor));
        Assert.Equal("8:tenant-a10:operator-72:r144:reaction:12:wf.follow-up5:1.0.010:on-created", reacted.EventId);
        // The source stays exactly as validated and committed: one version, its submitted body and its published event unchanged.
        var history = await fixture.Storage.HistoryAsync(Tenant, source.Receipt.RecordId);
        Assert.Equal(["""{"status":"A","value":10}"""], history.Versions.Select(version => Encoding.UTF8.GetString(version.Row.Body.Span)).ToArray());
        Assert.Equal(events[0], trigger);
        var stored = await fixture.Storage.FindAsync(Tenant, outcome.Receipt.RecordId);
        Assert.Equal("""{"status":"B","value":5}""", Encoding.UTF8.GetString(stored!.Body.Span));
        Assert.Equal(("r1", "operator-7"), (stored.OperationId, stored.ActorId));
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task A_redelivered_event_replays_its_reaction_and_another_reaction_key_creates_its_own_record()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var trigger = Assert.Single(await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0));
        var first = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, EntryType, Body(5)));

        var again = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, EntryType, Body(5)));
        var other = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, new RecordReactionKey("wf.follow-up", "1.0.0", "on-created-2"), EntryType, Body(5)));
        var nextVersion = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, new RecordReactionKey("wf.follow-up", "1.0.1", "on-created"), EntryType, Body(5)));

        Assert.True(again.Replayed);
        Assert.Equal(first.Receipt!.RecordId, again.Receipt!.RecordId);
        Assert.False(other.Replayed);
        Assert.False(nextVersion.Replayed);
        Assert.Equal(3, new[] { first.Receipt.RecordId, other.Receipt!.RecordId, nextVersion.Receipt!.RecordId }.Distinct().Count());
        var events = await fixture.Storage.Inner.ReadCommittedAsync(Tenant, 0);
        Assert.Equal(["r1", "r1", "r1", "r1"], events.Select(item => item.OperationId).ToArray());
        Assert.Equal("8:tenant-a10:operator-72:r146:reaction:12:wf.follow-up5:1.0.012:on-created-2", events[2].EventId);
        Assert.Equal("8:tenant-a10:operator-72:r144:reaction:12:wf.follow-up5:1.0.110:on-created", events[3].EventId);
        // Another body under the same reaction key is the same operation with another intent.
        var conflict = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, EntryType, Body(6)));
        Assert.Equal(("commit", "records.write.replay_conflict"), (conflict.Refusal!.Stage, conflict.Refusal.Code));
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

        var denied = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor), new(trigger, FollowUp, EntryType, Body(5)));

        Assert.Equal(("authorize", "TemporalDenied"), (denied.Refusal!.Stage, denied.Refusal.Code));
        var asked = fixture.Access.Requests.Last();
        Assert.Equal(("records:write", "operator-7", "tenant-a", "temporal.entry"), (asked.Operation, asked.Principal, asked.Tenant, asked.Record.RecordTypeId));
        fixture.Access.Allow = true;

        var invalid = await fixture.Writer.CreateReactionAsync(new(Tenant, Actor),
            new(trigger, FollowUp, EntryType, new JsonObject { ["status"] = "A" }));

        Assert.Equal(("validate", "entity.validation.body_invalid"), (invalid.Refusal!.Stage, invalid.Refusal.Code));
        Assert.Equal(before, fixture.Bytes());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.CreateReactionAsync(new("tenant-b", Actor),
            new(trigger, FollowUp, EntryType, Body(5))).AsTask());
    }
}
