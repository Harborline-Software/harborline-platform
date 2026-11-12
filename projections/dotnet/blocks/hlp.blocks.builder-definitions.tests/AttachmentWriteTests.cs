using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.ContentStore;
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

public sealed class AttachmentWriteTests
{
    [Fact]
    public async Task Pack_limit_above_store_installs_with_named_warning_and_unchanged_limit_and_put_uses_store_limit()
    {
        var fixture = await Fixture.Create();
        var head = (await fixture.Records.GetPublishedHeadAsync("tenant-a", "eam.evidence"))!;
        var entry = new RecordTypeDefinitionPackageEntry("eam.evidence", "1.0.0", PlatformPackageContent.PresentJson(RecordTypeDefinitionJson.SerializeCanonical(head)));
        var installed = await fixture.Records.AdmitInstallAsync("tenant-a", entry, new([], []));
        Assert.Empty(installed.Refusals);
        var warning = Assert.Single(installed.Warnings);
        Assert.Equal("content.field-limit-above-store", warning.Code);
        Assert.Equal("evidence", warning.FieldKey);
        Assert.Equal("/fields/0/binding/kind/parameters/item_limit_bytes", warning.Pointer);
        var publishedWarning = Assert.Single(fixture.Publication.PolicyWarnings, item => item.Code == "content.field-limit-above-store");
        Assert.Equal("/fields/0/binding/kind/parameters/item_limit_bytes", publishedWarning.Pointer);
        Assert.Equal(200, warning.DeclaredLimitBytes);
        Assert.Equal(100, warning.StoreLimitBytes);
        Assert.Equal("200", head.Fields[0].Binding!.Kind.Parameters["item_limit_bytes"]);
        var error = await Assert.ThrowsAsync<ContentRefusedException>(async () => await fixture.Upload(new string('a', 101)));
        Assert.Equal("content.too-large", error.Code);
        Assert.Equal(100, error.Detail["limit"]);
        Assert.Equal(0, fixture.Store.StagedCount);
    }

    [Fact]
    public async Task Records_promotes_stages_in_order_and_read_schema_refuses_staged_entries()
    {
        var fixture = await Fixture.Create();
        var first = await fixture.Upload("first");
        var second = await fixture.Upload("second");
        var write = await fixture.Write("create", List(first, second));
        Assert.True(write.Committed, write.Refusal?.Code);
        using var row = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        var entries = row.RootElement.GetProperty("evidence");
        Assert.Equal(["first.txt", "second.txt"], entries.EnumerateArray().Select(item => item.GetProperty("display_name").GetString()));
        Assert.All(entries.EnumerateArray(), item => { Assert.False(item.TryGetProperty("stage_id", out _)); Assert.Equal(write.Receipt!.RecordId, item.GetProperty("owner").GetProperty("record").GetString()); });
        Assert.Equal(2, fixture.Store.CommittedCount);
        var publication = (await fixture.Records.ResolvePublishedSchemaAsync("tenant-a", "eam.evidence"))!;
        Assert.True((await fixture.Registry.ValidateAsync(publication.Schema.Id, Assert.Single(fixture.Port.Rows).Body)).IsValid);
        Assert.False((await fixture.Registry.ValidateAsync(publication.Schema.Id, Encoding.UTF8.GetBytes(List(first).ToJsonString()))).IsValid);
        var refused = await fixture.Write("second-write", List(first));
        Assert.False(refused.Committed);
        Assert.False(refused.Replayed);
        Assert.Equal("content.staging-expired", refused.Refusal!.Code);
        Assert.Single(fixture.Port.Rows);
    }

