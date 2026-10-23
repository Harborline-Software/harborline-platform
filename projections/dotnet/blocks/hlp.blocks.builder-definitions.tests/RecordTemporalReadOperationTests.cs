using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using TenantId = Harborline.Foundation.Assets.Common.TenantId;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTemporalWriteOperationTests;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>Explicit effective reads and current authority over real prepared, committed journal revisions.</summary>
public sealed class RecordTemporalReadOperationTests
{
    private static readonly RecordReadScope Scope = new(RecordTemporalWriteOperationTests.Tenant, Actor);
    private static int Value(RecordReadResult record)
    {
        using var body = JsonDocument.Parse(record.Body);
        return body.RootElement.GetProperty("value").GetInt32();
    }
    private static RecordTemporalReadResult Found(RecordTemporalReadOutcome result, long revision, int value)
    {
        Assert.Equal(RecordTemporalReadStatus.Found, result.Status);
        Assert.Null(result.Refusal);
        Assert.NotNull(result.Decision);
        var record = Assert.IsType<RecordTemporalReadResult>(result.Record);
        Assert.Equal(revision, record.Temporal.Revision);
        Assert.Equal(value, Value(record.Record));
        Assert.Equal("temporal.entry", record.Record.RecordTypeId);
        Assert.True(record.Record.RecordedAt.EqualsExact(GateAt));
        return record;
    }
    private static void Reset(Fixture fixture)
    {
        fixture.Access.Requests.Clear();
        fixture.Access.Evidence.Clear();
        fixture.Definitions.Pins.Clear();
        fixture.Schemas.RegisteredText.Clear();
    }
    private static async Task<(RecordTemporalReceipt First, RecordTemporalReceipt Second, RecordTemporalReceipt Third)>
        Corrections(Fixture fixture)
    {
        var first = Committed(await fixture.CreateEvent());
        var second = Committed(await fixture.AmendEvent(first));
        var third = Committed(await fixture.AmendEvent(second, "r3", "2031-05-01T00:00:00-04:00",
            "2031-06-01T00:00:00-04:00", 15, "correct May"));
        return (first, second, third);
    }

