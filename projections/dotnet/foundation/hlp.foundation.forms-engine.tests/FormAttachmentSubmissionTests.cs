using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.ContentStore;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

// Oracles: ADR-0142 decisions 1-3, DES-0054 cc-1/cc-2 and the literal submitted order, names, codes and pointers.
public sealed class FormAttachmentSubmissionTests
{
    [Fact]
    public async Task Stages_are_delivered_unchanged_and_only_Records_commits_ordered_references()
    {
        var f = await Fixture.Create();
        var one = await f.Upload("first");
        var two = await f.Upload("second");
        using var candidate = Candidate(one, two);
        var original = candidate.RootElement.GetRawText();
        Assert.False((await f.Registry.ValidateAsync(new(f.Form.Definition.SchemaRef.Value), Encoding.UTF8.GetBytes(original))).IsValid);
        Assert.True((await f.Form.Engine.ValidateAsync(f.Form.Definition.Id, candidate)).IsValid);
        var receipt = await f.Form.Engine.SubmitAsync(new(f.Form.Definition.Id, candidate, "staged"));
        Assert.Equal(0, f.Store.CommittedCount);
        Assert.Equal(2, f.Store.StagedCount);
        Assert.Equal(original, Assert.Single(f.BodyRequests));
        var envelope = Assert.Single(f.Form.State.Idempotency.Values).Projection;
        using var delivered = JsonDocument.Parse(envelope.ProtectedAcceptedValues);
        Assert.Equal(original, delivered.RootElement.GetRawText());

        f.Execute = true;
        await f.Form.Engine.RecoverProjectionsAsync(10);

        Assert.True(Assert.Single(f.Outcomes).Committed, f.Outcomes[0].Refusal?.Code);
        var row = Assert.Single(f.Rows.Rows);
        using var stored = JsonDocument.Parse(row.Body);
        var entries = stored.RootElement.GetProperty("proof/~");
        Assert.Equal(["first.txt", "second.txt"], entries.EnumerateArray().Select(e => e.GetProperty("display_name").GetString()));
        Assert.All(entries.EnumerateArray(), entry =>
        {
            Assert.False(entry.TryGetProperty("stage_id", out _));
            Assert.Equal(row.RecordId, entry.GetProperty("owner").GetProperty("record").GetString());
            Assert.Equal("text/plain", entry.GetProperty("media_type").GetString());
        });
        Assert.Equal("sys.forms-worker", row.ActorId);
        Assert.Equal(2, f.Store.CommittedCount);
        Assert.Equal(0, f.Store.StagedCount);
        Assert.True((await f.Registry.ValidateAsync(row.Schema, row.Body)).IsValid);
        Assert.False((await f.Registry.ValidateAsync(row.Schema, envelope.ProtectedAcceptedValues)).IsValid);
        Assert.Equal(original, candidate.RootElement.GetRawText());
    }

    [Fact]
    public async Task Another_principals_stage_refuses_using_server_bound_submitter_not_request_data()
    {
        var f = await Fixture.Create();
        var stage = await f.Upload("first", "bob");
        using var candidate = Candidate(stage);
        var body = JsonNode.Parse(candidate.RootElement.GetRawText())!.AsObject();
        body["originator"] = "bob";
        using var spoofed = JsonDocument.Parse(body.ToJsonString());
        await f.Form.Engine.SubmitAsync(new(f.Form.Definition.Id, spoofed, "foreign"));
        f.Execute = true;
        await f.Form.Engine.RecoverProjectionsAsync(10);
        var refusal = Assert.Single(f.Outcomes).Refusal!;
        Assert.Equal("content.staging-expired", refusal.Code);
        Assert.Empty(f.Rows.Rows);
        Assert.Equal(1, f.Store.StagedCount);
    }

