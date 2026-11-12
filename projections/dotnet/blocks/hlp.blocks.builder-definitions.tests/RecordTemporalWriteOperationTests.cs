using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Foundation.RuleEngine.References;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using TenantId = Harborline.Foundation.Assets.Common.TenantId;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>Accepted SPEC/API controls W/A through real producer, access, schema, field and prepared journal paths.</summary>
public sealed class RecordTemporalWriteOperationTests
{
    // Oracles: RW-1 / DES-0015 records-eng-2 require authorize first. Authorized refusal literals
    // pin the pre-fix contract; journal bytes and untouched stage counters prove no persistence or target/receipt reads.
    [Theory]
    [InlineData("operation", false)]
    [InlineData("interval", false)]
    [InlineData("version", false)]
    [InlineData("body", false)]
    [InlineData("operation", true)]
    [InlineData("interval", true)]
    [InlineData("predecessor", true)]
    [InlineData("reason", true)]
    [InlineData("version", true)]
    [InlineData("body", true)]
    [Trait("Holds", "records-eng-2")]
    public async Task Temporal_request_shape_is_refused_only_after_the_gate_allows(string invalid, bool amend)
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var before = fixture.Bytes();
        var lookups = fixture.Storage.ReceiptLookups;
        var targetLookups = fixture.Storage.TargetLookups;
        fixture.Storage.Events.Clear();
        fixture.Access.Requests.Clear();
        var body = Body();
        if (invalid == "body") body["status"] = "bad\ud800";
        var operation = invalid == "operation" ? "" : "bad-input";
        var from = GateAt;
        DateTimeOffset? to = invalid == "interval" ? from : null;
        var basis = invalid == "predecessor" ? new RecordTemporalBasis(0, new string('a', 64)) : Basis(first);
        var reason = invalid == "reason" ? " " : "reviewed";
        var version = invalid == "version" ? " " : null;
        async Task<RecordTemporalWriteOutcome> Write() => amend
            ? await fixture.Writer.AmendAsync(new(Tenant, Actor),
                new(first.Receipt.RecordId, operation, basis, body, from, to, reason, version))
            : await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor),
                new(RecordTemporalWriteOperationTests.Type, operation, body, from, to, version));

        fixture.Access.Allow = false;
        var denied = await Write();
        Assert.Equal(RecordTemporalWriteStatus.Refused, denied.Status);
        Assert.Equal(("authorize", "TemporalDenied"), (denied.Refusal!.Stage, denied.Refusal.Code));
        Assert.Empty(denied.Pointers);
        Assert.False(denied.Decision!.Allowed);
        Assert.Null(denied.Receipt);
        var asked = Assert.Single(fixture.Access.Requests);
        Assert.Equal(amend ? null : RecordTemporalWriteOperationTests.Type, asked.Record.RecordTypeId);
        Assert.Empty(asked.Record.Fields);
        Assert.Equal(before, fixture.Bytes());
        Assert.Empty(fixture.Storage.Events);
        Assert.Equal(lookups, fixture.Storage.ReceiptLookups);
        Assert.Equal(targetLookups, fixture.Storage.TargetLookups);

        fixture.Access.Allow = true;
        var allowed = await Write();
        Assert.Equal(2, fixture.Access.Requests.Count);
        // The fixture decider is the independent oracle for the exact evidence returned to the host.
        Assert.Same(fixture.Access.Evidence.Last(), allowed.Decision);
        Assert.True(allowed.Decision!.Allowed);
        if (invalid is "operation" or "body")
        {
            Assert.Equal(RecordTemporalWriteStatus.Refused, allowed.Status);
            Assert.Equal("validate", allowed.Refusal!.Stage);
            Assert.Equal(invalid == "operation" ? "records.write.operation_id_invalid" : "entity.validation.body_invalid",
                allowed.Refusal.Code);
            Assert.Equal(invalid == "operation" ? new[] { "/operation_id" } : [], allowed.Pointers);
        }
        else
        {
            Assert.Equal(RecordTemporalWriteStatus.InvalidTemporalInput, allowed.Status);
            Assert.Null(allowed.Refusal);
            Assert.Equal(new[] { invalid switch
            {
                "interval" => "/effective_to", "predecessor" => "/expected_predecessor",
                "reason" => "/reason", _ => "/version",
            } }, allowed.Pointers);
        }
        Assert.Null(allowed.Receipt);
        Assert.Equal(before, fixture.Bytes());
        Assert.Empty(fixture.Storage.Events);
        Assert.Equal(lookups, fixture.Storage.ReceiptLookups);
        Assert.Equal(targetLookups, fixture.Storage.TargetLookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Holds", "records-eng-2")]
    public async Task An_async_gate_cannot_change_the_body_snapshot_that_is_validated_and_committed(bool amend)
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Access.BeforeDecision = async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
        };
        var supplied = Body(20);
        var write = amend
            ? fixture.Writer.AmendAsync(new(Tenant, Actor), new(first.Receipt.RecordId, "snapshot-amend",
                Basis(first), supplied, GateAt, null, "reviewed")).AsTask()
            : fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor),
                new(Type, "snapshot-create", supplied, GateAt, null)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Oracle: 20 was supplied when the operation began; 99 belongs only to the caller's later mutation.
        supplied["value"] = 99;
        release.SetResult();
        _ = Committed(await write);

        Assert.Equal(99, supplied["value"]!.GetValue<int>());
        Assert.Equal(20, JsonNode.Parse(fixture.Schemas.Validated.Last())!["value"]!.GetValue<int>());
        Assert.Equal(20, JsonNode.Parse(fixture.Storage.Staged!.Row.Body.Span)!["value"]!.GetValue<int>());
        Assert.Equal(2, fixture.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Holds", "records-eng-2")]
    public async Task An_amend_target_with_a_lone_surrogate_reaches_the_gate_before_encoding_refuses(bool allow)
    {
        using var fixture = await Fixture.Create();
        fixture.Access.Allow = allow;
        const string target = "bad\ud800";
        var outcome = await fixture.Writer.AmendAsync(new(Tenant, Actor), new(target, "bad-target",
            new(1, new string('a', 64)), Body(), GateAt, null, "reviewed"));

        var asked = Assert.Single(fixture.Access.Requests);
        Assert.Equal(target, asked.Record.Id);
        Assert.Null(asked.Record.RecordTypeId);
        Assert.Empty(asked.Record.Fields);
        Assert.Equal(RecordTemporalWriteStatus.Refused, outcome.Status);
        Assert.Equal(allow ? ("validate", "entity.validation.body_invalid") : ("authorize", "TemporalDenied"),
            (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.Same(Assert.Single(fixture.Access.Evidence), outcome.Decision);
        Assert.Equal(allow, outcome.Decision!.Allowed);
        Assert.Equal(0, fixture.Storage.TargetLookups);
        Assert.Equal(0, fixture.Storage.ReceiptLookups);
        Assert.Empty(fixture.Storage.Events);
        Assert.False(File.Exists(fixture.PathName));
    }

    [Fact]
    public async Task Explicit_temporal_create_preserves_three_distinct_instants()
    {
        using var fixture = await Fixture.Create();
        var declared = Date("2031-04-01T00:00:00-04:00");
        var receipt = Committed(await fixture.CreateEvent());
        Assert.Equal(1, receipt.Temporal.Revision);
        Assert.Null(receipt.Temporal.Predecessor);
        Assert.Null(receipt.Temporal.Reason);
        Assert.Empty(receipt.Temporal.Supersedes);
        Assert.True(declared.EqualsExact(receipt.Temporal.EffectiveFrom));
        Assert.True(GateAt.EqualsExact(receipt.Receipt.RecordedAt));
        Assert.True(AuthorityAt.EqualsExact(receipt.Temporal.AuthorityRecordedAt));
        Assert.Equal(1, fixture.OperationClock.Reads);
        Assert.Equal(1, fixture.AuthorityClock.Reads);
        Assert.Null((await fixture.Storage.FindTemporalOperationAsync(new(Tenant, Actor, "r1")))!.Receipt!.Temporal.Predecessor);
        var asked = Assert.Single(fixture.Access.Requests);
        Assert.Equal(("records:write", Tenant, Actor, "record", Type),
            (asked.Operation, asked.Tenant, asked.Principal, asked.Record.Kind, asked.Record.RecordTypeId));
        Assert.Empty(asked.Record.Fields);
        Assert.True(GateAt.EqualsExact(asked.At));
        var reopened = new FileJournalRecordWriteStore(fixture.PathName);
        var selected = await reopened.FindAtAsync(Tenant, receipt.Receipt.RecordId, declared);
        Assert.Equal(RecordTemporalLookupKind.Temporal, selected.Kind);
        Assert.Equal("""{"status":"A","value":10}""", Encoding.UTF8.GetString(selected.Temporal!.Row.Body.Span));
        using var journal = JsonDocument.Parse(fixture.Bytes());
        Assert.Equal("records-write-file-journal/3", journal.RootElement.GetProperty("Format").GetString());
        Assert.Equal(0, journal.RootElement.GetProperty("LegacyEntryCount").GetInt32());
        var entry = Assert.Single(journal.RootElement.GetProperty("Entries").EnumerateArray());
        Assert.True(GateAt.EqualsExact(Date(entry.GetProperty("Audit").GetProperty("RecordedAt").GetString()!)));
        Assert.Equal(1, fixture.Entries());
    }

    [Fact]
    public async Task Reason_history_defaults_do_not_select_temporal_mode()
    {
        using var fixture = await Fixture.Create(mode: false);
        var declaration = (await fixture.Publisher.GetPublishedHeadAsync(Tenant, Type))!;
        Assert.True(declaration.AmendmentPolicy!.ReasonRequired);
        Assert.Null(declaration.AmendmentPolicy.Mode);
        Assert.Equal(RecordHistoryPolicy.Enabled, declaration.HistoryPolicy);
        var outcome = await fixture.CreateEvent();
        Assert.Equal(RecordTemporalWriteStatus.PolicyNotEnabled, outcome.Status);
        Assert.Equal(new[] { "/amendment_policy", "/history_policy" }, outcome.Pointers);
        Assert.NotNull(outcome.Decision);
        Assert.False(File.Exists(fixture.PathName));
        Assert.Empty(fixture.Storage.Events);
        using var vectors = FileJournalRecordTemporalTests.Vectors();
        foreach (var policy in vectors.RootElement.GetProperty("policy_literals").EnumerateArray())
        {
            var text = policy.GetProperty("json").GetString()!;
            var json = """{"envelope":{"tenant":"tenant-a","section":"temporal"},"name":"Entry","record_type_id":"temporal.entry","fields":[],"amendment_policy":"""
                + text + "}";
            if (policy.GetProperty("expected").GetString()!.StartsWith("records.document_invalid", StringComparison.Ordinal))
                Assert.Throws<JsonException>(() => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json)));
            else
            {
                var parsed = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));
                Assert.True(parsed.AmendmentPolicy!.ReasonRequired);
                var canonical = Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(parsed));
                if (text.Contains("\"mode\"", StringComparison.Ordinal))
                {
                    Assert.Equal(RecordAmendmentMode.Supersession, parsed.AmendmentPolicy.Mode);
                    Assert.Contains("\"amendment_policy\":{\"mode\":\"supersession\",\"reason_required\":true}", canonical, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Null(parsed.AmendmentPolicy.Mode);
                    Assert.Contains("\"amendment_policy\":{\"reason_required\":true}", canonical, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public async Task Temporal_amend_keeps_logical_identity_and_exact_publication()
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        await fixture.PublishNext(gate: RecordCreationGate.FormOnly);
        var second = Committed(await fixture.AmendEvent(first, version: "1.1.0"));
        Assert.Equal(first.Receipt.RecordId, second.Receipt.RecordId);
        Assert.Equal(2, second.Temporal.Revision);
        Assert.Equal(new RecordTemporalBasis(1, first.BindingHash), second.Temporal.Predecessor);
        Assert.Equal("1.1.0", second.Receipt.Version);
        var asked = fixture.Access.Requests.Last();
        Assert.Equal(first.Receipt.RecordId, asked.Record.Id);
        Assert.Empty(asked.Record.Fields);
        var history = await fixture.Storage.HistoryAsync(Tenant, first.Receipt.RecordId);
        Assert.Equal(new[] { "1.0.0", "1.1.0" }, history.Versions.Select(value => value.Row.PublicationVersionId));
        Assert.Equal(new[] { 1L, 2L }, history.Versions.Select(value => value.Temporal.Revision));
        Assert.Equal(RecordTemporalLookupKind.NotFound,
            (await fixture.Storage.FindAtAsync("TENANT-A", first.Receipt.RecordId, GateAt)).Kind);
    }

    [Theory]
    [InlineData("equal-to", "/effective_to", false)]
    [InlineData("equal-to", "/effective_to", true)]
    [InlineData("reverse-to", "/effective_to", false)]
    [InlineData("reverse-to", "/effective_to", true)]
    [InlineData("basis-zero", "/expected_predecessor", false)]
    [InlineData("basis-zero", "/expected_predecessor", true)]
    [InlineData("basis-upper", "/expected_predecessor", false)]
    [InlineData("basis-upper", "/expected_predecessor", true)]
    [InlineData("basis-overflow", "/expected_predecessor", false)]
    [InlineData("basis-overflow", "/expected_predecessor", true)]
    [InlineData("reason-space", "/reason", false)]
    [InlineData("reason-space", "/reason", true)]
    [InlineData("reason-nbsp", "/reason", false)]
    [InlineData("reason-nbsp", "/reason", true)]
    [InlineData("version-space", "/version", false)]
    [InlineData("version-space", "/version", true)]
    [Trait("Holds", "records-eng-2")]
    public async Task Temporal_inputs_refuse_after_gate_allows_and_before_write(string fault, string pointer, bool allow)
    {
        using var fixture = await Fixture.Create();
        fixture.Access.Allow = allow;
        var from = Date("2031-04-01T00:00:00+00:00");
        var basis = new RecordTemporalBasis(fault == "basis-zero" ? 0 : fault == "basis-overflow" ? long.MaxValue : 1,
            new string(fault == "basis-upper" ? 'A' : 'a', 64));
        var request = new RecordTemporalAmendRequest("absent", "invalid-input", basis, Body(), from,
            fault == "equal-to" ? from : fault == "reverse-to" ? from.AddTicks(-1) : null,
            fault == "reason-space" ? " \t" : fault == "reason-nbsp" ? "\u00a0\u3000" : "reviewed",
            fault == "version-space" ? " " : null);
        var outcome = await fixture.Writer.AmendAsync(new(Tenant, Actor), request);
        if (!allow)
        {
            Assert.Equal(RecordTemporalWriteStatus.Refused, outcome.Status);
            Assert.Equal(("authorize", "TemporalDenied"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
            Assert.Empty(outcome.Pointers);
            Assert.Null(outcome.Receipt);
            Assert.Single(fixture.Access.Requests);
            Assert.Same(Assert.Single(fixture.Access.Evidence), outcome.Decision);
            Assert.False(outcome.Decision!.Allowed);
            Assert.Equal(0, fixture.Storage.TargetLookups);
            Assert.Equal(0, fixture.Storage.ReceiptLookups);
            Assert.Empty(fixture.Storage.Events);
            Assert.False(File.Exists(fixture.PathName));
            return;
        }

        Assert.Equal(RecordTemporalWriteStatus.InvalidTemporalInput, outcome.Status);
        Assert.Equal(new[] { pointer }, outcome.Pointers);
        Assert.Same(Assert.Single(fixture.Access.Evidence), outcome.Decision);
        Assert.Single(fixture.Access.Requests);
        Assert.True(Assert.Single(fixture.Access.Evidence).Allowed);
        Assert.Equal(0, fixture.Storage.ReceiptLookups);
        Assert.Empty(fixture.Storage.Events);
        Assert.False(File.Exists(fixture.PathName));
    }

    [Fact]
    public async Task New_temporal_writes_require_explicit_store_and_clock()
    {
        using var fixture = await Fixture.Create(clock: false);
        var request = new RecordTemporalCreateRequest(Type, "no-clock", Body(), DateTimeOffset.MinValue, null);
        var unavailable = await fixture.Compose(fixture.Storage, temporal: false).CreateEffectiveAsync(new(Tenant, Actor), request);
        Assert.Equal(RecordTemporalWriteStatus.TemporalStoreUnavailable, unavailable.Status);
        Assert.Empty(fixture.Access.Requests);
        var missing = await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor), request);
        Assert.Equal(RecordTemporalWriteStatus.TemporalClockRequired, missing.Status);
        Assert.NotNull(missing.Decision);
        Assert.Equal(0, fixture.Definitions.Heads);
        Assert.Empty(fixture.Storage.Events);
        Assert.False(File.Exists(fixture.PathName));
        Assert.Throws<ArgumentNullException>(() => new FileJournalRecordWriteStore(fixture.PathName, (TimeProvider)null!));
        var other = new Store(new FileJournalRecordWriteStore(fixture.PathName, fixture.AuthorityClock));
        Assert.Throws<ArgumentException>(() => new RecordWriteOperation(new AccessProvider(fixture.Access), fixture.Types,
            fixture.Schemas, fixture.Storage, fixture.Storage, fixture.OperationClock, fixture.Kinds, fixture.Domains,
            other, fixture.Storage, fixture.Storage));
        using var explicitMinimum = await Fixture.Create();
        var committed = Committed(await explicitMinimum.Writer.CreateEffectiveAsync(new(Tenant, Actor), request with { OperationId = "min" }));
        Assert.True(DateTimeOffset.MinValue.EqualsExact(committed.Temporal.EffectiveFrom));
        var before = explicitMinimum.Bytes();
        var heads = explicitMinimum.Definitions.Heads;
        var stamps = explicitMinimum.AuthorityClock.Reads;
        var clockless = new Store(new FileJournalRecordWriteStore(explicitMinimum.PathName));
        var replay = await explicitMinimum.Compose(clockless).CreateEffectiveAsync(new(Tenant, Actor), request with { OperationId = "min" });
        Assert.Equal(RecordTemporalWriteStatus.Replayed, replay.Status);
        Assert.Equal(committed.Receipt, replay.Receipt!.Receipt);
        Assert.Equal(committed.BindingHash, replay.Receipt.BindingHash);
        Assert.True(committed.Temporal.AuthorityRecordedAt.EqualsExact(replay.Receipt.Temporal.AuthorityRecordedAt));
        Assert.Equal(heads, explicitMinimum.Definitions.Heads);
        Assert.Equal(stamps, explicitMinimum.AuthorityClock.Reads);
        var newKey = await explicitMinimum.Compose(clockless).CreateEffectiveAsync(new(Tenant, Actor), request with { OperationId = "new-without-clock" });
        Assert.Equal(RecordTemporalWriteStatus.TemporalClockRequired, newKey.Status);
        Assert.Equal(before, explicitMinimum.Bytes());
    }

    [Fact]
    public async Task Temporal_instance_validation_uses_actual_stored_body_bytes()
    {
        using var fixture = await Fixture.Create(key: "a/b~c");
        var invalid = await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor),
            new(Type, "domain-bad", Body(status: "SECRET-C", key: "a/b~c"), GateAt, null));
        Assert.Equal(RecordTemporalWriteStatus.Refused, invalid.Status);
        Assert.Equal("validate", invalid.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", invalid.Refusal.Code);
        Assert.Equal(new[] { "/a~1b~0c" }, invalid.Pointers);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(invalid.Refusal), StringComparison.Ordinal);
        Assert.Equal(new[] { "begin", "rollback", "dispose" }, fixture.Storage.Events);
        Assert.False(File.Exists(fixture.PathName));
        fixture.Storage.Events.Clear();
        var one = JsonNode.Parse("""{"value":1.0,"a/b~c":"A"}""")!.AsObject();
        var accepted = Committed(await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor),
            new(Type, "number", one, GateAt, null)));
        Assert.Equal("""{"a/b~c":"A","value":1.0}""", Encoding.UTF8.GetString(fixture.Schemas.Validated.Last()));
        var stored = (await fixture.Storage.HistoryAsync(Tenant, accepted.Receipt.RecordId)).Versions[0];
        Assert.Equal("""{"a/b~c":"A","value":1.0}""", Encoding.UTF8.GetString(stored.Row.Body.Span));
        var beforeAmend = fixture.Bytes();
        var stamps = fixture.AuthorityClock.Reads;
        fixture.Storage.Events.Clear();
        var badAmend = await fixture.Writer.AmendAsync(new(Tenant, Actor), new(accepted.Receipt.RecordId, "amend-domain-bad",
            Basis(accepted), Body(status: "SECRET-C", key: "a/b~c"), GateAt, null, "reviewed"));
        Assert.Equal("validate", badAmend.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", badAmend.Refusal.Code);
        Assert.Equal(new[] { "/a~1b~0c" }, badAmend.Pointers);
        Assert.Equal(new[] { "begin", "rollback", "dispose" }, fixture.Storage.Events);
        Assert.Equal(stamps, fixture.AuthorityClock.Reads);
        Assert.Equal(beforeAmend, fixture.Bytes());
        var replay = await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor),
            new(Type, "number", JsonNode.Parse("""{"a/b~c":"A","value":1e0}""")!.AsObject(), GateAt, null));
        Assert.Equal(RecordTemporalWriteStatus.Replayed, replay.Status);
        Assert.Equal(1, fixture.Entries());
        var missing = await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor), new(Type, "missing", new(), GateAt, null));
        Assert.Equal("entity.validation.body_invalid", missing.Refusal!.Code);
        Assert.Equal(new[] { "/value" }, missing.Pointers);
        Assert.Equal(1, fixture.Entries());
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("tenant")]
    [InlineData("type")]
    public async Task Temporal_prepare_publication_fault_rolls_back_without_registry_claim(string fault)
    {
        using var fixture = await Fixture.Create();
        fixture.Definitions.HeadTransform = revision =>
        {
            if (revision is null) return null;
            var document = revision.Document;
            var body = fault == "tenant" ? document.BodyJson.Replace("\"tenant\":\"tenant-a\"", "\"tenant\":\"tenant-b\"", StringComparison.Ordinal)
                : fault == "type" ? document.BodyJson.Replace("\"record_type_id\":\"temporal.entry\"", "\"record_type_id\":\"temporal.other\"", StringComparison.Ordinal)
                    .Replace("\"name\":\"Entry\"", "\"name\":\"Other\"", StringComparison.Ordinal) : document.BodyJson;
            return revision with
            {
                Document = document with { BodyJson = body, Key = fault == "tenant" ? document.Key with { Tenant = "tenant-b" }
                    : fault == "type" ? document.Key with { DefinitionId = "temporal.other" } : document.Key },
                Digest = fault == "digest" ? new string('a', 64) : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))),
            };
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateEvent());
        Assert.Equal(new[] { "begin", "rollback", "dispose" }, fixture.Storage.Events);
        Assert.False(File.Exists(fixture.PathName));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryName, "*.tmp"));
        Assert.NotEmpty(fixture.Schemas.RegisteredText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Denied_temporal_write_exposes_no_operation_receipt(bool mismatch)
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var before = fixture.Bytes();
        var lookups = fixture.Storage.ReceiptLookups;
        fixture.Access.Allow = mismatch;
        fixture.Access.ForeignContext = mismatch;
        var denied = await fixture.CreateEvent();
        Assert.Equal(RecordTemporalWriteStatus.Refused, denied.Status);
        Assert.Null(denied.Receipt);
        Assert.Equal("authorize", denied.Refusal!.Stage);
        Assert.Equal(lookups, fixture.Storage.ReceiptLookups);
        Assert.Equal(before, fixture.Bytes());
        Assert.Equal(1, fixture.Entries());
        Assert.NotNull(first);
    }

    [Fact]
    public async Task Exact_replay_after_newer_head_is_local_after_current_allow()
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        var second = Committed(await fixture.AmendEvent(first));
        _ = Committed(await fixture.AmendEvent(second, "r3", "2031-05-01T00:00:00-04:00", "2031-06-01T00:00:00-04:00", 15));
        await fixture.PublishNext();
        var before = fixture.Bytes();
        var heads = fixture.Definitions.Heads;
        var registers = fixture.Schemas.RegisteredText.Count;
        var stamps = fixture.AuthorityClock.Reads;
        fixture.Schemas.RegisterFault = new IOException("must-not-rebind");
        var replay = await fixture.AmendEvent(first);
        Assert.Equal(RecordTemporalWriteStatus.Replayed, replay.Status);
        Assert.Equal(second.Receipt, replay.Receipt!.Receipt);
        Assert.Equal(second.BindingHash, replay.Receipt.BindingHash);
        Assert.True(second.Temporal.AuthorityRecordedAt.EqualsExact(replay.Receipt.Temporal.AuthorityRecordedAt));
        Assert.NotNull(replay.Decision);
        Assert.Equal(heads, fixture.Definitions.Heads);
        Assert.Equal(registers, fixture.Schemas.RegisteredText.Count);
        Assert.Equal(stamps, fixture.AuthorityClock.Reads);
        Assert.Equal(before, fixture.Bytes());
        Assert.Equal(3, fixture.Entries());
    }

    [Fact]
    public async Task Current_denial_still_blocks_exact_replay()
    {
        using var fixture = await Fixture.Create();
        _ = Committed(await fixture.CreateEvent());
        var before = fixture.Bytes();
        var lookups = fixture.Storage.ReceiptLookups;
        fixture.Access.Allow = false;
        var denied = await fixture.CreateEvent();
        Assert.Equal("TemporalDenied", denied.Refusal!.Code);
        Assert.Null(denied.Receipt);
        Assert.NotNull(denied.Decision);
        Assert.Equal(lookups, fixture.Storage.ReceiptLookups);
        Assert.Equal(before, fixture.Bytes());
    }

    [Theory]
    [InlineData("body")]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("offset")]
    [InlineData("reason")]
    [InlineData("pin")]
    [InlineData("basis-revision")]
    [InlineData("basis-hash")]
    [InlineData("target")]
    [InlineData("kind")]
    public async Task Every_temporal_intent_member_changes_same_key_settlement(string member)
    {
        using var fixture = await Fixture.Create();
        var first = Committed(await fixture.CreateEvent());
        _ = Committed(await fixture.AmendEvent(first));
        var request = new RecordTemporalAmendRequest(first.Receipt.RecordId, "r2", Basis(first), Body(20),
            Date("2031-06-01T00:00:00-04:00"), null, "reviewed");
        if (member == "target") request = request with { RecordId = Committed(await fixture.CreateEvent("another")).Receipt.RecordId };
        request = member switch
        {
            "body" => request with { Body = Body(21) },
            "from" => request with { EffectiveFrom = request.EffectiveFrom.AddTicks(1) },
            "to" => request with { EffectiveTo = request.EffectiveFrom.AddDays(1) },
            "offset" => request with { EffectiveFrom = request.EffectiveFrom.ToOffset(TimeSpan.Zero) },
            "reason" => request with { Reason = " reviewed" },
            "pin" => request with { Version = "1.0.0" },
            "basis-revision" => request with { ExpectedPredecessor = new(2, first.BindingHash) },
            "basis-hash" => request with { ExpectedPredecessor = new(1, new string('a', 64)) },
            _ => request,
        };
        var before = fixture.Bytes();
        var changed = member == "kind"
            ? await fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor), new(Type, "r2", Body(20), request.EffectiveFrom, null))
            : await fixture.Writer.AmendAsync(new(Tenant, Actor), request);
        Assert.Equal(RecordTemporalWriteStatus.Refused, changed.Status);
        Assert.Equal("commit", changed.Refusal!.Stage);
        Assert.Equal("records.write.replay_conflict", changed.Refusal.Code);
        Assert.Equal(new[] { "/operation_id" }, changed.Pointers);
        Assert.Null(changed.Receipt);
        Assert.Equal(before, fixture.Bytes());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task Lost_temporal_created_receipt_keeps_commit_and_settles_later(string fault)
    {
        using var fixture = await Fixture.Create();
        fixture.Storage.ChangeResult = result => result with
        {
            Receipt = fault == "missing" ? null : result.Receipt! with { Receipt = result.Receipt.Receipt with { OperationId = "foreign" } },
        };
        var lost = await fixture.CreateEvent();
        Assert.Equal(RecordTemporalWriteStatus.Refused, lost.Status);
        Assert.Equal("records.write.receipt_unsupported", lost.Refusal!.Code);
        Assert.Null(lost.Decision);
        Assert.False(lost.Committed);
        Assert.Equal(1, fixture.Entries());
        var original = (await fixture.Storage.Inner.FindTemporalOperationAsync(new(Tenant, Actor, "r1")))!.Receipt!;
        var before = fixture.Bytes();
        fixture.Storage.ChangeResult = null;
        fixture.Schemas.RegisterFault = new IOException("no-rebind-on-retry");
        var retry = await fixture.CreateEvent();
        Assert.Equal(RecordTemporalWriteStatus.Replayed, retry.Status);
        Assert.Equal(original.Receipt, retry.Receipt!.Receipt);
        Assert.Equal(original.BindingHash, retry.Receipt.BindingHash);
        Assert.True(AuthorityAt.EqualsExact(retry.Receipt.Temporal.AuthorityRecordedAt));
        Assert.Equal(before, fixture.Bytes());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("created-stored")]
    [InlineData("replay-new-receipt")]
    [InlineData("typed-stored")]
    [InlineData("missing-stored")]
    public async Task Unknown_port_state_or_inconsistent_settlement_is_internal_fault(string fault)
    {
        using var fixture = await Fixture.Create();
        var receipt = new RecordTemporalReceipt(new("record", "r1", Type, "1.0.0", new string('a', 64), new("opaque"), GateAt),
            new(1, null, [], GateAt, null, null, AuthorityAt), new string('b', 64));
        var prior = new RecordTemporalCommittedOperation("unknown-encoding", "unknown-digest", null);
        fixture.Storage.InsteadOfCommit = fault switch
        {
            "unknown" => new((RecordTemporalCommitDisposition)999, null),
            "created-stored" => new(RecordTemporalCommitDisposition.Created, null) { Stored = prior },
            "replay-new-receipt" => new(RecordTemporalCommitDisposition.Replayed, receipt) { Stored = prior },
            "typed-stored" => new(RecordTemporalCommitDisposition.BasisMismatch, null) { Stored = prior },
            _ => new(RecordTemporalCommitDisposition.Replayed, null),
        };
        if (fault == "missing-stored")
        {
            var outcome = await fixture.CreateEvent();
            Assert.Equal("records.write.receipt_unsupported", outcome.Refusal!.Code);
            Assert.False(outcome.Committed);
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateEvent());
        Assert.False(File.Exists(fixture.PathName));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryName, "*.tmp"));
    }

    [Theory]
    [InlineData("empty", false, false)]
    [InlineData("empty", false, true)]
    [InlineData("ascii-boundary", true, false)]
    [InlineData("ascii-boundary", true, true)]
    [InlineData("unicode-boundary", true, false)]
    [InlineData("unicode-boundary", true, true)]
    [InlineData("ascii-over", false, false)]
    [InlineData("ascii-over", false, true)]
    [InlineData("unicode-over", false, false)]
    [InlineData("unicode-over", false, true)]
    [InlineData("invalid-scalar", false, false)]
    [InlineData("invalid-scalar", false, true)]
    [Trait("Holds", "records-eng-2")]
    public async Task Temporal_operation_id_utf8_boundaries_are_enforced_after_gate_allows(string kind, bool lawful, bool allow)
    {
        using var fixture = await Fixture.Create();
        fixture.Access.Allow = allow;
        var operation = kind switch
        {
            "empty" => "",
            "ascii-boundary" => new string('x', 255),
            "unicode-boundary" => new string('\u00e9', 127) + "a",
            "ascii-over" => new string('x', 256),
            "unicode-over" => new string('\u00e9', 128),
            _ => "bad\ud800",
        };
        var outcome = await fixture.CreateEvent(operation);
        if (!allow)
        {
            Assert.Equal(RecordTemporalWriteStatus.Refused, outcome.Status);
            Assert.Equal(("authorize", "TemporalDenied"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
            Assert.Empty(outcome.Pointers);
            Assert.Null(outcome.Receipt);
            Assert.Single(fixture.Access.Requests);
            Assert.Same(Assert.Single(fixture.Access.Evidence), outcome.Decision);
            Assert.False(outcome.Decision!.Allowed);
            Assert.Equal(0, fixture.Storage.TargetLookups);
            Assert.Equal(0, fixture.Storage.ReceiptLookups);
            Assert.Empty(fixture.Storage.Events);
            Assert.False(File.Exists(fixture.PathName));
            return;
        }

        if (lawful)
        {
            _ = Committed(outcome);
            Assert.Single(fixture.Access.Requests);
            Assert.Equal(1, fixture.Entries());
        }
        else
        {
            Assert.Equal(RecordTemporalWriteStatus.Refused, outcome.Status);
            Assert.Equal("records.write.operation_id_invalid", outcome.Refusal!.Code);
            Assert.Equal(new[] { "/operation_id" }, outcome.Pointers);
            Assert.Single(fixture.Access.Requests);
            Assert.True(Assert.Single(fixture.Access.Evidence).Allowed);
            Assert.Same(Assert.Single(fixture.Access.Evidence), outcome.Decision);
            Assert.Equal(0, fixture.Storage.ReceiptLookups);
            Assert.Empty(fixture.Storage.Events);
            Assert.False(File.Exists(fixture.PathName));
        }
    }

    [Fact]
    public async Task Temporal_pre_cancel_and_programming_arguments_start_no_work()
    {
        using var fixture = await Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new RecordTemporalCreateRequest(Type, "cancel", Body(), GateAt, null);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor), request, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            fixture.Writer.CreateEffectiveAsync(new(Tenant, Actor), request with { OperationId = null! }).AsTask());
        Assert.Empty(fixture.Access.Requests);
        Assert.Equal(0, fixture.Storage.ReceiptLookups);
        Assert.Equal(0, fixture.OperationClock.Reads);
        Assert.False(File.Exists(fixture.PathName));
    }

    [Theory]
    [InlineData("uppercase-hash")]
    [InlineData("short-hash")]
    [InlineData("zero-revision")]
    [InlineData("create-predecessor")]
    [InlineData("create-segment")]
    public async Task Malformed_temporal_receipts_never_override_durable_accepted_state(string defect)
    {
        using var fixture = await Fixture.Create();
        var accepted = Committed(await fixture.CreateEvent());
        var before = fixture.Bytes();
        fixture.Schemas.RegisterFault = new IOException("no-bind-for-accepted-key");
        fixture.Storage.ChangeLookup = value =>
        {
            var original = value!.Receipt!;
            var changed = defect switch
            {
                "uppercase-hash" => original with { BindingHash = new string('A', 64) },
                "short-hash" => original with { BindingHash = "abc" },
                "zero-revision" => original with { Temporal = original.Temporal with { Revision = 0 } },
                "create-predecessor" => original with { Temporal = original.Temporal with { Predecessor = new(1, new string('a', 64)) } },
                _ => original with { Temporal = original.Temporal with { Supersedes =
                    [new(new(1, new string('a', 64)), GateAt, null)] } },
            };
            return value with { Receipt = changed };
        };
        var retry = await fixture.CreateEvent();
        Assert.Equal(RecordTemporalWriteStatus.Refused, retry.Status);
        Assert.Equal("commit", retry.Refusal!.Stage);
        Assert.Equal("records.write.receipt_unsupported", retry.Refusal.Code);
        Assert.Null(retry.Receipt);
        Assert.NotNull(retry.Decision);
        Assert.Equal(before, fixture.Bytes());
        var durable = (await fixture.Storage.Inner.FindTemporalOperationAsync(new(Tenant, Actor, "r1")))!.Receipt!;
        Assert.Equal(accepted.Receipt, durable.Receipt);
        Assert.Equal(accepted.BindingHash, durable.BindingHash);
        Assert.Equal(1, durable.Temporal.Revision);
        Assert.Null(durable.Temporal.Predecessor);
        Assert.Empty(durable.Temporal.Supersedes);
    }

    internal const string Tenant = "tenant-a";
    internal const string Actor = "operator-7";
    internal const string Type = "temporal.entry";
    internal static readonly DateTimeOffset GateAt = new(2031, 6, 3, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset AuthorityAt = new(2031, 6, 3, 9, 0, 2, TimeSpan.Zero);
    internal static DateTimeOffset Date(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    internal static JsonObject Body(int value = 10, string status = "A", string key = "status")
        => new() { ["value"] = value, [key] = status };
    internal static RecordTemporalBasis Basis(RecordTemporalReceipt receipt)
        => new(receipt.Temporal.Revision, receipt.BindingHash);
    // T-616/prevalidation-mutation (records-eng-5, ADR 0124 decision 1): an effective-dated create and its amendment each
    // fill the derived field from their own body before validation. Oracles: the literal calculation's arithmetic and the
    // canonical stored bodies.
    [Fact]
    [Trait("Holds", "records-eng-5")]
    public async Task records_eng_5_a_temporal_create_and_its_amendment_each_fill_the_derived_field_before_validation()
    {
        var doubled = new NamedCalculation("doubled", "1.0.0", """{"*":[{"var":"value"},2]}""");
        using var fixture = await Fixture.Create(derived: doubled);

        var created = Committed(await fixture.CreateEvent(value: 10));
        var amended = Committed(await fixture.AmendEvent(created, value: 21));

        var history = await fixture.Storage.HistoryAsync(Tenant, created.Receipt.RecordId);
        Assert.Equal(["""{"doubled":20,"status":"A","value":10}""", """{"doubled":42,"status":"A","value":21}"""],
            history.Versions.Select(version => Encoding.UTF8.GetString(version.Row.Body.Span)).Order(StringComparer.Ordinal).ToArray());
        Assert.NotEqual(created.Receipt.RecordId, string.Empty);
        Assert.Equal(created.Receipt.RecordId, amended.Receipt.RecordId);
    }

    [Fact]
    [Trait("Holds", "records-eng-5")]
    public async Task an_amend_gate_never_sees_caller_values_so_a_derived_value_cannot_slip_past_an_earlier_allow()
    {
        // Codex 4521203376 (CWE-863, CWE-807) and the M11 ruling: an amend is decided on an untyped request naming only the
        // record id (T-616 Option A), and AccessRecord facts come from the tenant-bound record reader, never the caller
        // (AccessScope.cs). A policy that would deny doubled > 50 is shown no field at all, while the pinned calculation
        // over value 30 still commits doubled 60. Oracles: the literal policy and the literal calculation's arithmetic.
        var doubled = new NamedCalculation("doubled", "1.0.0", """{"*":[{"var":"value"},2]}""");
        using var fixture = await Fixture.Create(derived: doubled);
        var created = Committed(await fixture.CreateEvent(value: 10));
        var seen = new List<int>();
        fixture.Access.Predicate = request =>
        {
            seen.Add(request.Record.Fields.Count);
            return !(request.Record.Fields.TryGetValue("doubled", out var value) && value!.GetValue<int>() > 50);
        };
        var body = Body(30);
        body["doubled"] = 0;

        var amended = Committed(await fixture.Writer.AmendAsync(new(Tenant, Actor), new(created.Receipt.RecordId, "amend-derived",
            Basis(created), body, Date("2031-06-01T00:00:00-04:00"), null, "reviewed", null)));

        Assert.Equal([0], seen);
        var history = await fixture.Storage.HistoryAsync(Tenant, amended.Receipt.RecordId);
        Assert.Contains("""{"doubled":60,"status":"A","value":30}""",
            history.Versions.Select(version => Encoding.UTF8.GetString(version.Row.Body.Span)));
    }

    [Fact]
    [Trait("Holds", "records-eng-5")]
    public async Task records_eng_5_a_temporal_write_whose_calculation_does_not_resolve_refuses_at_mutate()
    {
        var doubled = new NamedCalculation("doubled", "1.0.0", """{"*":[{"var":"value"},2]}""");
        using var fixture = await Fixture.Create(derived: doubled, composeCalculations: false);

        var refused = await fixture.CreateEvent(value: 10);

        Assert.False(refused.Committed);
        Assert.Equal(("mutate", "records.mutate.calculation_unresolved"), (refused.Refusal!.Stage, refused.Refusal.Code));
        Assert.Equal(["/doubled"], refused.Refusal.Pointers);
        Assert.Equal(0, fixture.Entries());
    }

    internal static RecordTemporalReceipt Committed(RecordTemporalWriteOutcome outcome)
    {
        Assert.True(outcome.Committed);
        Assert.Null(outcome.Refusal);
        Assert.Empty(outcome.Pointers);
        return Assert.IsType<RecordTemporalReceipt>(outcome.Receipt);
    }

    internal sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        internal int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return Now; }
    }

    internal sealed class Gate : IAuthorizationDecider
    {
        internal bool Allow { get; set; } = true;
        internal Func<AccessRequest, bool>? Predicate { get; set; }
        internal bool ForeignContext { get; set; }
        internal Func<CancellationToken, ValueTask>? BeforeDecision { get; set; }
        internal ConcurrentQueue<AccessRequest> Requests { get; } = new();
        internal ConcurrentQueue<AuthorizationDecisionEvidence> Evidence { get; } = new();
        public async ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request);
            if (BeforeDecision is { } before) await before(cancellationToken);
            var allowed = Allow && (Predicate?.Invoke(request) ?? true);
            var context = ForeignContext ? new AccessRequest(request.Operation, request.Principal, "another-tenant",
                request.Record, request.At) : request;
            var evidence = new AuthorizationDecisionEvidence(context, allowed, allowed ? "None" : "TemporalDenied",
                "temporal-fixture-binding", [], []);
            Evidence.Enqueue(evidence);
            return evidence;
        }
    }

    internal sealed class Registry(ISchemaRegistry inner, bool opaque) : ISchemaRegistry
    {
        private readonly Dictionary<SchemaId, SchemaId> _canonical = [];
        private readonly Dictionary<SchemaId, Schema> _schemas = [];
        internal List<string> RegisteredText { get; } = [];
        internal List<byte[]> Validated { get; } = [];
        internal Exception? RegisterFault { get; set; }
        internal Exception? ValidateFault { get; set; }
        internal Func<CancellationToken, ValueTask>? DuringValidate { get; set; }
        public ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default)
            => opaque ? ValueTask.FromResult(_schemas.GetValueOrDefault(id)) : inner.GetAsync(id, cancellationToken);
        public async ValueTask<Schema> RegisterAsync(string jsonSchemaText, IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegisteredText.Add(jsonSchemaText);
            if (RegisterFault is not null) throw RegisterFault;
            var schema = await inner.RegisterAsync(jsonSchemaText, parents, tags, cancellationToken);
            if (!opaque) return schema;
            var id = new SchemaId("opaque-temporal:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jsonSchemaText))));
            _canonical[id] = schema.Id;
            var returned = schema with { Id = id };
            _schemas[id] = returned;
            return returned;
        }
        public async ValueTask<SchemaValidationResult> ValidateAsync(SchemaId id, ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validated.Add(bytes.ToArray());
            if (ValidateFault is not null) throw ValidateFault;
            if (DuringValidate is not null) await DuringValidate(cancellationToken);
            return await inner.ValidateAsync(opaque ? _canonical[id] : id, bytes, cancellationToken);
        }
        public IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null, CancellationToken cancellationToken = default)
            => inner.ListAsync(tagFilter, cancellationToken);
    }

    internal sealed class Catalogue(IVersionedDefinitionStore inner) : IVersionedDefinitionStore
    {
        internal int Heads { get; private set; }
        internal List<DefinitionBinding> Pins { get; } = [];
        internal Func<DefinitionRevision?, DefinitionRevision?>? HeadTransform { get; set; }
        internal Func<DefinitionBinding, DefinitionRevision?, DefinitionRevision?>? PinTransform { get; set; }
        internal Exception? PinFault { get; set; }
        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default)
            => inner.ListKeysAsync(tenant, kind, cancellationToken);
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId,
            string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision, requestId, cancellationToken);
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default)
            => inner.ListHistoryAsync(key, cancellationToken);
        public async ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default)
        {
            Heads++;
            var revision = await inner.GetPublishedHeadAsync(key, cancellationToken);
            return HeadTransform is null ? revision : HeadTransform(revision);
        }
        public async ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default)
        {
            Pins.Add(binding);
            if (PinFault is not null) throw PinFault;
            var revision = await inner.ResolvePublishedAsync(binding, cancellationToken);
            return PinTransform is null ? revision : PinTransform(binding, revision);
        }
    }

    // Every exposed storage interface is the same object. Its accepted state remains the real journal.
    internal sealed class Store(FileJournalRecordWriteStore inner) : IRecordTemporalWritePort, IRecordTemporalReceiptReader,
        IRecordTemporalReadStore, IRecordWriteReceiptReader, IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>
    {
        internal FileJournalRecordWriteStore Inner { get; } = inner;
        internal ConcurrentQueue<string> Events { get; } = new();
        internal int ReceiptLookups { get; private set; }
        internal int TargetLookups { get; private set; }
        internal int HiddenLookups { get; set; }
        internal string? FaultAt { get; set; }
        internal Exception Fault { get; set; } = new IOException("fixture-stage-fault");
        internal Func<RecordTemporalCommit, RecordTemporalCommit>? ChangeResult { get; set; }
        internal RecordTemporalCommit? InsteadOfCommit { get; set; }
        internal Action<string>? OnStep { get; set; }
        internal Func<ValueTask>? BeforeCommit { get; set; }
        internal Func<RecordTemporalCommittedOperation?, RecordTemporalCommittedOperation?>? ChangeLookup { get; set; }
        internal RecordTemporalWrite? Staged { get; private set; }
        public bool HasAuthorityClock => Inner.HasAuthorityClock;
        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
            => Inner.FindAsync(key, cancellationToken);
        public ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
            => Inner.FindAsync(tenant, recordId, cancellationToken);
        public ValueTask<RecordTemporalLookup> FindAtAsync(string tenant, string recordId, DateTimeOffset at, CancellationToken cancellationToken = default)
            => Inner.FindAtAsync(tenant, recordId, at, cancellationToken);
        public ValueTask<RecordTemporalHistory> HistoryAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
            => Inner.HistoryAsync(tenant, recordId, cancellationToken);
        public ValueTask<RecordTemporalTarget?> FindTemporalTargetAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
        {
            TargetLookups++;
            return Inner.FindTemporalTargetAsync(tenant, recordId, cancellationToken);
        }
        public async ValueTask<RecordTemporalCommittedOperation?> FindTemporalOperationAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        {
            ReceiptLookups++;
            if (HiddenLookups > 0) { HiddenLookups--; return null; }
            var result = await Inner.FindTemporalOperationAsync(key, cancellationToken);
            return ChangeLookup is null ? result : ChangeLookup(result);
        }
        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
            => Inner.BeginAsync(cancellationToken);
        async ValueTask<IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>>
            IKernelPreparedTransactionPort<RecordTemporalWrite, RecordTemporalCommit>.BeginAsync(CancellationToken cancellationToken)
        {
            Step("begin");
            return new Transaction(this, await ((IRecordTemporalWritePort)Inner).BeginAsync(cancellationToken));
        }
        private void Step(string step)
        {
            Events.Enqueue(step);
            OnStep?.Invoke(step);
            if (FaultAt == step) throw Fault;
        }
        private sealed class Transaction(Store owner, IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit> inner)
            : IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
            { owner.Step("operation"); return inner.StageOperationAsync(operation, cancellationToken); }
            public ValueTask StageRecordAsync(RecordTemporalWrite write, CancellationToken cancellationToken = default)
            { owner.Step("record"); owner.Staged = write; return inner.StageRecordAsync(write, cancellationToken); }
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            { owner.Step("audit"); return inner.StageAuditAsync(audit, cancellationToken); }
            public async ValueTask<RecordTemporalCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Step("commit");
                if (owner.InsteadOfCommit is { } substitute) return substitute;
                if (owner.BeforeCommit is not null) await owner.BeforeCommit();
                var result = await inner.CommitAsync(cancellationToken);
                return owner.ChangeResult?.Invoke(result) ?? result;
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            { owner.Events.Enqueue("rollback"); return inner.RollbackAsync(cancellationToken); }
            public ValueTask DisposeAsync()
            { owner.Events.Enqueue("dispose"); return inner.DisposeAsync(); }
        }
    }

    internal sealed class Fixture : IDisposable
    {
        internal string DirectoryName { get; } = Path.Combine(Path.GetTempPath(), "t617-temporal-" + Guid.NewGuid().ToString("N"));
        internal string PathName => Path.Combine(DirectoryName, "records.json");
        internal Clock OperationClock { get; } = new(GateAt);
        internal Clock AuthorityClock { get; } = new(AuthorityAt);
        internal Gate Access { get; } = new();
        internal required Registry Schemas { get; init; }
        internal required Catalogue Definitions { get; init; }
        internal required InMemoryVersionedDefinitionStore RawDefinitions { get; init; }
        internal required RecordTypeDefinitionStore Publisher { get; init; }
        internal required RecordTypeDefinitionStore Types { get; init; }
        internal required IFieldKindRuntime Kinds { get; init; }
        internal required IFieldDomainRuntime Domains { get; init; }
        internal Store Storage { get; private set; } = null!;
        internal RecordWriteOperation Writer => Compose(Storage);
        internal RecordReadOperation Reader => new(Storage, new AccessProvider(Access), Types, OperationClock);
        internal PinnedClosure? Calculations { get; init; }
        internal RecordWriteOperation Compose(Store store, bool temporal = true) => temporal
            ? new(new AccessProvider(Access), Types, Schemas, store, store, OperationClock, Kinds, Domains, store, store, store) { Calculations = Calculations }
            : new(new AccessProvider(Access), Types, Schemas, store, store, OperationClock, Kinds, Domains) { Calculations = Calculations };
        internal static async Task<Fixture> Create(bool mode = true, bool clock = true, bool opaque = false, string key = "status",
            NamedCalculation? derived = null, bool composeCalculations = true)
        {
            var kinds = new FieldKindRuntime(new FieldKindRegistry([
                new("text", "1.0.0", null, FieldScalarValueShape.Text),
                new("amount", "1.0.0", null, FieldScalarValueShape.Number),
            ]));
            var domains = RecordWriteFieldRuntimeTests.FixtureDomains(new Clock(GateAt));
            var validator = new RecordsIntentValidator();
            var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission(), domains);
            var registry = new Registry(new InMemorySchemaRegistry(fieldKindRuntime: kinds), opaque);
            var defaults = new RecordFieldDefaults(kinds);
            var window = new DefinitionContractWindow(1, 0, 1);
            var definitions = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
            {
                [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(window, validator),
                [DefinitionKind.Classes] = ClassDefinitionStore.Admission(window),
            });
            var classes = new ClassDefinitionStore(definitions);
            await classes.CreateDraftAsync(new(Tenant, "temporal", "Items", "1.0.0", new(1, 0), "temporal-tests"), "class-create");
            await classes.PublishAsync(Tenant, "temporal.items", "1.0.0", 1, "class-publish");
            var publisher = new RecordTypeDefinitionStore(definitions, compiler, defaults, registry, window,
                new(TenantId.FromString(Tenant), "type-author"), CreationRoles);
            List<FieldDefinition> fields =
                [new("value", "Value", new(new("amount", "1.0.0", new Dictionary<string, string>()), new(true, 1, 1, [], null))),
                 new(key, "Status", new(new("text", "1.0.0", new Dictionary<string, string>()), new(false, 0, 1, [], new(LiteralValues: ["A", "B"]))))];
            if (derived is not null)
                fields.Add(new FieldDefinition("doubled", "Doubled", new(new("amount", "1.0.0", new Dictionary<string, string>()), new(true, 1, 1, [], null)))
                    { Calculation = new(derived.Pin.Name, derived.Pin.Version, derived.Pin.Digest) });
            await publisher.CreateDraftAsync(new(Tenant, "temporal", "Entry", "1.0.0", fields,
                new(1, 0), ClassId: "temporal.items", RecordClass: RecordClass.Master, PackageId: "temporal-tests",
                CreationGate: RecordCreationGate.Role, CreationRoleId: "asset-author")
                { AmendmentPolicy = new(true) { Mode = mode ? RecordAmendmentMode.Supersession : null }, HistoryPolicy = RecordHistoryPolicy.Enabled }, "type-create");
            await publisher.PublishAsync(Tenant, Type, "1.0.0", 1, "type-publish");
            var observed = new Catalogue(definitions);
            var fixture = new Fixture
            {
                Schemas = registry, RawDefinitions = definitions, Definitions = observed, Publisher = publisher,
                Types = new(observed, compiler, defaults, registry, window,
                    new(TenantId.FromString(Tenant), "type-author"), CreationRoles), Kinds = kinds, Domains = domains,
                Calculations = derived is not null && composeCalculations ? new([], [derived]) : null,
            };
            Directory.CreateDirectory(fixture.DirectoryName);
            fixture.Storage = new(clock ? new(fixture.PathName, fixture.AuthorityClock) : new(fixture.PathName));
            registry.RegisteredText.Clear();
            return fixture;
        }
        internal Task<RecordTemporalWriteOutcome> CreateEvent(string operation = "r1", string from = "2031-04-01T00:00:00-04:00",
            string? to = null, int value = 10, string? version = null)
            => Writer.CreateEffectiveAsync(new(Tenant, Actor), new(Type, operation, Body(value), Date(from),
                to is null ? null : Date(to), version)).AsTask();
        internal Task<RecordTemporalWriteOutcome> AmendEvent(RecordTemporalReceipt basis, string operation = "r2",
            string from = "2031-06-01T00:00:00-04:00", string? to = null, int value = 20, string reason = "reviewed", string? version = null)
            => Writer.AmendAsync(new(Tenant, Actor), new(basis.Receipt.RecordId, operation, Basis(basis), Body(value),
                Date(from), to is null ? null : Date(to), reason, version)).AsTask();
        internal byte[] Bytes() => File.Exists(PathName) ? File.ReadAllBytes(PathName) : [];
        internal int Entries()
        {
            if (!File.Exists(PathName)) return 0;
            using var document = JsonDocument.Parse(File.ReadAllBytes(PathName), new JsonDocumentOptions { MaxDepth = 65 });
            return document.RootElement.GetProperty("Entries").GetArrayLength();
        }
        internal async Task PublishNext(string version = "1.1.0", RecordCreationGate? gate = null)
        {
            var head = (await Publisher.GetPublishedHeadAsync(Tenant, Type))!;
            if (gate is not null) head = head with { CreationGate = gate, CreationRoleId = null };
            await Publisher.SaveDraftAsync(Type, head, version, 2, "next-draft");
            await Publisher.PublishAsync(Tenant, Type, version, 3, "next-publish");
        }
        public void Dispose() { if (Directory.Exists(DirectoryName)) Directory.Delete(DirectoryName, true); }
    }
}