    [Fact]
    public async Task A_refused_later_commit_rolls_back_earlier_reference_and_the_record()
    {
        var fixture = await Fixture.Create();
        var good = await fixture.Upload("first");
        var refused = await fixture.Write("refused", List(good, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        Assert.Equal("content.staging-expired", refused.Refusal!.Code);
        Assert.False(refused.Replayed);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
        Assert.True((await fixture.Write("retry", List(good))).Committed);
    }

    [Theory]
    [InlineData("open", false, true)]
    [InlineData("closed", false, false)]
    [InlineData("closed", true, true)]
    public async Task Evidence_condition_requires_an_attachment_exactly_when_true(string status, bool attach, bool expected)
    {
        var fixture = await Fixture.Create(condition: true);
        var body = attach ? List(await fixture.Upload("evidence")) : new JsonObject { ["evidence"] = new JsonArray() };
        body["status"] = status;
        var outcome = await fixture.Write("condition", body);
        Assert.Equal(expected, outcome.Committed);
        if (!expected) { Assert.Equal("field.evidence_required", outcome.Refusal!.Code); Assert.Equal("rules", outcome.Refusal.Stage); Assert.Equal(["/evidence"], outcome.Refusal.Pointers); Assert.Empty(fixture.Port.Rows); Assert.Equal(0, fixture.Store.CommittedCount); }
    }

    // content-store-cc-2: Records evaluates an evidence condition through the shared Rules compiler on every write, so
    // admission compiles it the same way. A condition the compiler refuses cannot reach a published generation.
    [Theory]
    [InlineData("""{"unsupported_operator":[]}""")]
    [InlineData("""{">":[{"agg":["sum","rows","amount"]},0]}""")]
    [InlineData("""{"==":[{"var":"caller.id"},"x"]}""")]
    public async Task Content_store_cc_2_an_evidence_condition_the_rules_compiler_refuses_is_refused_at_admission(string expression)
    {
        var fixture = await Fixture.Create(condition: true);
        var head = (await fixture.Records.GetPublishedHeadAsync("tenant-a", "eam.evidence"))!;
        var bad = head with { Fields = [head.Fields[0] with { RequiredCondition = JsonSerializer.Deserialize<JsonElement>(expression) }, .. head.Fields.Skip(1)] };

        var preflight = await fixture.Records.PreflightAsync("tenant-a", bad);
        var previewed = Assert.Single(preflight.Refusals);
        Assert.Equal("field.required_condition_invalid", previewed.Code);
        Assert.Equal("/fields/0/required_condition", previewed.Pointer);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await fixture.Records.SaveDraftAsync("eam.evidence", bad, "1.0.1", 2, "bad-condition"));
        var saved = Assert.Single(refused.Refusals);
        Assert.Equal("field.required_condition_invalid", saved.Code);
        Assert.Equal("/fields/0/required_condition", saved.Pointer);

        // The working generation is untouched and still writable.
        var body = List(await fixture.Upload("evidence"));
        body["status"] = "closed";
        Assert.True((await fixture.Write("still-writable", body)).Committed);
    }

    // content-store-cc-2: an evidence condition reads the final candidate the write commits, so it sees a later
    // attachment field's promoted references, never its staged {stage_id, display_name} entries.
    [Fact]
    public async Task Content_store_cc_2_an_evidence_condition_reads_later_attachment_fields_after_promotion()
    {
        // True while documents still holds a staged entry; false once every entry is a committed reference.
        var fixture = await Fixture.CreateWithLaterAttachment("""{"in":["stage_id",{"cat":[{"var":"documents"}]}]}""");
        var body = new JsonObject { ["evidence"] = new JsonArray(), ["documents"] = List(await fixture.Upload("document"))["evidence"]!.DeepClone() };
        var outcome = await fixture.Write("later-field", body);
        Assert.True(outcome.Committed, outcome.Refusal?.Code);
        using var row = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        Assert.False(row.RootElement.GetProperty("documents")[0].TryGetProperty("stage_id", out _));
    }

    // ADR-0124 d1 / records-eng-5: calculations must describe the committed attachment value.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_attachment_dependent_calculation_reads_committed_references(bool temporal)
    {
        var calculation = new NamedCalculation("has-text", "1.0.0", """{"if":[{"in":["text/plain",{"cat":[{"var":"evidence"}]}]},"has text","no text"]}""");
        var fixture = await Fixture.CreateWithCalculation(calculation, temporal);
        var body = List(await fixture.Upload("first"));
        body["status"] = "client";
        var result = await fixture.WriteEither(temporal, "derive-reference", body);
        Assert.True(result.Committed, result.Refusal?.Code);
        using var stored = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        Assert.Equal("text/plain", stored.RootElement.GetProperty("evidence")[0].GetProperty("media_type").GetString());
        Assert.Equal("has text", stored.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_attachment_valued_calculation_replaces_supplied_stage_entries(bool temporal)
    {
        var calculation = new NamedCalculation("empty-evidence", "1.0.0", "[]");
        var fixture = await Fixture.CreateWithCalculation(calculation, temporal, attachmentResult: true);
        var body = List(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var result = await fixture.WriteEither(temporal, "derive-stage", body);
        Assert.True(result.Committed, result.Refusal?.Code);
        using var stored = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        Assert.Equal(0, stored.RootElement.GetProperty("evidence").GetArrayLength());
        Assert.Equal(0, fixture.Store.CommittedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_calculated_attachment_cannot_probe_a_stage_absent_from_supplied_fields(bool temporal)
    {
        var calculation = new NamedCalculation("constant-stage", "1.0.0",
            """[{"stage_id":"11111111-1111-1111-1111-111111111111","display_name":"calculated.txt"}]""");
        var fixture = await Fixture.CreateWithCalculation(calculation, temporal, attachmentResult: true);
        var locks = new List<Guid>();
        var calls = new List<string>();
        fixture.Port.DecorateSession = inner =>
        {
            var proxy = DispatchProxy.Create<IContentBookkeepingSession, AuditSession>();
            var observer = (AuditSession)(object)proxy;
            observer.Inner = inner;
            observer.Audits = [];
            observer.StageLocks = locks;
            observer.Calls = calls;
            return proxy;
        };
        var result = await fixture.WriteEither(temporal, "calculated-new-lock", new JsonObject());
        Assert.False(result.Committed);
        Assert.Equal("records.mutate.calculation_failed", result.Refusal!.Code);
        Assert.Equal("mutate", result.Refusal.Stage);
        Assert.Equal(["/evidence"], result.Refusal.Pointers);
        Assert.Empty(calls);
        Assert.Empty(locks);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_calculated_foreign_reference_refuses_before_any_content_call(bool temporal)
    {
        var foreign = await Fixture.Create();
        Assert.True((await foreign.Write("foreign", List(await foreign.Upload("foreign")))).Committed);
        var references = JsonNode.Parse(Assert.Single(foreign.Port.Rows).Body.Span)!["evidence"]!;
        var calculation = new NamedCalculation("foreign-reference", "1.0.0", references.ToJsonString());
        var fixture = await Fixture.CreateWithCalculation(calculation, temporal, attachmentResult: true);
        var calls = new List<string>();
        fixture.Port.DecorateSession = inner =>
        {
            var proxy = DispatchProxy.Create<IContentBookkeepingSession, AuditSession>();
            var observer = (AuditSession)(object)proxy;
            observer.Inner = inner; observer.Audits = []; observer.Calls = calls;
            return proxy;
        };
        var result = await fixture.WriteEither(temporal, "calculated-foreign", new JsonObject());
        Assert.Equal("records.mutate.calculation_failed", result.Refusal!.Code);
        Assert.Equal(["/evidence"], result.Refusal.Pointers);
        Assert.Empty(calls);
        Assert.Empty(fixture.Port.Rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Has_a_PDF_agrees_on_create_effective_create_and_amendment(bool temporal)
    {
        var calculation = new NamedCalculation("has-pdf", "1.0.0", """{"if":[{"in":["application/pdf",{"cat":[{"var":"evidence"}]}]},"has PDF","no PDF"]}""");
        var fixture = await Fixture.CreateWithCalculation(calculation, true);
        var stage = await fixture.Store.PutAsync(new("tenant-a"), new MemoryStream("%PDF-1.7\nfixture"u8.ToArray()), "application/pdf", null, Evidence());
        if (!temporal)
        {
            Assert.True((await fixture.Write("ordinary-pdf", List(stage))).Committed);
        }
        else
        {
            var first = await fixture.Operation.CreateEffectiveAsync(new("tenant-a", "operator-7"), new("eam.evidence", "effective-pdf", List(stage), DateTimeOffset.UnixEpoch, null));
            Assert.Equal(RecordTemporalWriteStatus.Created, first.Status);
            Assert.Equal("has PDF", JsonNode.Parse(fixture.Port.Rows[0].Body.Span)!["status"]!.GetValue<string>());
            var amendment = await fixture.Operation.AmendAsync(new("tenant-a", "operator-7"), new(first.Receipt!.Receipt.RecordId, "amend-pdf", new(first.Receipt.Temporal.Revision, first.Receipt.BindingHash), JsonNode.Parse(fixture.Port.Rows[0].Body.Span)!.AsObject(), DateTimeOffset.UnixEpoch.AddDays(1), null, "keep PDF"));
            Assert.Equal(RecordTemporalWriteStatus.Created, amendment.Status);
        }
        Assert.All(fixture.Port.Rows, row => Assert.Equal("has PDF", JsonNode.Parse(row.Body.Span)!["status"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_mutate_refusal_rolls_back_promoted_inputs(bool temporal)
    {
        var calculation = new NamedCalculation("bad-money", "1.0.0", """{"money.add":[{"var":"originator"},4]}""");
        var fixture = await Fixture.CreateWithCalculation(calculation, temporal);
        var body = List(await fixture.Upload("first")); body["originator"] = "not money";
        var result = await fixture.WriteEither(temporal, "bad-calculation", body);
        Assert.Equal("records.mutate.calculation_failed", result.Refusal!.Code);
        Assert.Equal(["/status"], result.Refusal.Pointers);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
        Assert.Equal(1, fixture.Port.Rollbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_derived_attachment_may_copy_already_locked_references(bool temporal)
    {
        var calculation = new NamedCalculation("copy-documents", "1.0.0", """{"var":"documents"}""");
        var fixture = await Fixture.CreateWithCalculation(calculation, temporal, attachmentResult: true, addDocuments: true);
        var body = new JsonObject { ["documents"] = List(await fixture.Upload("first"))["evidence"]!.DeepClone() };
        var result = await fixture.WriteEither(temporal, "copy-locked", body);
        Assert.True(result.Committed, result.Refusal?.Code);
        var saved = JsonNode.Parse(Assert.Single(fixture.Port.Rows).Body.Span)!;
        Assert.Equal("text/plain", saved["evidence"]![0]!["media_type"]!.GetValue<string>());
        Assert.Equal(5, saved["evidence"]![0]!["length"]!.GetValue<long>());
        Assert.Equal(1, fixture.Store.CommittedCount);
        Assert.Equal(0, fixture.Store.StagedCount);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("accepted")]
    [InlineData("temporal")]
    public async Task An_unbindable_calculated_kind_refuses_at_the_field_pointer_and_rolls_back(string path)
    {
        var calculation = new NamedCalculation("empty-proof", "1.0.0", "[]");
        var fixture = await Fixture.CreateWithUnbindableCalculatedKind(calculation, path);
        var body = new JsonObject();
        var result = path == "accepted"
            ? await fixture.Operation.CompleteAcceptedAsync(new("tenant-a", "sys.records-worker"), Accepted("alice", body))
            : await fixture.WriteEither(path == "temporal", "bad-write-binding", body);
        Assert.False(result.Committed);
        Assert.Equal("validate", result.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", result.Refusal.Code);
        Assert.Equal(["/proof~1~0"], result.Refusal.Pointers);
        Assert.Empty(result.Refusal.Detail);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(1, fixture.Port.Rollbacks);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(0, fixture.Store.StagedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_mutation_after_hashing_cannot_change_the_promoted_stage(bool accepted)
    {
        var fixture = accepted ? await Fixture.CreateFormOnly() : await Fixture.Create();
        var actor = accepted ? "alice" : "operator-7";
        var first = await fixture.UploadAs("first", actor);
        var replacement = await fixture.UploadAs("replacement", actor);
        var body = List(first);
        fixture.Port.BeforeNextLookup = () =>
        {
            body["evidence"] = List(replacement)["evidence"]!.DeepClone();
            return ValueTask.CompletedTask;
        };
        var result = accepted
            ? await fixture.Operation.CompleteAcceptedAsync(new("tenant-a", "sys.records-worker"), Accepted("alice", body))
            : await fixture.Write("snapshot", body);
        Assert.True(result.Committed, result.Refusal?.Code);
        using var stored = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        Assert.Equal(5, stored.RootElement.GetProperty("evidence")[0].GetProperty("length").GetInt64());
        Assert.Equal(1, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
        Assert.Equal(replacement, Assert.Single(await fixture.Storage.Bookkeeping.StagedCreatedAtOrBeforeAsync(DateTimeOffset.MaxValue)).StageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_field_size_refusal_names_the_escaped_destination_pointer(bool temporal)
    {
        var fixture = await Fixture.Create(fieldKey: "photos/~", temporalMode: temporal, fieldLimit: 2);
        var stage = await fixture.Store.PutAsync(new("tenant-a"), new MemoryStream("abc"u8.ToArray()), "text/plain", null, Evidence());
        var result = await fixture.WriteEither(temporal, "field-limit", new JsonObject { ["photos/~"] = new JsonArray(Stage(stage, "evidence.txt")) });
        Assert.False(result.Committed);
        Assert.Equal("content.too-large", result.Refusal!.Code);
        Assert.Equal(["/photos~1~0"], result.Refusal.Pointers);
        Assert.Equal(2, result.Refusal.Detail["limit"]);
        Assert.Equal(3, result.Refusal.Detail["observed"]);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
    }

    [Fact]
    public async Task A_create_cannot_keep_another_records_reference()
    {
        var fixture = await Fixture.Create();
        Assert.True((await fixture.Write("first-record", List(await fixture.Upload("first")))).Committed);
        var prior = JsonNode.Parse(Assert.Single(fixture.Port.Rows).Body.Span)!.AsObject();
        var result = await fixture.Write("another-record", prior);
        Assert.False(result.Committed);
        Assert.Equal("content.staging-expired", result.Refusal!.Code);
        Assert.Equal(["/evidence/0"], result.Refusal.Pointers);
        Assert.Single(fixture.Port.Rows);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("retype")]
    [InlineData("foreign")]
    public async Task A_temporal_kept_entry_uses_the_prior_committed_field_reference(string change)
    {
        var fixture = await Fixture.Create(temporalMode: true);
        var created = await fixture.Operation.CreateEffectiveAsync(new("tenant-a", "operator-7"),
            new("eam.evidence", "initial", List(await fixture.Upload("first")), DateTimeOffset.UnixEpoch, null));
        Assert.Equal(RecordTemporalWriteStatus.Created, created.Status);
        var prior = JsonNode.Parse(Assert.Single(fixture.Port.Rows).Body.Span)!.AsObject();
        if (change == "rename") prior["evidence"]![0]!["display_name"] = "renamed.txt";
        if (change == "retype") prior["evidence"]![0]!["media_type"] = "text/csv";
        if (change == "foreign")
        {
            var other = await fixture.Write("foreign-record", List(await fixture.Upload("other")));
            Assert.True(other.Committed);
            prior["evidence"] = JsonNode.Parse(fixture.Port.Rows[^1].Body.Span)!["evidence"]!.DeepClone();
        }
        var result = await fixture.Operation.AmendAsync(new("tenant-a", "operator-7"), new(created.Receipt!.Receipt.RecordId, "amend",
            new(created.Receipt.Temporal.Revision, created.Receipt.BindingHash), prior, DateTimeOffset.UnixEpoch.AddDays(1), null, "rename evidence"));
        if (change == "rename")
        {
            Assert.Equal(RecordTemporalWriteStatus.Created, result.Status);
            var saved = JsonNode.Parse(fixture.Port.Rows[^1].Body.Span)!;
            Assert.Equal("renamed.txt", saved["evidence"]![0]!["display_name"]!.GetValue<string>());
            Assert.Equal("text/plain", saved["evidence"]![0]!["media_type"]!.GetValue<string>());
            Assert.Equal(5, saved["evidence"]![0]!["length"]!.GetValue<long>());
        }
        else
        {
            Assert.NotNull(result.Refusal);
            Assert.Equal(change == "retype" ? "content.media-type-mismatch" : "content.staging-expired", result.Refusal.Code);
            Assert.Equal(["/evidence/0"], result.Refusal.Pointers);
        }
    }

    // content-store-cc-2, ADR-0142 decision 3 (Terra P1-2): an accepted command is the submitter's write completed by a
    // worker. The submitter's own stage promotes with the worker as audit actor; another principal's stage still refuses.
    [Fact]
    public async Task Content_store_cc_2_an_accepted_command_promotes_the_submitters_stage_and_no_one_elses()
    {
        var fixture = await Fixture.CreateFormOnly();
        var own = await fixture.UploadAs("alice evidence", "alice");
        var audits = new List<KernelAuditEvidence>();
        fixture.Port.DecorateSession = inner =>
        {
            var proxy = DispatchProxy.Create<IContentBookkeepingSession, AuditSession>();
            ((AuditSession)proxy).Inner = inner;
            ((AuditSession)proxy).Audits = audits;
            return proxy;
        };
        var body = List(own);
        body["originator"] = "mallory";
        var completed = await fixture.Operation.CompleteAcceptedAsync(new("tenant-a", "sys.records-worker"), Accepted("alice", body));
        Assert.True(completed.Committed, completed.Refusal?.Code);
        var audit = Assert.Single(audits);
        Assert.Equal("sys.records-worker", audit.ActorId);
        Assert.Equal("alice", audit.OriginatorId);
        var snapshot = AuthoritySnapshotEncoding.TryDecode(audit.AuthoritySnapshot.Span);
        Assert.NotNull(snapshot);
        Assert.Equal("sys.records-worker", snapshot.Principal);
        Assert.Equal("records:complete-accepted", AuthoritySnapshotEncoding.Fact(snapshot, "act", "act:"));
        var written = Assert.Single(fixture.Port.Rows);
        Assert.Equal("sys.records-worker", written.ActorId);
        Assert.Equal("alice", written.OriginatorId);
        using var row = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        Assert.False(row.RootElement.GetProperty("evidence")[0].TryGetProperty("stage_id", out _));
        Assert.Equal(1, fixture.Store.CommittedCount);

        var other = await fixture.UploadAs("mallory evidence", "mallory");
        var stolen = await fixture.Operation.CompleteAcceptedAsync(new("tenant-a", "sys.records-worker"),
            Accepted("alice", List(other)) with { CommandId = "cmd-2", OperationId = "op-accepted-2" });
        Assert.False(stolen.Committed);
        Assert.Equal("content.staging-expired", stolen.Refusal!.Code);
        Assert.Single(fixture.Port.Rows);
    }

    [Theory]
    [InlineData("sys.records-worker")]
    [InlineData("bob")]
    public async Task A_direct_writer_cannot_promote_alices_stage_by_supplying_an_originator_in_the_body(string actor)
    {
        var fixture = await Fixture.Create();
        var stage = await fixture.UploadAs("alice evidence", "alice");
        var body = List(stage);
        body["originator"] = "alice";
        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", actor), new("eam.evidence", "spoof-originator", body));
        Assert.False(outcome.Committed);
        Assert.Equal("content.staging-expired", outcome.Refusal!.Code);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(1, fixture.Store.StagedCount);
        Assert.Equal(0, fixture.Store.CommittedCount);
    }

    public class AuditSession : DispatchProxy
    {
        public IContentBookkeepingSession Inner { get; set; } = null!;
        public List<KernelAuditEvidence> Audits { get; set; } = null!;
        public List<Guid>? StageLocks { get; set; }
        public List<string>? Calls { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls?.Add(targetMethod!.Name);
            if (targetMethod!.Name == "StageAuditAsync") Audits.Add((KernelAuditEvidence)args![0]!);
            if (targetMethod.Name == "FindStagedForUpdateAsync") StageLocks?.Add((Guid)args![0]!);
            return targetMethod.Invoke(Inner, args);
        }
    }

    // ADR-0142 decision 3 (M11 ruling on Terra P1-2): the principal the store sees at commit is the accepted command's
    // server-bound submitter, while the audit actor stays the worker. Bob's accepted command naming Alice's stage refuses content.staging-expired, and the
    // stage stays for Alice's own command.
    [Fact]
    public async Task Adr_0142_another_submitters_accepted_command_naming_a_stage_refuses_staging_expired()
    {
        var fixture = await Fixture.CreateFormOnly();
        var alices = await fixture.UploadAs("alice evidence", "alice");
        var bobs = await fixture.Operation.CompleteAcceptedAsync(new("tenant-a", "sys.records-worker"), Accepted("bob", List(alices)));
        Assert.False(bobs.Committed);
        Assert.Equal("content.staging-expired", bobs.Refusal!.Code);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(1, fixture.Store.StagedCount);
        Assert.Equal(0, fixture.Store.CommittedCount);
        var own = await fixture.Operation.CompleteAcceptedAsync(new("tenant-a", "sys.records-worker"),
            Accepted("alice", List(alices)) with { CommandId = "cmd-3", OperationId = "op-accepted-3" });
        Assert.True(own.Committed, own.Refusal?.Code);
    }

    // content-store-cc-2 with M11's ruling on Terra P1-4: two writes that promote the same content in opposite orders take
    // their row locks in one canonical order, so neither waits on the other forever.
    [Fact]
    public async Task Content_store_cc_2_two_writes_naming_the_same_content_in_opposite_orders_both_commit()
    {
        var fixture = await Fixture.Create();
        var (x1, y1, y2, x2) = (await fixture.Upload("x-bytes"), await fixture.Upload("y-bytes"), await fixture.Upload("y-bytes"), await fixture.Upload("x-bytes"));
        var first = DispatchProxy.Create<IContentBookkeepingSession, RendezvousSession>();
        var second = DispatchProxy.Create<IContentBookkeepingSession, RendezvousSession>();
        ((RendezvousSession)first).Partner = (RendezvousSession)second;
        ((RendezvousSession)second).Partner = (RendezvousSession)first;
        var handed = 0;
        fixture.Port.DecorateSession = inner =>
        {
            var proxy = Interlocked.Increment(ref handed) == 1 ? first : second;
            ((RendezvousSession)proxy).Inner = inner;
            return proxy;
        };
        var writes = Task.WhenAll(fixture.Write("forward", List(x1, y1)).AsTask(), fixture.Write("reverse", List(y2, x2)).AsTask());
        var outcomes = await writes.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(outcomes, outcome => Assert.True(outcome.Committed, outcome.Refusal?.Code));
        Assert.Equal(2, fixture.Port.Rows.Count);
        Assert.Equal(2, fixture.Store.CommittedCount);
    }

    // T-618 (Terra P2-7): the preview reports a kind that does not bind as a refusal; its warning pass never throws.
    [Fact]
    public async Task T_618_preflight_reports_an_unbindable_attachment_kind_instead_of_throwing()
    {
        var fixture = await Fixture.Create();
        var head = (await fixture.Records.GetPublishedHeadAsync("tenant-a", "eam.evidence"))!;
        var binding = head.Fields[0].Binding!;
        var bad = head with { Fields = [head.Fields[0] with { Binding = binding with { Kind = binding.Kind with { Parameters = new Dictionary<string, string> { ["item_limit_bytes"] = "lots" } } } }, .. head.Fields.Skip(1)] };
        var preflight = await fixture.Records.PreflightAsync("tenant-a", bad);
        Assert.Contains(preflight.Refusals, refusal => refusal.Code == "field.kind_parameter_invalid");
        Assert.DoesNotContain(preflight.Warnings, warning => warning.Code == "content.field-limit-above-store");
    }

    // records-ck-8 (Terra P2-8): required_condition is an expression on any field, not only an attachment. A true
    // condition requires a present, non-null value on the plain status field.
    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, "open", true)]
    [InlineData(false, null, true)]
    public async Task Records_ck_8_a_required_condition_on_a_plain_field_requires_it_exactly_when_true(bool attach, string? status, bool expected)
    {
        var fixture = await Fixture.CreateWithStatusCondition("""{"!!":[{"var":"evidence"}]}""");
        var body = attach ? List(await fixture.Upload("evidence")) : new JsonObject { ["evidence"] = new JsonArray() };
        if (status is not null) body["status"] = status;
        var outcome = await fixture.Write("plain-condition", body);
        Assert.Equal(expected, outcome.Committed);
        if (!expected)
        {
            Assert.Equal("field.required", outcome.Refusal!.Code);
            Assert.Equal("rules", outcome.Refusal.Stage);
            Assert.Equal(["/status"], outcome.Refusal.Pointers);
            Assert.Empty(fixture.Port.Rows);
            Assert.Equal(0, fixture.Store.CommittedCount);
        }
    }

    // content-store-cc-2, ADR 0113 (Terra P2-9): the promotion is part of the record's own commit. When that commit fails,
    // no reference lands, the stage survives, and the same stage commits on retry.
    [Fact]
    public async Task Content_store_cc_2_a_failed_record_commit_leaves_no_promoted_reference()
    {
        var fixture = await Fixture.Create();
        var stage = await fixture.Upload("evidence");
        fixture.Port.FailCommit = true;
        await Assert.ThrowsAsync<IOException>(async () => await fixture.Write("commit-fails", List(stage)));
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
        fixture.Port.FailCommit = false;
        Assert.True((await fixture.Write("commit-retry", List(stage))).Committed);
        Assert.Equal(1, fixture.Store.CommittedCount);
    }

    [Fact]
    public async Task Kept_and_new_entries_round_trip_in_order_through_one_enclosing_write()
    {
        var fixture = await Fixture.Create();
        var first = await fixture.Upload("first");
        var created = await fixture.Write("create", List(first));
        using var saved = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        var kept = saved.RootElement.GetProperty("evidence")[0].Clone();
        var next = await fixture.Upload("second");
        var mixed = JsonSerializer.SerializeToElement(new JsonArray(JsonNode.Parse(kept.GetRawText()), Stage(next, "new.txt")));
        // The host boundary models a subsequent write of the same record, with the existing reference kept verbatim.
        JsonArray? resolved = null;
        var port = new ArrayPort(fixture.Storage.Bookkeeping);
        await KernelTransactionBoundary.ExecutePreparedAsync(async token =>
        {
            resolved = await fixture.Runtime.ResolveWriteAsync(fixture.Attachment, mixed, new("tenant-a"), new(created.Receipt!.RecordId, "evidence"),
                Evidence(port.Session), saved.RootElement.GetProperty("evidence"), token);
            return new KernelCommand<JsonArray>(new("mixed", "mixed", "literal-fingerprint"), resolved, Evidence().Audit);
        }, port);
        var read = Assert.IsType<JsonArray>(port.Saved);
        Assert.Equal(kept.GetRawText(), JsonSerializer.SerializeToElement(read[0]).GetRawText());
        Assert.Equal(["first.txt", "new.txt"], read.Select(item => item!["display_name"]!.GetValue<string>()));
        Assert.All(read, item => Assert.Null(item!["stage_id"]));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Records_refuses_missing_attachment_runtime_or_transaction_instead_of_committing_separately(bool runtime, bool session)
    {
        var fixture = await Fixture.Create(composeRuntime: runtime, exposeSession: session);
        var stage = await fixture.Upload("evidence");
        var outcome = await fixture.Write("uncomposed", List(stage));
        Assert.False(outcome.Committed);
        Assert.Equal("bind", outcome.Refusal!.Stage);
        Assert.Equal("content.transaction-required", outcome.Refusal.Code);
        Assert.Equal(["/evidence"], outcome.Refusal.Pointers);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Concurrent_attachment_retries_settle_the_committed_operation_after_stage_consumption(bool temporal, bool divergent)
    {
        var fixture = await Fixture.Create(temporalMode: temporal);
        var stage = await fixture.Upload("evidence");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Port.BeforeNextLookup = async () => { entered.SetResult(); await release.Task.ConfigureAwait(false); };
        var loserBody = List(stage);
        if (divergent) loserBody["status"] = "different intent";
        var loser = fixture.WriteEither(temporal, "same-operation", loserBody);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var winner = await fixture.WriteEither(temporal, "same-operation", List(stage));
        Assert.True(winner.Committed); Assert.False(winner.Replayed); Assert.Null(winner.Decision);
        release.SetResult();
        var settled = await loser.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(settled.Decision!.Allowed);
        Assert.NotEqual(winner.Receipt!.RecordId, settled.Decision.RecordId);
        if (divergent)
        {
            Assert.False(settled.Committed);
            Assert.Equal("commit", settled.Refusal!.Stage);
            Assert.Equal("records.write.replay_conflict", settled.Refusal.Code);
            Assert.Equal(["/operation_id"], settled.Refusal.Pointers);
        }
        else
        {
            Assert.True(settled.Replayed, settled.Refusal?.Code);
            Assert.Equal(winner.Receipt, settled.Receipt);
        }
        Assert.Single(fixture.Port.Rows);
        Assert.Equal(1, fixture.Port.Rollbacks);
        Assert.Equal(1, fixture.Store.CommittedCount);
        Assert.Equal(0, fixture.Store.StagedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raced_replay_does_not_capture_a_callers_context_during_receipt_IO(bool temporal)
    {
        var fixture = await Fixture.Create(temporalMode: temporal);
        var stage = await fixture.Upload("evidence");
        var original = await fixture.WriteEither(temporal, "receipt-io", List(stage));
        Assert.True(original.Committed);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = false;
        fixture.Port.HideNextLookup = true;
        fixture.Port.BeforeNextLookup = async () => { entered = true; await release.Task.ConfigureAwait(false); };
        var context = new CountingContext(); var previous = SynchronizationContext.Current;
        Task<RecordWriteOutcome> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = fixture.WriteEither(temporal, "receipt-io", List(stage));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(entered); Assert.False(pending.IsCompleted);
        release.SetResult();
        var replay = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(replay.Replayed, replay.Refusal?.Code);
        Assert.Equal(original.Receipt, replay.Receipt);
        Assert.Equal(0, context.Posts);
        Assert.Equal(1, fixture.Port.Rollbacks);
        Assert.Single(fixture.Port.Rows);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Temporal_unknown_or_consumed_stages_under_a_new_operation_remain_refusals(bool consumed, int rows)
    {
        var fixture = await Fixture.Create(temporalMode: true);
        var stage = consumed ? await fixture.Upload("evidence") : Guid.Parse("11111111-1111-1111-1111-111111111111");
        if (consumed) Assert.True((await fixture.WriteEither(true, "first-operation", List(stage))).Committed);
        var outcome = await fixture.Operation.CreateEffectiveAsync(new("tenant-a", "operator-7"),
            new("eam.evidence", "new-operation", List(stage), DateTimeOffset.UnixEpoch, null));
        Assert.Equal(RecordTemporalWriteStatus.Refused, outcome.Status);
        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("content.staging-expired", outcome.Refusal.Code);
        Assert.Empty(outcome.Refusal.Detail);
        Assert.True(outcome.Decision!.Allowed);
        Assert.Equal(rows, fixture.Port.Rows.Count);
        Assert.Equal(rows, fixture.Store.CommittedCount);
        Assert.Equal(0, fixture.Store.StagedCount);
        Assert.Equal(1, fixture.Port.Rollbacks);
    }

    [Theory]
    [InlineData("evidence", "/evidence")]
    [InlineData("proof/~", "/proof~1~0")]
    public async Task Malformed_entries_become_a_structured_refusal_with_an_escaped_field_pointer(string field, string pointer)
    {
        var fixture = await Fixture.Create(fieldKey: field);
        var outcome = await fixture.Write("malformed", new JsonObject { [field] = new JsonArray(new JsonObject()) });
        Assert.False(outcome.Committed);
        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", outcome.Refusal.Code);
        Assert.Equal([pointer], outcome.Refusal.Pointers);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
    }

    [Fact]
    public async Task Records_does_not_capture_a_callers_context_while_promoting_a_stage()
    {
        var fixture = await Fixture.Create();
        var stage = await fixture.Upload("evidence");
        var session = DispatchProxy.Create<IContentBookkeepingSession, DelayedSession>();
        var delay = (DelayedSession)session;
        fixture.Port.DecorateSession = inner => { delay.Inner = inner; return session; };
        var context = new CountingContext(); var previous = SynchronizationContext.Current;
        Task<RecordWriteOutcome> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = fixture.Write("context", List(stage)).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(delay.Entered); Assert.False(pending.IsCompleted);
        delay.Release.SetResult();
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(30))).Committed);
        Assert.Equal(0, context.Posts);
        Assert.Single(fixture.Port.Rows);
    }

    private sealed class CountingContext : SynchronizationContext
    {
        private int posts;
        public int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        { Interlocked.Increment(ref posts); ThreadPool.QueueUserWorkItem(_ => callback(state)); }
    }
    public class DelayedSession : DispatchProxy
    {
        public IContentBookkeepingSession Inner { get; set; } = null!;
        public bool Entered { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name != "FindStagedForUpdateAsync") return result;
            Entered = true; return Wait((ValueTask<ContentStoreItem?>)result!);
        }
        private async ValueTask<ContentStoreItem?> Wait(ValueTask<ContentStoreItem?> operation)
        { await Release.Task.ConfigureAwait(false); return await operation.ConfigureAwait(false); }
    }

    private static JsonObject List(params Guid[] stages) => new() { ["evidence"] = new JsonArray(stages.Select((stage, index) => (JsonNode)Stage(stage, index == 0 ? "first.txt" : "second.txt")).ToArray()) };
    private static JsonObject Stage(Guid stage, string name) => new() { ["stage_id"] = stage.ToString(), ["display_name"] = name };
    private static ContentStoreWrite Evidence(IContentBookkeepingSession? session = null, string actor = "operator-7") => new(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "literal-fingerprint"), new(Guid.NewGuid().ToString("N"), actor, DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty), session);

    // The acceptance a form's submit gate committed for the submitter, in the shape AcceptedRecordCommandReadings admits.
    private static AcceptedRecordCreate Accepted(string submitter, JsonObject body)
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        var snapshot = AuthoritySnapshotEncoding.Encode(new AuthorizationDecisionEvidence(
            new AccessRequest("forms:submit-gate", submitter, "tenant-a", new AccessRecord("tenant-a", "form", "capture@1.0.0", new Dictionary<string, JsonNode?>()), at),
            true, "None", "grant:fixture@1", [], [], kind: "Gate"));
        var command = new AcceptedRecordCreate("tenant-a", "cmd-1", submitter, "op-accepted-1", "eam.evidence", body,
            "records-write-intent/1", "", "capture@1.0.0", "acceptance-audit-1", at, at, snapshot);
        return command with { BodyDigest = RecordWriteIntent.Digest(RecordWriteIntent.Encode(command.RecordTypeId, null, command.Body)) };
    }

    // Lets two transactions meet after each takes its first committed-row lock, so a lock-order inversion deadlocks
    // deterministically; a transaction whose partner is blocked waits out the bound and carries on.
    public class RendezvousSession : DispatchProxy
    {
        public IContentBookkeepingSession Inner { get; set; } = null!;
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RendezvousSession Partner { get; set; } = null!;
        private int met;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            // Every call that takes a committed row's lock: a lock-ahead read, a conditional create, or adding an owner.
            if (targetMethod.Name is not ("FindItemForUpdateAsync" or "TryCreateItemAsync" or "AddOwnerAsync")
                || Interlocked.Exchange(ref met, 1) == 1) return result;
            return result switch
            {
                ValueTask<ContentStoreItem?> item => Meet(item),
                ValueTask<bool> added => Meet(added),
                _ => result,
            };
        }
        private async ValueTask<T> Meet<T>(ValueTask<T> operation)
        {
            var value = await operation.ConfigureAwait(false);
            Arrived.TrySetResult();
            try { await Partner.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            return value;
        }
    }

    private sealed class Fixture
    {
        public InMemoryContentStoreStorage Storage { get; } = new();
        public InMemoryContentStore Store { get; }
        public AttachmentFieldRuntime Runtime { get; }
        public ICompiledAttachmentFieldKind Attachment { get; }
        public RecordTypeDefinitionStore Records { get; }
        public InMemorySchemaRegistry Registry { get; }
        public Port Port { get; }
        public RecordWriteOperation Operation { get; }
        public RecordTypePublication Publication { get; }
        private Fixture(bool condition, bool composeRuntime, bool exposeSession, string fieldKey, bool temporalMode, string? conditionJson = null, bool formOnly = false, string? statusCondition = null, long fieldLimit = 200, NamedCalculation? calculation = null, bool attachmentResult = false, bool failWriteBinding = false)
        {
            Store = new(new(100, 1000, 10000, TimeSpan.FromHours(1)), new Keys(), TimeProvider.System, Storage);
            Runtime = new(Store);
            var kinds = new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), Store.ItemLimitBytes);
            var kind = new FieldKindReference("attachment", "1.0.0", new Dictionary<string, string> { ["item_limit_bytes"] = fieldLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            Attachment = (ICompiledAttachmentFieldKind)kinds.Bind(kind, "/kind");
            var validator = new RecordsIntentValidator();
            var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission());
            Registry = new(fieldKindRuntime: kinds);
            var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
            { [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(Window, validator), [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window) });
            var classes = new ClassDefinitionStore(catalogue);
            classes.CreateDraftAsync(new("tenant-a", "eam", "Equipment", "1.0.0", new(1, 0), "eam-core"), "class").AsTask().GetAwaiter().GetResult();
            classes.PublishAsync("tenant-a", "eam.equipment", "1.0.0", 1, "class-publish").AsTask().GetAwaiter().GetResult();
            Records = new(catalogue, compiler, new(kinds), Registry, Window, roles: CreationRoles);
            var field = new FieldDefinition(fieldKey, "Evidence", new(kind, new(false, 0, null, [], null)))
            { RequiredCondition = conditionJson is not null ? JsonSerializer.Deserialize<JsonElement>(conditionJson) : condition ? JsonSerializer.Deserialize<JsonElement>("""{"==":[{"var":"status"},"closed"]}""") : null };
            var status = new FieldDefinition("status", "Status") { RequiredCondition = statusCondition is null ? null : JsonSerializer.Deserialize<JsonElement>(statusCondition) };
            if (calculation is not null)
            {
                var pin = new FieldCalculationPin(calculation.Pin.Name, calculation.Pin.Version, calculation.Pin.Digest);
                if (attachmentResult) field = field with { Calculation = pin };
                else status = status with { Calculation = pin };
            }
            FieldDefinition[] fields = conditionJson is null ? [field, status] : [field, status, new("documents", "Documents", new(kind, new(false, 0, null, [], null)))];
            fields = [.. fields, new("originator", "Client-supplied data, not identity")];
            Records.CreateDraftAsync(new("tenant-a", "eam", "Evidence", "1.0.0", fields, new(1, 0),
                ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core", CreationGate: formOnly ? RecordCreationGate.FormOnly : RecordCreationGate.Role, CreationRoleId: formOnly ? null : "asset-author") { AmendmentPolicy = new(true) { Mode = temporalMode ? RecordAmendmentMode.Supersession : null } }, "type").AsTask().GetAwaiter().GetResult();
            Publication = Records.PublishAsync("tenant-a", "eam.evidence", "1.0.0", 1, "publish").AsTask().GetAwaiter().GetResult();
            Port = new(Storage.Bookkeeping) { ExposeSession = exposeSession };
            var writeKinds = failWriteBinding ? new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)])) : kinds;
            Operation = new(new AccessProvider(new Gate()), Records, Registry, Port, Port, TimeProvider.System, writeKinds, RecordWriteFieldRuntimeTests.FixtureDomains(TimeProvider.System), Port, Port, Port) { Attachments = composeRuntime ? Runtime : null, Calculations = calculation is null ? null : new([], [calculation]) };
        }
        public static Task<Fixture> CreateWithCalculation(NamedCalculation calculation, bool temporalMode, bool attachmentResult = false, bool addDocuments = false) =>
            Task.FromResult(new Fixture(false, true, true, "evidence", temporalMode, conditionJson: addDocuments ? """{"==":[1,2]}""" : null, calculation: calculation, attachmentResult: attachmentResult));
        public static Task<Fixture> CreateWithUnbindableCalculatedKind(NamedCalculation calculation, string path) =>
            Task.FromResult(new Fixture(false, true, true, "proof/~", path == "temporal", formOnly: path == "accepted",
                calculation: calculation, attachmentResult: true, failWriteBinding: true));
        public static Task<Fixture> CreateFormOnly() => Task.FromResult(new Fixture(false, true, true, "evidence", false, formOnly: true));
        public static Task<Fixture> CreateWithStatusCondition(string statusCondition) => Task.FromResult(new Fixture(false, true, true, "evidence", false, statusCondition: statusCondition));
        public ValueTask<Guid> UploadAs(string text, string actor) => Runtime.PutAsync(Attachment, new("tenant-a"), "evidence", new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/plain", null, Evidence(actor: actor));
        public static Task<Fixture> CreateWithLaterAttachment(string conditionJson) => Task.FromResult(new Fixture(false, true, true, "evidence", false, conditionJson));
        public static Task<Fixture> Create(bool condition = false, bool composeRuntime = true, bool exposeSession = true, string fieldKey = "evidence", bool temporalMode = false, long fieldLimit = 200) => Task.FromResult(new Fixture(condition, composeRuntime, exposeSession, fieldKey, temporalMode, fieldLimit: fieldLimit));
        public ValueTask<Guid> Upload(string text) => Runtime.PutAsync(Attachment, new("tenant-a"), "evidence", new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/plain", null, Evidence());
        public async Task<RecordWriteOutcome> WriteEither(bool temporal, string operation, JsonObject body)
        {
            if (!temporal) return await Write(operation, body).ConfigureAwait(false);
            var outcome = await Operation.CreateEffectiveAsync(new("tenant-a", "operator-7"), new("eam.evidence", operation, body, DateTimeOffset.UnixEpoch, null)).ConfigureAwait(false);
            return new(outcome.Receipt?.Receipt, outcome.Status == RecordTemporalWriteStatus.Replayed, outcome.Refusal) { Decision = outcome.Decision };
        }
        public ValueTask<RecordWriteOutcome> Write(string operation, JsonObject body) => Operation.CreateAsync(new("tenant-a", "operator-7"), new("eam.evidence", operation, body));
    }
    private sealed class Keys : ITenantContentKeyProvider { public byte[] GetKey(TenantId tenant) => SHA256.HashData(Encoding.UTF8.GetBytes("test:" + tenant.Value)); }
    private sealed class Gate : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, true, "None", "grant:writer", [], []));
    }
    private sealed class Port(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader,
        IRecordTemporalWritePort, IRecordTemporalReceiptReader, IRecordTemporalReadStore
    {
        public bool ExposeSession { get; init; } = true;
        public bool FailCommit { get; set; }
        public Func<IContentBookkeepingSession, IContentBookkeepingSession>? DecorateSession { get; set; }
        public List<RecordWriteRow> Rows { get; } = [];
        public List<RecordTemporalVersion> Versions { get; } = [];
        public Func<ValueTask>? BeforeNextLookup { get; set; }
        public bool HideNextLookup { get; set; }
        public int Rollbacks { get; private set; }
        public bool HasAuthorityClock => true;
        private readonly Dictionary<string, RecordTemporalCommittedOperation> temporalReceipts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RecordWriteCommittedOperation> receipts = new(StringComparer.Ordinal);
        public async ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        { var prior = receipts.GetValueOrDefault(key.IdempotencyKey); if (HideNextLookup) { HideNextLookup = false; return null; } await PauseLookupAsync().ConfigureAwait(false); return prior; }
        public async ValueTask<RecordTemporalCommittedOperation?> FindTemporalOperationAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        { var prior = temporalReceipts.GetValueOrDefault(key.IdempotencyKey); if (HideNextLookup) { HideNextLookup = false; return null; } await PauseLookupAsync().ConfigureAwait(false); return prior; }
        private async ValueTask PauseLookupAsync()
        { var pause = BeforeNextLookup; BeforeNextLookup = null; if (pause is not null) await pause().ConfigureAwait(false); }
        public ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RecordTemporalLookup> FindAtAsync(string tenant, string recordId, DateTimeOffset effectiveAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RecordTemporalHistory> HistoryAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RecordTemporalHistory(RecordTemporalHistoryKind.Temporal, Versions.Where(v => v.Row.Tenant == tenant && v.Row.RecordId == recordId).ToArray()));
        public ValueTask<RecordTemporalTarget?> FindTemporalTargetAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Versions.FirstOrDefault(v => v.Row.Tenant == tenant && v.Row.RecordId == recordId) is { } prior ? new RecordTemporalTarget(prior.Row.RecordTypeId, true) : null);
        async ValueTask<IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>> IKernelPreparedTransactionPort<RecordTemporalWrite, RecordTemporalCommit>.BeginAsync(CancellationToken cancellationToken)
            => new TemporalTransaction(this, await bookkeeping.BeginTransactionAsync(cancellationToken));
        private sealed class TemporalTransaction(Port owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>, IRecordContentTransaction
        {
            public IContentBookkeepingSession ContentSession => owner.DecorateSession?.Invoke(content) ?? content;
            private RecordTemporalWrite write = null!;
            private KernelOperationIdentity operation = null!;
            private KernelAuditEvidence audit = null!;
            public ValueTask StageOperationAsync(KernelOperationIdentity value, CancellationToken cancellationToken = default) { operation = value; return ((IContentBookkeepingSession)content).StageOperationAsync(value, cancellationToken); }
            public ValueTask StageRecordAsync(RecordTemporalWrite value, CancellationToken cancellationToken = default) { write = value; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence value, CancellationToken cancellationToken = default) { audit = value; return ((IContentBookkeepingSession)content).StageAuditAsync(value, cancellationToken); }
            public async ValueTask<RecordTemporalCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                var row = write.Row;
                var temporal = new RecordTemporalEvidence((write.ExpectedPredecessor?.Revision ?? 0) + 1, write.ExpectedPredecessor, [], write.EffectiveFrom, write.EffectiveTo, write.Reason, DateTimeOffset.UnixEpoch);
                var receipt = new RecordTemporalReceipt(new(row.RecordId, row.OperationId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, row.RecordedAt),
                    temporal, RecordTemporalJournalBinding.HashBinding(operation.IdempotencyKey, operation, row, audit, temporal));
                await content.CommitAsync(cancellationToken);
                owner.Versions.Add(new(row, temporal, receipt.BindingHash));
                owner.Rows.Add(row); owner.temporalReceipts.Add(operation.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                return new(RecordTemporalCommitDisposition.Created, receipt);
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) { owner.Rollbacks++; return content.RollbackAsync(cancellationToken); }
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
        public async ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            var transaction = new Transaction(this, await bookkeeping.BeginTransactionAsync(cancellationToken));
            return ExposeSession ? transaction : new WithoutContent(transaction);
        }
        private sealed class WithoutContent(IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit> inner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => inner.StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(RecordWriteRow row, CancellationToken cancellationToken = default) => inner.StageRecordAsync(row, cancellationToken);
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.StageAuditAsync(audit, cancellationToken);
            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
        private sealed class Transaction(Port owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>, IRecordContentTransaction
        {
            public IContentBookkeepingSession ContentSession => owner.DecorateSession?.Invoke(content) ?? content;
            private RecordWriteRow row = null!;
            private KernelOperationIdentity operation = null!;
            public ValueTask StageOperationAsync(KernelOperationIdentity value, CancellationToken cancellationToken = default) { operation = value; return ((IContentBookkeepingSession)content).StageOperationAsync(value, cancellationToken); }
            public ValueTask StageRecordAsync(RecordWriteRow value, CancellationToken cancellationToken = default) { row = value; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public async ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                // The host's one database commit fails as a whole: neither the record nor its content bookkeeping lands.
                if (owner.FailCommit) throw new IOException("The record store's commit failed.");
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, row.RecordedAt);
                await content.CommitAsync(cancellationToken);
                owner.Rows.Add(row); owner.receipts.Add(operation.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                return new(RecordWriteCommitDisposition.Created, receipt);
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) { owner.Rollbacks++; return content.RollbackAsync(cancellationToken); }
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }
    private sealed class ArrayPort(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<JsonArray, bool>
    {
        public IContentBookkeepingSession Session { get; private set; } = null!;
        public JsonArray? Saved { get; private set; }
        public async ValueTask<IKernelPreparedTransaction<JsonArray, bool>> BeginAsync(CancellationToken cancellationToken = default)
        { var transaction = await bookkeeping.BeginTransactionAsync(cancellationToken); Session = transaction; return new Transaction(this, transaction); }
        private sealed class Transaction(ArrayPort owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<JsonArray, bool>
        {
            private JsonArray pending = null!;
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(JsonArray record, CancellationToken cancellationToken = default) { pending = record; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public async ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default) { await content.CommitAsync(cancellationToken); owner.Saved = pending; return true; }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => content.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }
}