    [Fact]
    public async Task Kept_reference_on_a_form_create_refuses_at_the_same_Records_pointer()
    {
        var f = await Fixture.Create();
        using var candidate = JsonDocument.Parse("""{"proof/~":[{"content_id":"hmac-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","length":1,"media_type":"text/plain","display_name":"kept.txt","owner":{"record":"prior","element":"proof/~"}}]}""");
        Assert.True((await f.Form.Engine.ValidateAsync(f.Form.Definition.Id, candidate)).IsValid);
        await f.Form.Engine.SubmitAsync(new(f.Form.Definition.Id, candidate, "kept"));
        f.Execute = true;
        await f.Form.Engine.RecoverProjectionsAsync(10);
        var refusal = Assert.Single(f.Outcomes).Refusal!;
        Assert.Equal("content.staging-expired", refusal.Code);
        Assert.Equal(["/proof~1~0/0"], refusal.Pointers);
        Assert.Empty(f.Rows.Rows);
    }

    [Theory]
    [InlineData("[{}]", "field.value_type_mismatch", "/proof~1~0/0")]
    [InlineData("[{\"stage_id\":\"bad\",\"display_name\":\"x\"}]", "field.value_type_mismatch", "/proof~1~0/0")]
    [InlineData("[{\"stage_id\":\"11111111-1111-1111-1111-111111111111\",\"display_name\":\"x\",\"extra\":true}]", "field.value_type_mismatch", "/proof~1~0/0")]
    [InlineData("[{\"content_id\":\"hmac-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"length\":51,\"media_type\":\"text/plain\",\"display_name\":\"x\",\"owner\":{\"record\":\"r\",\"element\":\"e\"}}]", "content.too-large", "/proof~1~0/0")]
    [InlineData("[{\"content_id\":\"hmac-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"length\":1,\"media_type\":\"image/png\",\"display_name\":\"x\",\"owner\":{\"record\":\"r\",\"element\":\"e\"}}]", "content.field-media-type-not-allowed", "/proof~1~0/0")]
    public async Task Shared_field_submission_validation_keeps_codes_and_escaped_pointers(string entries, string code, string jsonPointer)
    {
        var f = await Fixture.Create();
        using var candidate = JsonDocument.Parse("{\"proof/~\":" + entries + "}");
        var result = await f.Form.Engine.ValidateAsync(f.Form.Definition.Id, candidate);
        Assert.Contains(result.Errors, e => e.Code.HasValue && e.Code.Value == code && e.JsonPointer == jsonPointer);
        await Assert.ThrowsAsync<FormEngineValidationException>(async () => await f.Form.Engine.SubmitAsync(new(f.Form.Definition.Id, candidate, "invalid")));
        Assert.Empty(f.Rows.Rows);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("42")]
    public async Task Attachment_array_shape_and_sibling_schema_are_not_relaxed(string value)
    {
        var f = await Fixture.Create();
        using var candidate = JsonDocument.Parse("{\"proof/~\":" + value + "}");
        Assert.False((await f.Form.Engine.ValidateAsync(f.Form.Definition.Id, candidate)).IsValid);
        using var sibling = JsonDocument.Parse("""{"proof/~":[],"originator":42}""");
        Assert.False((await f.Form.Engine.ValidateAsync(f.Form.Definition.Id, sibling)).IsValid);
    }

    [Fact]
    public async Task Unbound_forms_keep_the_strict_read_grammar()
    {
        var f = await Fixture.Create();
        var schema = await f.Registry.GetAsync(new(f.Form.Definition.SchemaRef.Value));
        var unbound = await Harness.CreateAsync(schemaRegistry: f.Registry, schemaJson: schema!.JsonSchemaText);
        using var candidate = Candidate(await f.Upload("first"));
        Assert.False((await unbound.Engine.ValidateAsync(unbound.Definition.Id, candidate)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RecordTypeCreationGate.Role)]
    public async Task Bound_attachments_without_an_accepted_Records_command_refuse_stages(RecordTypeCreationGate? creationGate)
    {
        var f = await Fixture.Create(creationGate: creationGate);
        using var candidate = Candidate(await f.Upload("first"));
        // Oracle: ADR-0142 decision 2, DES-0054 cc-1/cc-2 and DES-0030 cc-13 require references outside a Records write.
        // Required reference members are missing at entry 0; RFC 6901 escapes the fixture's proof/~ field as proof~1~0.
        var result = await f.Form.Engine.ValidateAsync(f.Form.Definition.Id, candidate);
        Assert.Contains(("required", "/proof~1~0/0"), result.Errors.Select(e => (e.Code.Value, e.JsonPointer)));
        var refusal = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await f.Form.Engine.SubmitAsync(new(f.Form.Definition.Id, candidate, "no-accepted-command")));
        Assert.Contains(("required", "/proof~1~0/0"), refusal.Errors.Select(e => (e.Code.Value, e.JsonPointer)));
        Assert.Empty(f.Form.State.Commands);
        Assert.Empty(f.Form.State.Idempotency);
    }