    [Fact]
    public async Task Future_effective_revision_does_not_replace_earlier_current_state()
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        fixture.AuthorityClock.Now = AuthorityAt.AddHours(-1);
        var second = Committed(await fixture.AmendEvent(first));
        Assert.Equal(2, second.Temporal.Revision);
        Assert.True(second.Temporal.AuthorityRecordedAt < first.Temporal.AuthorityRecordedAt);
        var before = fixture.Bytes();
        Reset(fixture);
        fixture.OperationClock.Now = Date("2031-05-15T12:00:00-04:00");
        var clockReads = fixture.OperationClock.Reads;
        var current = await fixture.Reader.ReadAsync(Scope, new(first.Receipt.RecordId));
        Assert.True(current.Found);
        Assert.Equal(10, Value(current.Record!));
        Assert.Equal(clockReads + 1, fixture.OperationClock.Reads);
        Assert.True(Assert.Single(fixture.Access.Requests).At.EqualsExact(Date("2031-05-15T12:00:00-04:00")));
        Reset(fixture);
        var may = Found(await fixture.Reader.ReadAtAsync(Scope,
            new(first.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))), 1, 10);
        Assert.Equal(first.BindingHash, may.BindingHash);
        fixture.OperationClock.Now = Date("2031-06-01T00:00:00-04:00");
        var june = await fixture.Reader.ReadAsync(Scope, new(first.Receipt.RecordId));
        Assert.Equal(20, Value(june.Record!));
        Found(await fixture.Reader.ReadAtAsync(Scope,
            new(first.Receipt.RecordId, Date("2031-06-01T00:00:00-04:00"))), 2, 20);
        Assert.Equal(before, fixture.Bytes());
    }

    [Fact]
    public async Task Correction_predecessor_is_not_superseded_visible_revision()
    {
        using var fixture = await Fixture.Create();
        var chain = await Corrections(fixture);
        var before = fixture.Bytes();
        Assert.Equal(new RecordTemporalBasis(2, chain.Second.BindingHash), chain.Third.Temporal.Predecessor);
        var segment = Assert.Single(chain.Third.Temporal.Supersedes);
        Assert.Equal(new RecordTemporalBasis(1, chain.First.BindingHash), segment.Revision);
        Assert.True(Date("2031-05-01T04:00:00+00:00").EqualsExact(segment.EffectiveFrom));
        Assert.True(Date("2031-06-01T04:00:00+00:00").EqualsExact(segment.EffectiveTo!.Value));
        var reopened = new FileJournalRecordWriteStore(fixture.PathName);
        var reader = new RecordReadOperation(reopened, new AccessProvider(fixture.Access), fixture.Types, fixture.OperationClock);
        Found(await reader.ReadAtAsync(Scope, new(chain.First.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))), 3, 15);
        Found(await reader.ReadAtAsync(Scope, new(chain.First.Receipt.RecordId, Date("2031-06-01T00:00:00-04:00"))), 2, 20);
        var history = await reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.Found, history.Status);
        Assert.Equal(new[] { 1L, 2L, 3L }, history.Records.Select(value => value.Temporal.Revision));
        Assert.Equal(new[] { 10, 20, 15 }, history.Records.Select(value => Value(value.Record)));
        Assert.Equal(new[] { chain.First.BindingHash, chain.Second.BindingHash, chain.Third.BindingHash },
            history.Records.Select(value => value.BindingHash));
        Assert.Null(history.Records[0].Temporal.EffectiveTo);
        Assert.True(Date("2031-04-01T00:00:00-04:00").EqualsExact(history.Records[0].Temporal.EffectiveFrom));
        Assert.Null(history.Records[1].Temporal.EffectiveTo);
        Assert.True(Date("2031-06-01T00:00:00-04:00").EqualsExact(history.Records[1].Temporal.EffectiveFrom));
        Assert.Equal(before, fixture.Bytes());
    }

    [Fact]
    public async Task Disjoint_periods_and_gaps_are_explicit_without_legacy_fallback()
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent("island-a", to: "2031-05-01T00:00:00-04:00"));
        var second = Committed(await fixture.AmendEvent(first, "island-b", to: "2031-07-01T00:00:00-04:00"));
        Assert.Empty(second.Temporal.Supersedes);
        Found(await fixture.Reader.ReadAtAsync(Scope,
            new(first.Receipt.RecordId, Date("2031-04-15T00:00:00-04:00"))), 1, 10);
        Found(await fixture.Reader.ReadAtAsync(Scope,
            new(first.Receipt.RecordId, Date("2031-06-01T00:00:00-04:00"))), 2, 20);
        foreach (var at in new[] { "2031-03-31T23:59:59-04:00", "2031-05-15T00:00:00-04:00", "2031-07-01T00:00:00-04:00" })
        {
            Reset(fixture);
            var gap = await fixture.Reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, Date(at)));
            Assert.Equal(RecordTemporalReadStatus.NoEffectiveRevision, gap.Status);
            Assert.Null(gap.Record);
            Assert.Null(gap.Refusal);
            Assert.Null(gap.Decision);
            fixture.OperationClock.Now = Date(at);
            var old = await fixture.Reader.ReadAsync(Scope, new(first.Receipt.RecordId));
            Assert.Equal("records.read.not_found", old.Refusal);
            Assert.Null(old.Record);
            Assert.Null(old.Decision);
            Assert.Empty(fixture.Access.Requests);
            Assert.Empty(fixture.Definitions.Pins);
        }
        var ordinary = await fixture.Writer.CreateAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor), new(RecordTemporalWriteOperationTests.Type, "ordinary", Body(90)));
        Assert.True(ordinary.Committed);
        Reset(fixture);
        var unsupported = await fixture.Reader.ReadAtAsync(Scope, new(ordinary.Receipt!.RecordId, GateAt));
        Assert.Equal(RecordTemporalReadStatus.UnsupportedLegacyBasis, unsupported.Status);
        Assert.Null(unsupported.Record);
        var unsupportedHistory = await fixture.Reader.ReadHistoryAsync(Scope, ordinary.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.UnsupportedLegacyBasis, unsupportedHistory.Status);
        Assert.Empty(unsupportedHistory.Records);
        Assert.Empty(unsupportedHistory.Decisions);
        Assert.Equal(RecordTemporalReadStatus.NotFound,
            (await fixture.Reader.ReadAtAsync(Scope, new("missing-record", GateAt))).Status);
        Assert.Equal(RecordTemporalReadStatus.NotFound,
            (await fixture.Reader.ReadHistoryAsync(Scope, "missing-record")).Status);
        Assert.Empty(fixture.Access.Requests);
        Assert.Empty(fixture.Definitions.Pins);
    }

    [Fact]
    public async Task Equivalent_offsets_compare_by_instant_but_keep_exact_request_identity()
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var before = fixture.Bytes();
        var retry = await fixture.CreateEvent(from: "2031-04-01T04:00:00+00:00");
        Assert.Equal("records.write.replay_conflict", retry.Refusal!.Code);
        Assert.Equal(before, fixture.Bytes());
        var local = Found(await fixture.Reader.ReadAtAsync(Scope,
            new(first.Receipt.RecordId, Date("2031-04-01T00:00:00-04:00"))), 1, 10);
        var utc = Found(await fixture.Reader.ReadAtAsync(Scope,
            new(first.Receipt.RecordId, Date("2031-04-01T04:00:00+00:00"))), 1, 10);
        Assert.Equal(local.BindingHash, utc.BindingHash);
        Assert.True(local.Temporal.EffectiveFrom.EqualsExact(Date("2031-04-01T00:00:00-04:00")));
        var correction = Committed(await fixture.AmendEvent(first, from: "2031-05-01T06:00:00+02:00",
            to: "2031-06-01T06:00:00+02:00", reason: "\u001c"));
        Assert.Equal("\u001c", correction.Temporal.Reason);
        Assert.True(Date("2031-05-01T06:00:00+02:00").EqualsExact(correction.Temporal.EffectiveFrom));
        Assert.True(Date("2031-06-01T06:00:00+02:00").EqualsExact(correction.Temporal.EffectiveTo!.Value));
        var superseded = Assert.Single(correction.Temporal.Supersedes);
        Assert.True(Date("2031-05-01T04:00:00+00:00").EqualsExact(superseded.EffectiveFrom));
        Assert.True(Date("2031-06-01T04:00:00+00:00").EqualsExact(superseded.EffectiveTo!.Value));
        foreach (var blank in new[] { "\u00a0", "\u3000" })
        {
            var refused = await fixture.AmendEvent(correction, "blank-" + (int)blank[0], reason: blank);
            Assert.Equal(RecordTemporalWriteStatus.InvalidTemporalInput, refused.Status);
            Assert.Equal(new[] { "/reason" }, refused.Pointers);
        }
        var offsetRetry = await fixture.AmendEvent(first, from: "2031-05-01T04:00:00+00:00",
            to: "2031-06-01T04:00:00+00:00", reason: "\u001c");
        Assert.Equal("records.write.replay_conflict", offsetRetry.Refusal!.Code);
        Assert.Equal(2, fixture.Entries());
    }

    [Fact]
    public async Task Untimed_old_point_read_needs_clock_only_for_temporal_identity()
    {
        using var fixture = await Fixture.Create();
        var temporal = Committed(await fixture.CreateEvent());
        var ordinary = await fixture.Writer.CreateAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor), new(RecordTemporalWriteOperationTests.Type, "ordinary", Body(90)));
        var before = fixture.Bytes();
        var clockless = new FileJournalRecordWriteStore(fixture.PathName);
        Assert.Equal(90, Value(newRecord((await clockless.FindAsync(RecordTemporalWriteOperationTests.Tenant, ordinary.Receipt!.RecordId))!)));
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => clockless.FindAsync(RecordTemporalWriteOperationTests.Tenant, temporal.Receipt.RecordId).AsTask());
        Assert.Equal("Temporal point reads require an explicit authority clock.", error.Message);
        Assert.Equal(RecordTemporalLookupKind.Temporal,
            (await clockless.FindAtAsync(RecordTemporalWriteOperationTests.Tenant, temporal.Receipt.RecordId, GateAt)).Kind);
        Assert.Single((await clockless.HistoryAsync(RecordTemporalWriteOperationTests.Tenant, temporal.Receipt.RecordId)).Versions);
        Assert.Equal(temporal.BindingHash,
            (await clockless.FindTemporalOperationAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor, "r1")))!.Receipt!.BindingHash);
        var clock = new Clock(Date("2031-05-15T00:00:00-04:00"));
        var clocked = new FileJournalRecordWriteStore(fixture.PathName, clock);
        Assert.Equal(10, Value(newRecord((await clocked.FindAsync(RecordTemporalWriteOperationTests.Tenant, temporal.Receipt.RecordId))!)));
        Assert.Equal(1, clock.Reads);
        _ = await clocked.FindAsync(RecordTemporalWriteOperationTests.Tenant, ordinary.Receipt.RecordId);
        Assert.Equal(1, clock.Reads);
        var reader = new RecordReadOperation(clockless, new AccessProvider(fixture.Access), fixture.Types, clock);
        var current = await reader.ReadAsync(Scope, new(temporal.Receipt.RecordId));
        Assert.Equal(10, Value(current.Record!));
        Assert.Equal(2, clock.Reads);
        Assert.Equal(before, fixture.Bytes());
        static RecordReadResult newRecord(RecordWriteRow row)
            => new(row.RecordId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, row.Body, row.RecordedAt);
    }

    [Fact]
    public async Task Old_only_reader_and_current_projection_remain_compatible()
    {
        using var fixture = await Fixture.Create();
        var accepted = await fixture.Writer.CreateAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor), new(RecordTemporalWriteOperationTests.Type, "old-port-row", Body(42)));
        var oldOnly = new OldOnly(fixture.Storage.Inner);
        var reader = new RecordReadOperation(oldOnly, new AccessProvider(fixture.Access), fixture.Types, fixture.OperationClock);
        Reset(fixture);
        var read = await reader.ReadAsync(Scope, new(accepted.Receipt!.RecordId));
        Assert.True(read.Found);
        Assert.Equal(42, Value(read.Record!));
        Assert.Equal(accepted.Receipt.RecordId, read.Record!.RecordId);
        Assert.True(read.Record.RecordedAt.EqualsExact(GateAt));
        Assert.Equal(1, oldOnly.Calls);
        Reset(fixture);
        Assert.Equal(RecordTemporalReadStatus.TemporalStoreUnavailable,
            (await reader.ReadAtAsync(Scope, new(accepted.Receipt.RecordId, GateAt))).Status);
        var history = await reader.ReadHistoryAsync(Scope, accepted.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.TemporalStoreUnavailable, history.Status);
        Assert.Empty(history.Records);
        Assert.Empty(history.Decisions);
        Assert.Equal(1, oldOnly.Calls);
        Assert.Empty(fixture.Access.Requests);
        Assert.Empty(fixture.Definitions.Pins);
        fixture.Access.Allow = false;
        var denied = await reader.ReadAsync(Scope, new(accepted.Receipt.RecordId));
        Assert.Equal("TemporalDenied", denied.Refusal);
        Assert.Null(denied.Record);
        Assert.False(denied.Decision!.Allowed);
        Assert.Equal(new[] { "Body", "DefinitionDigest", "RecordId", "RecordTypeId", "RecordedAt", "Schema", "Version" },
            typeof(RecordReadResult).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "BindingHash", "Record", "Temporal" },
            typeof(RecordTemporalReadResult).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Selected_revision_uses_its_own_exact_immutable_publication()
    {
        using var fixture = await Fixture.Create();
        var key = new DefinitionKey(RecordTemporalWriteOperationTests.Tenant, DefinitionKind.Records, RecordTemporalWriteOperationTests.Type);
        var original = (await fixture.RawDefinitions.ResolvePublishedAsync(new(key, "1.0.0")))!;
        var originalBody = original.Document.BodyJson;
        Assert.Contains("\"field_key\":\"status\"", originalBody, StringComparison.Ordinal);
        await fixture.RawDefinitions.SaveDraftAsync(new(key, "opaque-A", "1.0.1", originalBody), 2, "opaque-a-draft");
        await fixture.Publisher.PublishAsync(RecordTemporalWriteOperationTests.Tenant, RecordTemporalWriteOperationTests.Type, "opaque-A", 3, "opaque-a-publish");
        await fixture.RawDefinitions.SaveDraftAsync(new(key, "opaque-B", "1.0.2", originalBody), 4, "opaque-b-draft");
        await fixture.Publisher.PublishAsync(RecordTemporalWriteOperationTests.Tenant, RecordTemporalWriteOperationTests.Type, "opaque-B", 5, "opaque-b-publish");
        var variantBody = originalBody.Replace("\"field_key\":\"status\"", "\"field_key\":\"alternate_status\"", StringComparison.Ordinal);
        await fixture.RawDefinitions.SaveDraftAsync(new(key, "opaque-C", "1.0.3", variantBody), 6, "opaque-c-draft");
        await fixture.Publisher.PublishAsync(RecordTemporalWriteOperationTests.Tenant, RecordTemporalWriteOperationTests.Type, "opaque-C", 7, "opaque-c-publish");
        var registry = new IndependentIds(new InMemorySchemaRegistry(fieldKindRuntime: fixture.Kinds));
        var types = Types(fixture, registry);
        var writer = new RecordWriteOperation(new AccessProvider(fixture.Access), types, registry, fixture.Storage,
            fixture.Storage, fixture.OperationClock, fixture.Kinds, fixture.Domains, fixture.Storage, fixture.Storage, fixture.Storage);
        var first = Committed(await writer.CreateEffectiveAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor),
            new(RecordTemporalWriteOperationTests.Type, "pin-a", Body(10), Date("2031-04-01T00:00:00-04:00"), null, "opaque-A")));
        var second = Committed(await writer.AmendAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor), new(first.Receipt.RecordId, "pin-b",
            Basis(first), Body(20), Date("2031-06-01T00:00:00-04:00"), null, "use B", "opaque-B")));
        var third = Committed(await writer.AmendAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor), new(first.Receipt.RecordId, "pin-c",
            Basis(second), Body(30, key: "alternate_status"), Date("2031-07-01T00:00:00-04:00"), null, "use C", "opaque-C")));
        // These identities are literal provider contracts, not values copied out of the producer's read result.
        Assert.Equal("schema-provider:status-v1", first.Receipt.Schema.Value);
        Assert.Equal("schema-provider:status-v1", second.Receipt.Schema.Value);
        Assert.Equal("schema-provider:alternate-status-v1", third.Receipt.Schema.Value);
        Assert.Equal(first.Receipt.DefinitionDigest, second.Receipt.DefinitionDigest);
        Assert.NotEqual(first.Receipt.DefinitionDigest, third.Receipt.DefinitionDigest);
        var durable = await fixture.Storage.Inner.HistoryAsync(RecordTemporalWriteOperationTests.Tenant, first.Receipt.RecordId);
        Assert.Equal(new[] { "opaque-A", "opaque-B", "opaque-C" }, durable.Versions.Select(value => value.Row.PublicationVersionId));
        Assert.Equal(new[] { "1.0.1", "1.0.2", "1.0.3" }, durable.Versions.Select(value => value.Row.Version));
        var before = fixture.Bytes();
        var heads = fixture.Definitions.Heads;
        foreach (var cold in new[] { false, true })
        {
            ISchemaRegistry current = cold ? new IndependentIds(new InMemorySchemaRegistry(fieldKindRuntime: fixture.Kinds)) : registry;
            var observed = (IndependentIds)current;
            var reader = new RecordReadOperation(new FileJournalRecordWriteStore(fixture.PathName),
                new AccessProvider(fixture.Access), Types(fixture, current), fixture.OperationClock);
            fixture.Definitions.Pins.Clear();
            observed.Text.Clear();
            var may = Found(await reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))), 1, 10);
            var june = Found(await reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, Date("2031-06-15T00:00:00-04:00"))), 2, 20);
            var july = Found(await reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, Date("2031-07-15T00:00:00-04:00"))), 3, 30);
            Assert.Equal(new[] { "opaque-A", "opaque-B", "opaque-C" }, fixture.Definitions.Pins.Select(value => value.VersionId));
            Assert.Equal("schema-provider:status-v1", may.Record.Schema.Value);
            Assert.Equal("schema-provider:status-v1", june.Record.Schema.Value);
            Assert.Equal("schema-provider:alternate-status-v1", july.Record.Schema.Value);
            Assert.Equal(3, observed.Text.Count);
            Assert.Equal(observed.Text[0], observed.Text[1]);
            Assert.NotEqual(observed.Text[1], observed.Text[2]);
            Assert.Equal(heads, fixture.Definitions.Heads);
            fixture.Definitions.Pins.Clear();
            observed.Text.Clear();
            var history = await reader.ReadHistoryAsync(Scope, first.Receipt.RecordId);
            Assert.Equal(RecordTemporalReadStatus.Found, history.Status);
            Assert.Equal(new[] { "opaque-A", "opaque-B", "opaque-C" }, fixture.Definitions.Pins.Select(value => value.VersionId));
            Assert.Equal(new[] { "schema-provider:status-v1", "schema-provider:status-v1", "schema-provider:alternate-status-v1" },
                history.Records.Select(value => value.Record.Schema.Value));
            Assert.Equal(3, observed.Text.Count);
            Assert.Equal(heads, fixture.Definitions.Heads);
        }
        Assert.Equal(before, fixture.Bytes());
    }

    [Fact]
    public async Task History_access_is_current_per_revision_and_all_or_nothing()
    {
        using var fixture = await Fixture.Create();
        var chain = await Corrections(fixture);
        var before = fixture.Bytes();
        fixture.OperationClock.Now = Date("2040-08-09T10:11:12+03:00");
        Reset(fixture);
        fixture.Access.Predicate = request => request.Principal == Actor
            && request.Record.Fields["value"]!.GetValue<int>() != 20;
        var reads = fixture.OperationClock.Reads;
        var denied = await fixture.Reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.Refused, denied.Status);
        Assert.Equal("TemporalDenied", denied.Refusal);
        Assert.Empty(denied.Records);
        var decision = Assert.Single(denied.Decisions);
        Assert.False(decision.Allowed);
        Assert.Equal(2, fixture.Access.Requests.Count);
        Assert.Equal(reads + 1, fixture.OperationClock.Reads);
        Assert.Empty(fixture.Schemas.RegisteredText);
        Assert.Empty(fixture.Definitions.Pins);
        Assert.All(fixture.Access.Requests, request =>
        {
            Assert.True(Date("2040-08-09T10:11:12+03:00").EqualsExact(request.At));
            Assert.Equal("records:read", request.Operation);
            Assert.Equal(chain.First.Receipt.RecordId, request.Record.Id);
            Assert.Equal(RecordTemporalWriteOperationTests.Type, request.Record.RecordTypeId);
        });
        var serialized = JsonSerializer.Serialize(denied);
        Assert.DoesNotContain("\"Body\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(chain.First.BindingHash, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(chain.Third.BindingHash, serialized, StringComparison.Ordinal);
        Reset(fixture);
        var otherActor = await fixture.Reader.ReadHistoryAsync(new(RecordTemporalWriteOperationTests.Tenant, "reader-without-access"), chain.First.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.Refused, otherActor.Status);
        Assert.Empty(otherActor.Records);
        Assert.Single(otherActor.Decisions);
        Assert.Empty(fixture.Definitions.Pins);
        Reset(fixture);
        var otherTenant = await fixture.Reader.ReadHistoryAsync(new("tenant-b", Actor), chain.First.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.NotFound, otherTenant.Status);
        Assert.Empty(otherTenant.Records);
        Assert.Empty(otherTenant.Decisions);
        Assert.Empty(fixture.Access.Requests);
        fixture.Access.Predicate = null;
        Reset(fixture);
        reads = fixture.OperationClock.Reads;
        var success = await fixture.Reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.Found, success.Status);
        Assert.Equal(new[] { 10, 20, 15 }, success.Records.Select(value => Value(value.Record)));
        Assert.Equal(3, success.Decisions.Count);
        Assert.All(success.Decisions, evidence => Assert.True(evidence.Allowed));
        Assert.Equal(reads + 1, fixture.OperationClock.Reads);
        Assert.All(fixture.Access.Requests, request => Assert.True(fixture.OperationClock.Now.EqualsExact(request.At)));
        Assert.Equal(before, fixture.Bytes());
        Reset(fixture);
        var at = Found(await fixture.Reader.ReadAtAsync(Scope,
            new(chain.First.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))), 3, 15);
        Assert.True(Assert.Single(fixture.Access.Requests).At.EqualsExact(Date("2040-08-09T10:11:12+03:00")));
        Assert.True(at.Temporal.EffectiveFrom.EqualsExact(Date("2031-05-01T00:00:00-04:00")));
        fixture.Access.ForeignContext = true;
        Reset(fixture);
        var mismatch = await fixture.Reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId);
        Assert.Equal("access.decision_mismatch", mismatch.Refusal);
        Assert.Empty(mismatch.Records);
        Assert.Empty(mismatch.Decisions);
        Assert.Empty(fixture.Definitions.Pins);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("draft")]
    [InlineData("admission")]
    [InlineData("tenant")]
    [InlineData("kind")]
    [InlineData("type")]
    [InlineData("identity")]
    [InlineData("label")]
    [InlineData("digest")]
    public async Task History_bind_failure_returns_no_partial_history(string poison)
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        await fixture.PublishNext();
        var second = Committed(await fixture.AmendEvent(first));
        var before = fixture.Bytes();
        Reset(fixture);
        var heads = fixture.Definitions.Heads;
        fixture.Definitions.PinTransform = (binding, revision) =>
        {
            // Both current gates must have succeeded before the first publication lookup.
            Assert.Equal(2, fixture.Access.Requests.Count);
            if (binding.VersionId != "1.1.0") return revision;
            return poison switch
            {
                "missing" => null,
                "draft" => revision! with { Status = DefinitionStatus.Draft },
                "admission" => revision! with { Document = revision.Document with
                    { BodyJson = revision.Document.BodyJson.Replace("\"major\":1", "\"major\":2", StringComparison.Ordinal) } },
                "tenant" => revision! with { Document = revision.Document with { Key = revision.Document.Key with { Tenant = "other" } } },
                "kind" => revision! with { Document = revision.Document with { Key = revision.Document.Key with { Kind = DefinitionKind.Forms } } },
                "type" => revision! with { Document = revision.Document with { Key = revision.Document.Key with { DefinitionId = "temporal.other" } } },
                "identity" => revision! with { Document = revision.Document with { VersionId = "other-id" } },
                "label" => revision! with { Document = revision.Document with { Version = "1.9.0" } },
                "digest" => revision! with { Digest = new string('a', 64) },
                _ => throw new ArgumentOutOfRangeException(nameof(poison)),
            };
        };
        if (poison is "missing" or "draft" or "admission")
        {
            var outcome = await fixture.Reader.ReadHistoryAsync(Scope, first.Receipt.RecordId);
            Assert.Equal(RecordTemporalReadStatus.Refused, outcome.Status);
            Assert.Equal("entity.validation.schema_unknown", outcome.Refusal);
            Assert.Empty(outcome.Records);
            Assert.True(Assert.Single(outcome.Decisions).Allowed);
            Assert.Equal(2, fixture.Definitions.Pins.Count);
            Assert.Single(fixture.Schemas.RegisteredText);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Reader.ReadHistoryAsync(Scope, first.Receipt.RecordId).AsTask());
            Assert.Equal(2, fixture.Definitions.Pins.Count);
            Assert.InRange(fixture.Schemas.RegisteredText.Count, 1, 2);
        }
        Assert.Equal(heads, fixture.Definitions.Heads);
        Assert.Equal(before, fixture.Bytes());
        // The durable history is still intact and was never "repaired" by a refused bind.
        fixture.Definitions.PinTransform = null;
        var durable = await fixture.Storage.Inner.HistoryAsync(RecordTemporalWriteOperationTests.Tenant, first.Receipt.RecordId);
        Assert.Equal(new[] { first.BindingHash, second.BindingHash }, durable.Versions.Select(value => value.BindingHash));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registry_failure_and_cancellation_stop_without_a_second_candidate_or_partial_history(bool cancel)
    {
        using var fixture = await Fixture.Create();
        var chain = await Corrections(fixture);
        var before = fixture.Bytes();
        using var cancellation = new CancellationTokenSource();
        var failure = cancel ? (Exception)new OperationCanceledException(cancellation.Token) : new IOException("registry fixture outage");
        var registry = new IndependentIds(new InMemorySchemaRegistry(fieldKindRuntime: fixture.Kinds))
            { Failure = failure, FailAt = 2 };
        var reader = new RecordReadOperation(fixture.Storage, new AccessProvider(fixture.Access), Types(fixture, registry), fixture.OperationClock);
        // The provider normally uses different lawful opaque IDs from the committed provider. For this fault test,
        // retain the actual committed registry's IDs so revision1 matches on its first registration.
        registry.ReturnFrom = fixture.Schemas;
        Reset(fixture);
        if (cancel)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId, cancellation.Token).AsTask());
            Assert.Same(failure, error);
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(
                () => reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId).AsTask()));
        Assert.Equal(3, fixture.Access.Requests.Count);
        Assert.Equal(2, registry.Text.Count);
        Assert.Equal(2, fixture.Definitions.Pins.Count);
        Assert.Equal(before, fixture.Bytes());
        registry.Text.Clear();
        registry.FailAt = 1;
        if (cancel)
            Assert.Same(failure, await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => reader.ReadAtAsync(Scope, new(chain.First.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00")),
                    cancellation.Token).AsTask()));
        else
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(
                () => reader.ReadAtAsync(Scope, new(chain.First.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))).AsTask()));
        Assert.Single(registry.Text);
        Assert.Equal(before, fixture.Bytes());
    }

    private static RecordTypeDefinitionStore Types(Fixture fixture, ISchemaRegistry registry, DefinitionContractWindow? window = null)
        => new(fixture.Definitions,
            new RecordTypeSchemaCompiler(new RecordsIntentValidator(), fixture.Kinds, new SharedValueDomainAdmission(), fixture.Domains),
            new RecordFieldDefaults(fixture.Kinds), registry, window ?? new(1, 0, 1),
            new(TenantId.FromString(RecordTemporalWriteOperationTests.Tenant), "type-author"), CreationRoles);

    // A concrete lawful alternate-ID provider: exact schema texts from the real compiler remain the source content.
    // Only the provider's returned IDs define stored identity; read expectations pin its two literals independently.
    private sealed class IndependentIds(ISchemaRegistry inner) : ISchemaRegistry
    {
        private readonly Dictionary<SchemaId, SchemaId> _canonical = [];
        private readonly Dictionary<SchemaId, Schema> _schemas = [];
        internal List<string> Text { get; } = [];
        internal Exception? Failure { get; set; }
        internal int FailAt { get; set; }
        internal ISchemaRegistry? ReturnFrom { get; set; }
        public async ValueTask<Schema> RegisterAsync(string text, IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text.Add(text);
            if (Text.Count == FailAt && Failure is not null) throw Failure;
            if (ReturnFrom is not null) return await ReturnFrom.RegisterAsync(text, parents, tags, cancellationToken);
            var schema = await inner.RegisterAsync(text, parents, tags, cancellationToken);
            var id = new SchemaId(text.Contains("\"alternate_status\"", StringComparison.Ordinal)
                ? "schema-provider:alternate-status-v1" : "schema-provider:status-v1");
            _canonical[id] = schema.Id;
            return _schemas[id] = schema with { Id = id };
        }
        public ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default)
            => ReturnFrom?.GetAsync(id, cancellationToken) ?? ValueTask.FromResult(_schemas.GetValueOrDefault(id));
        public ValueTask<SchemaValidationResult> ValidateAsync(SchemaId id, ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default)
            => ReturnFrom?.ValidateAsync(id, bytes, cancellationToken)
                ?? inner.ValidateAsync(_canonical[id], bytes, cancellationToken);
        public IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null, CancellationToken cancellationToken = default)
            => inner.ListAsync(tagFilter, cancellationToken);
    }

    [Fact]
    public async Task Actual_registry_size_refusal_propagates_before_any_legacy_candidate()
    {
        using var fixture = await Fixture.Create();
        var chain = await Corrections(fixture);
        var before = fixture.Bytes();
        var registry = new IndependentIds(new InMemorySchemaRegistry(
            new SchemaRegistryOptions { MaxSchemaBytes = 64 }, fixture.Kinds));
        var reader = new RecordReadOperation(fixture.Storage, new AccessProvider(fixture.Access),
            Types(fixture, registry), fixture.OperationClock);
        Reset(fixture);
        var size = await Assert.ThrowsAsync<InvalidSchemaException>(
            () => reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId).AsTask());
        Assert.Contains("exceeding the 64-byte limit.", size.Message, StringComparison.Ordinal);
        Assert.Equal(3, fixture.Access.Requests.Count);
        Assert.Single(registry.Text);
        Assert.Single(fixture.Definitions.Pins);
        Assert.Equal(before, fixture.Bytes());
        registry.Text.Clear();
        fixture.Definitions.Pins.Clear();
        await Assert.ThrowsAsync<InvalidSchemaException>(() => reader.ReadAtAsync(Scope,
            new(chain.First.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))).AsTask());
        Assert.Single(registry.Text);
        Assert.Single(fixture.Definitions.Pins);
        Assert.Equal(before, fixture.Bytes());
    }

    [Fact]
    public async Task Returned_temporal_evidence_and_bodies_are_detached()
    {
        using var fixture = await Fixture.Create();
        var chain = await Corrections(fixture);
        var before = fixture.Bytes();
        var source = new MutableTemporal(fixture.Storage.Inner);
        var reader = new RecordReadOperation(source, new AccessProvider(fixture.Access), fixture.Types, fixture.OperationClock);
        // An actual store export uses arrays for these attacks, even though arrays report IsReadOnly=true.
        // Mutating the source during the gate happens after the producer's required snapshot boundary.
        var attacked = false;
        fixture.Access.Predicate = _ =>
        {
            if (!attacked)
            {
                attacked = true;
                Assert.Single(source.Exported);
                ChangeBytes(source.Exported[0].Row.Body);
                var segments = Assert.IsAssignableFrom<IList<RecordTemporalSupersededSegment>>(source.Exported[0].Temporal.Supersedes);
                segments[0] = new(new(99, new string('a', 64)), Date("1900-01-01T00:00:00+00:00"), null);
                source.Exported[0] = source.Exported[0] with { BindingHash = new string('b', 64) };
            }
            return true;
        };
        var selected = Found(await reader.ReadAtAsync(Scope,
            new(chain.First.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))), 3, 15);
        Assert.True(attacked);
        Assert.Equal(chain.Third.BindingHash, selected.BindingHash);
        Assert.Equal(1, Assert.Single(selected.Temporal.Supersedes).Revision.Revision);
        Assert.True(Assert.Single(selected.Temporal.Supersedes).EffectiveFrom.EqualsExact(Date("2031-05-01T04:00:00+00:00")));
        ChangeBytes(selected.Record.Body);
        Attack(selected.Temporal.Supersedes, new(new(88, new string('b', 64)), GateAt, null));
        fixture.Access.Predicate = null;
        var reread = Found(await fixture.Reader.ReadAtAsync(Scope,
            new(chain.First.Receipt.RecordId, Date("2031-05-15T00:00:00-04:00"))), 3, 15);
        Assert.Equal(chain.Third.BindingHash, reread.BindingHash);
        Assert.Equal(1, Assert.Single(reread.Temporal.Supersedes).Revision.Revision);
        var history = await reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId);
        Assert.Equal(RecordTemporalReadStatus.Found, history.Status);
        Assert.Equal(new[] { 10, 20, 15 }, history.Records.Select(value => Value(value.Record)));
        Assert.Equal(3, source.Exported.Length);
        ChangeBytes(source.Exported[0].Row.Body);
        source.Exported[1] = source.Exported[1] with { Temporal = source.Exported[1].Temporal with { Revision = 77 } };
        ChangeBytes(history.Records[0].Record.Body);
        Attack(history.Records, history.Records[0] with { BindingHash = new string('c', 64) });
        Attack(history.Records[2].Temporal.Supersedes, new(new(66, new string('d', 64)), GateAt, null));
        Attack(history.Decisions, history.Decisions[1]);
        // Retry receipts have the same detached nested-list requirement as public effective/history reads.
        var receipt = Committed(await fixture.AmendEvent(chain.Second, "r3", "2031-05-01T00:00:00-04:00",
            "2031-06-01T00:00:00-04:00", 15, "correct May"));
        Attack(receipt.Temporal.Supersedes, new(new(55, new string('e', 64)), GateAt, null));
        var receiptAgain = (await fixture.Storage.Inner.FindTemporalOperationAsync(new(RecordTemporalWriteOperationTests.Tenant, Actor, "r3")))!.Receipt!;
        Assert.Equal(chain.Third.BindingHash, receiptAgain.BindingHash);
        Assert.Equal(1, Assert.Single(receiptAgain.Temporal.Supersedes).Revision.Revision);
        var historyAgain = await fixture.Reader.ReadHistoryAsync(Scope, chain.First.Receipt.RecordId);
        Assert.Equal(new[] { 1L, 2L, 3L }, historyAgain.Records.Select(value => value.Temporal.Revision));
        Assert.Equal(new[] { 10, 20, 15 }, historyAgain.Records.Select(value => Value(value.Record)));
        Assert.Equal(new[] { chain.First.BindingHash, chain.Second.BindingHash, chain.Third.BindingHash },
            historyAgain.Records.Select(value => value.BindingHash));
        Assert.Equal(before, fixture.Bytes());
        var current = await fixture.Reader.ReadAsync(Scope, new(chain.First.Receipt.RecordId));
        Assert.Equal(20, Value(current.Record!));
        ChangeBytes(current.Record!.Body);
        Assert.Equal(20, Value((await fixture.Reader.ReadAsync(Scope, new(chain.First.Receipt.RecordId))).Record!));
        Assert.Equal(before, fixture.Bytes());
        Assert.Equal(new[] { "BindingHash", "Record", "Temporal" },
            typeof(RecordTemporalReadResult).GetProperties().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Body", "DefinitionDigest", "RecordId", "RecordTypeId", "RecordedAt", "Schema", "Version" },
            typeof(RecordReadResult).GetProperties().Select(value => value.Name).Order(StringComparer.Ordinal));

        static void ChangeBytes(ReadOnlyMemory<byte> memory)
        {
            Assert.True(MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> bytes));
            Assert.True(bytes.Count > 0);
            bytes.Array![bytes.Offset] = (byte)'!';
        }
        static void Attack<T>(IReadOnlyList<T> values, T replacement)
        {
            Assert.NotEmpty(values);
            var indexed = Assert.IsAssignableFrom<IList<T>>(values);
            try { indexed[0] = replacement; }
            catch (NotSupportedException) { }
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("both")]
    [InlineData("empty")]
    [InlineData("foreign")]
    [InlineData("history-empty")]
    [InlineData("history-unknown")]
    public async Task A_custom_temporal_port_cannot_turn_inconsistent_state_into_success_or_partial_data(string fault)
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var source = new MutableTemporal(fixture.Storage.Inner);
        source.ChangeLookup = value => fault switch
        {
            "unknown" => value with { Kind = (RecordTemporalLookupKind)999 },
            "both" => value with { NonTemporal = value.Temporal!.Row },
            "empty" => value with { Temporal = null },
            "foreign" => value with { Temporal = value.Temporal! with { Row = value.Temporal.Row with { Tenant = "foreign-tenant" } } },
            _ => value,
        };
        source.ChangeHistory = value => fault switch
        {
            "history-empty" => value with { Versions = [] },
            "history-unknown" => value with { Kind = (RecordTemporalHistoryKind)999 },
            _ => value,
        };
        var reader = new RecordReadOperation(source, new AccessProvider(fixture.Access), fixture.Types, fixture.OperationClock);
        Reset(fixture);
        var before = fixture.Bytes();
        if (fault.StartsWith("history-", StringComparison.Ordinal))
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadHistoryAsync(Scope, first.Receipt.RecordId).AsTask());
        else if (fault == "foreign")
            await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, GateAt)).AsTask());
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, GateAt)).AsTask());
        Assert.Empty(fixture.Access.Requests);
        Assert.Empty(fixture.Definitions.Pins);
        Assert.Equal(before, fixture.Bytes());
    }

    [Theory]
    [InlineData("missing-publication")]
    [InlineData("bad-binding-hash")]
    [InlineData("invalid-revision")]
    [InlineData("empty-interval")]
    [InlineData("missing-evidence")]
    [InlineData("missing-supersedes")]
    [InlineData("unexpected-initial-reason")]
    [InlineData("invalid-superseded-revision")]
    public async Task Current_read_rejects_invalid_temporal_versions_before_authorization(string fault)
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var source = new MutableTemporal(fixture.Storage.Inner)
        {
            ChangeLookup = lookup => lookup with
            {
                Temporal = fault switch
                {
                    "missing-publication" => lookup.Temporal! with { Row = lookup.Temporal.Row with { PublicationVersionId = null } },
                    "bad-binding-hash" => lookup.Temporal! with { BindingHash = "invalid-hash" },
                    "invalid-revision" => lookup.Temporal! with { Temporal = lookup.Temporal.Temporal with { Revision = 0 } },
                    "empty-interval" => lookup.Temporal! with { Temporal = lookup.Temporal.Temporal with { EffectiveTo = lookup.Temporal.Temporal.EffectiveFrom } },
                    "missing-evidence" => lookup.Temporal! with { Temporal = null! },
                    "missing-supersedes" => lookup.Temporal! with { Temporal = lookup.Temporal.Temporal with { Supersedes = null! } },
                    "unexpected-initial-reason" => lookup.Temporal! with { Temporal = lookup.Temporal.Temporal with { Reason = "not an amendment" } },
                    "invalid-superseded-revision" => lookup.Temporal! with
                    {
                        Temporal = lookup.Temporal.Temporal with
                        {
                            Supersedes = [new(new(1, new string('a', 64)), GateAt, null)],
                        },
                    },
                    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
                },
            },
        };
        var reader = new RecordReadOperation(source, new AccessProvider(fixture.Access), fixture.Types, fixture.OperationClock);
        Reset(fixture);
        var before = fixture.Bytes();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync(Scope, new(first.Receipt.RecordId)).AsTask());
        Assert.Empty(fixture.Access.Requests);
        Assert.Empty(fixture.Definitions.Pins);
        Assert.Equal(before, fixture.Bytes());
    }

    [Fact]
    public async Task Read_dependency_and_same_token_cancellation_are_not_missing_state()
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var source = new MutableTemporal(fixture.Storage.Inner) { Failure = new IOException("read fixture outage") };
        var reader = new RecordReadOperation(source, new AccessProvider(fixture.Access), fixture.Types, fixture.OperationClock);
        Reset(fixture);
        Assert.Same(source.Failure, await Assert.ThrowsAsync<IOException>(
            () => reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, GateAt)).AsTask()));
        Assert.Same(source.Failure, await Assert.ThrowsAsync<IOException>(
            () => reader.ReadHistoryAsync(Scope, first.Receipt.RecordId).AsTask()));
        Assert.Empty(fixture.Access.Requests);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = source.Calls;
        var reads = fixture.OperationClock.Reads;
        var stopped = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAtAsync(Scope, new(first.Receipt.RecordId, GateAt), cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, stopped.CancellationToken);
        Assert.Equal(calls, source.Calls);
        Assert.Equal(reads, fixture.OperationClock.Reads);
        source.Failure = new OperationCanceledException(cancellation.Token);
        var running = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadHistoryAsync(Scope, first.Receipt.RecordId).AsTask());
        Assert.Same(source.Failure, running);
        Assert.Equal(cancellation.Token, running.CancellationToken);
    }

    // This adapter deliberately exposes mutable exports from the actual committed journal. It grants no authority,
    // substitutes no catalogue, and lets tests attack the producer's input/export isolation without a separate store.
    private sealed class MutableTemporal(IRecordTemporalReadStore inner) : IRecordTemporalReadStore
    {
        internal RecordTemporalVersion[] Exported { get; private set; } = [];
        internal Func<RecordTemporalLookup, RecordTemporalLookup>? ChangeLookup { get; set; }
        internal Func<RecordTemporalHistory, RecordTemporalHistory>? ChangeHistory { get; set; }
        internal Exception? Failure { get; set; }
        internal int Calls { get; private set; }
        private void Before(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; if (Failure is not null) throw Failure; }
        public ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A temporal-capable producer must use its explicit sampled instant.");
        public async ValueTask<RecordTemporalLookup> FindAtAsync(string tenant, string recordId, DateTimeOffset instant,
            CancellationToken cancellationToken = default)
        {
            Before(cancellationToken);
            var lookup = await inner.FindAtAsync(tenant, recordId, instant, cancellationToken);
            if (lookup.Temporal is not null)
            {
                Exported = [Mutable(lookup.Temporal)];
                lookup = lookup with { Temporal = Exported[0] };
            }
            return ChangeLookup?.Invoke(lookup) ?? lookup;
        }
        public async ValueTask<RecordTemporalHistory> HistoryAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
        {
            Before(cancellationToken);
            var history = await inner.HistoryAsync(tenant, recordId, cancellationToken);
            Exported = history.Versions.Select(Mutable).ToArray();
            history = history with { Versions = Exported };
            return ChangeHistory?.Invoke(history) ?? history;
        }
        public ValueTask<RecordTemporalTarget?> FindTemporalTargetAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
            => inner.FindTemporalTargetAsync(tenant, recordId, cancellationToken);
        private static RecordTemporalVersion Mutable(RecordTemporalVersion value) => value with
        {
            Row = value.Row with { Body = value.Row.Body.ToArray() },
            Temporal = value.Temporal with { Supersedes = value.Temporal.Supersedes.ToArray() },
        };
    }

    private sealed class OldOnly(IRecordReadStore inner) : IRecordReadStore
    {
        internal int Calls { get; private set; }
        public ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
        { Calls++; return inner.FindAsync(tenant, recordId, cancellationToken); }
    }
}