    [Fact]
    public async Task Required_attachment_count_refuses_before_acceptance_with_the_shared_code()
    {
        var f = await Fixture.Create(required: true);
        using var candidate = JsonDocument.Parse("""{"proof/~":[]}""");
        var result = await f.Form.Engine.ValidateAsync(f.Form.Definition.Id, candidate);
        Assert.Contains(result.Errors, e => e.Code.HasValue && e.Code.Value == "field.required" && e.JsonPointer == "/proof~1~0");
        await Assert.ThrowsAsync<FormEngineValidationException>(async () => await f.Form.Engine.SubmitAsync(new(f.Form.Definition.Id, candidate, "empty")));
        Assert.Empty(f.Form.State.Commands);
    }

    [Fact]
    public async Task Staged_size_media_and_evidence_constraints_refuse_in_the_Records_create()
    {
        foreach (var scenario in new[] { "size", "media", "evidence" })
        {
            var f = await Fixture.Create(evidence: scenario == "evidence");
            var stage = await f.Store.PutAsync(new("tenant-engine"), new MemoryStream(Encoding.UTF8.GetBytes(scenario == "size" ? new string('x', 51) : "%PDF-1.7\nfile")), scenario == "size" ? "text/plain" : "application/pdf", null, Evidence("alice"));
            using var candidate = scenario == "evidence" ? JsonDocument.Parse("""{"proof/~":[]}""") : Candidate(stage);
            await f.Form.Engine.SubmitAsync(new(f.Form.Definition.Id, candidate, scenario));
            f.Execute = true;
        await f.Form.Engine.RecoverProjectionsAsync(10);
            var refusal = Assert.Single(f.Outcomes).Refusal!;
            Assert.Equal(scenario switch { "size" => "content.too-large", "media" => "content.field-media-type-not-allowed", _ => "field.evidence_required" }, refusal.Code);
            Assert.Contains("/proof~1~0", refusal.Pointers);
            Assert.Empty(f.Rows.Rows);
            Assert.Equal(0, f.Store.CommittedCount);
        }
    }

    private static JsonDocument Candidate(params Guid[] stages) => JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
    { ["proof/~"] = stages.Select((id, i) => new { stage_id = id, display_name = i == 0 ? "first.txt" : "second.txt" }).ToArray() }));
    private static ContentStoreWrite Evidence(string actor) => new(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "fixture"), new(Guid.NewGuid().ToString("N"), actor, DateTimeOffset.UtcNow, ReadOnlyMemory<byte>.Empty));

    private sealed class Fixture : IFormFieldBindingSource, IFormProjectionSink, IFormAcceptedRecordBodies, IFormRecordTypePolicyReader
    {
        public InMemoryContentStore Store { get; private set; } = null!;
        public InMemorySchemaRegistry Registry { get; private set; } = null!;
        public Harness Form { get; private set; } = null!;
        public RowPort Rows { get; private set; } = null!;
        public List<RecordWriteOutcome> Outcomes { get; } = [];
        public List<string> BodyRequests { get; } = [];
        private RecordWriteOperation operation = null!;
        private NewRecordType type = null!;
        private RecordTypeCreationGate? creationGate;
        public bool Execute { get; set; }
        public static async Task<Fixture> Create(bool evidence = false, bool required = false, RecordTypeCreationGate? creationGate = RecordTypeCreationGate.FormOnly)
        {
            var f = new Fixture { creationGate = creationGate };
            var storage = new InMemoryContentStoreStorage();
            f.Store = new(new(100, 1000, 10000, TimeSpan.FromHours(1)), new Keys(), TimeProvider.System, storage);
            var kinds = new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference), new("text", "1.0.0", null, FieldScalarValueShape.Text)]), 100);
            var kind = new FieldKindReference("attachment", "1.0.0", new Dictionary<string, string> { ["item_limit_bytes"] = "50", ["accepted_media_types"] = "[\"text/plain\"]" });

            var domains = new ValueDomainRuntime(new SharedFieldBindingTests.FieldHost { Domain = null }, new ReadAuthority(), TimeProvider.System);
            var validator = new RecordsIntentValidator();
            var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission());
            var window = new DefinitionContractWindow(1, 0, 1);
            var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
            { [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(window, validator), [DefinitionKind.Classes] = ClassDefinitionStore.Admission(window) });
            var classes = new ClassDefinitionStore(catalogue);
            await classes.CreateDraftAsync(new("tenant-engine", "eam", "Equipment", "1.0.0", new(1, 0), "eam-core"), "class");
            await classes.PublishAsync("tenant-engine", "eam.equipment", "1.0.0", 1, "publish-class");
            f.Registry = new(fieldKindRuntime: kinds);
            var roles = RoleVocabulary.FromApi([new(Guid.Parse("11111111-1111-1111-1111-111111111111"), RoleReference.Domain("evidence-author"), "Evidence author", new(RoleOwnerKind.Package, "eam-core"), false)]);
            var records = new RecordTypeDefinitionStore(catalogue, compiler, new(kinds), f.Registry, window, roles: roles);
            f.type = new("tenant-engine", "eam", "Evidence", "1.0.0",
                [new FieldDefinition("proof/~", "Proof", new(kind, new(required, 0, null, [], null))) { RequiredCondition = evidence ? JsonSerializer.Deserialize<JsonElement>("{\"==\":[1,1]}") : null }, new("originator", "Untrusted request data")], new(1, 0), ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core", CreationGate: creationGate == RecordTypeCreationGate.Role ? RecordCreationGate.Role : RecordCreationGate.FormOnly, CreationRoleId: creationGate == RecordTypeCreationGate.Role ? "evidence-author" : null);
            await records.CreateDraftAsync(f.type, "type");
            var publication = await records.PublishAsync("tenant-engine", "eam.evidence", "1.0.0", 1, "publish");
            f.Rows = new(storage.Bookkeeping);
            f.operation = new(new AccessProvider(new AcceptedCommandFixtures.Decider()), records, f.Registry, f.Rows, f.Rows, TimeProvider.System, kinds, domains) { Attachments = new AttachmentFieldRuntime(f.Store) };
            f.Form = await Harness.CreateAsync(schemaRegistry: f.Registry, schemaJson: publication.Schema.JsonSchemaText,
                fieldBindings: f, fieldKinds: kinds, fieldDomains: domains, projectionSink: f, security: new PlaintextSecurity(),
                options: new() { AcceptedCommandWorkerId = "sys.forms-worker" }, clock: TimeProvider.System,
                submitGates: new AccessProviderFormSubmitGateAccess(new AccessProvider(new AcceptedCommandFixtures.Decider()), TimeProvider.System), recordTypePolicies: f,
                definitionFactory: (schema, tenant) => Harness.CreateDefinition(schema, tenant) with { RecordTypeId = creationGate is null ? null : "eam.evidence", SubmitGate = AcceptedCommandFixtures.InspectorGate,
                    Overlay = new(new Dictionary<string, FieldOverlay> { ["proof/~"] = new(InternationalizedText.FromInvariant("Proof")), ["originator"] = new(InternationalizedText.FromInvariant("Data")) }, [new("main", InternationalizedText.FromInvariant("Main"), ["proof/~", "originator"], new([], []))], []) });
            return f;
        }
        public ValueTask<Guid> Upload(string text, string actor = "alice") => Store.PutAsync(new("tenant-engine"), new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/plain", null, Evidence(actor));
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) => ValueTask.FromResult(creationGate);
        public ValueTask<FormSchemaFieldBindings?> ResolveAsync(TenantId tenant, string schemaRef, CancellationToken cancellationToken = default) => ValueTask.FromResult<FormSchemaFieldBindings?>(new(tenant, schemaRef, type.Fields.ToDictionary(field => field.FieldKey, field => field.Binding ?? new(new("text", "1.0.0", new Dictionary<string, string>()), new(false, 0, 1, [], null)))));
        public bool ExecutesAcceptedCommands => Execute;
        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default)
        {
            BodyRequests.Add(request.AcceptedCandidate.RootElement.GetRawText());
            return ValueTask.FromResult(new FormAcceptedRecordBodyDigest("records-write-intent/1", RecordWriteIntent.Digest(RecordWriteIntent.Encode("eam.evidence", null, JsonNode.Parse(request.AcceptedCandidate.RootElement.GetRawText())!.AsObject()))));
        }
        public async ValueTask<FormProjectionDeliveryResult> DeliverAcceptedAsync(FormAcceptedCommandDelivery delivery, CancellationToken cancellationToken = default)
        {
            var command = delivery.Command;
            var outcome = await operation.CompleteAcceptedAsync(new("tenant-engine", delivery.WorkerId), new("tenant-engine", command.CommandId, delivery.Envelope.ActorId, command.OperationId, "eam.evidence", JsonNode.Parse(delivery.Envelope.ProtectedAcceptedValues.Span)!.AsObject(), command.BodyDigestEncoding!, command.BodyDigest!, "inspection@1.0.0", command.AcceptanceAuditId!, command.AcceptanceDecidedAt!.Value, command.AcceptedAt, command.AcceptanceSnapshot), cancellationToken);
            Outcomes.Add(outcome);
            return new([]) { TerminalRefusal = outcome.Refusal?.Code };
        }
    }
    private sealed class ReadAuthority : IFieldDomainReadAuthority
    {
        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain, FieldDomainMember member, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
    private sealed class Keys : ITenantContentKeyProvider { public byte[] GetKey(TenantId tenant) => SHA256.HashData(Encoding.UTF8.GetBytes(tenant.Value)); }
    private sealed class PlaintextSecurity : IFormFieldSecurity
    {
        public ValueTask<FormProtectionResult> ProtectAsync(FormExecutionScope scope, FormDefinition definition, EntityId instanceId, JsonDocument acceptedCandidate, CancellationToken cancellationToken = default) => ValueTask.FromResult(new FormProtectionResult(JsonSerializer.SerializeToUtf8Bytes(acceptedCandidate.RootElement), new HashSet<string>()));
        public ValueTask<FormReadableCandidate> ReadAsync(FormExecutionScope scope, FormDefinition definition, FormSubmissionRecord submission, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class RowPort(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        public List<RecordWriteRow> Rows { get; } = [];
        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default) => ValueTask.FromResult<RecordWriteCommittedOperation?>(null);
        public async ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default) => new Transaction(this, await bookkeeping.BeginTransactionAsync(cancellationToken));
        private sealed class Transaction(RowPort owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>, IRecordContentTransaction
        {
            public IContentBookkeepingSession ContentSession => content;
            private RecordWriteRow row = null!;
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(RecordWriteRow value, CancellationToken cancellationToken = default) { row = value; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public async ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                await content.CommitAsync(cancellationToken);
                owner.Rows.Add(row);
                return new(RecordWriteCommitDisposition.Created, new(row.RecordId, row.OperationId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, row.RecordedAt));
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => content.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }
}
