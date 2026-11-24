using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class ReactionFormsAuditTests
{
    // Oracle: T-616/reaction-write-identities acceptance 43; literal lifecycle refusal and unchanged pending state.
    [Fact]
    public async Task T616_forms_refuses_a_delegated_actor_on_lifecycle_audit()
    {
        var instance = new EntityId("harborline", "forms", "instance-1");
        var tenant = new TenantId("tenant-a");
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var form = new FormDefinitionId("inspection");
        var version = new SemanticVersion(1, 0, 0);
        var schema = new SchemaId("sha256:accepted");
        var snapshot = "acceptance-decision"u8.ToArray();
        var command = new FormAcceptedRecordCommand("outbox-1", "operation-1", "records.inspection", "fingerprint", now, snapshot)
        {
            AcceptanceAuditId = "audit-1", AcceptanceDecidedAt = now,
            BodyDigestEncoding = "records-write-intent/1", BodyDigest = "digest-1",
        };
        var commit = new FormSubmissionCommit("op-1",
            new FormSubmissionRecord(instance, tenant, Guid.Empty, "alice", form, version, "fingerprint", "{}"u8.ToArray(), now, schema, "records.inspection"),
            new FormMutationAuditEnvelope("audit-1", instance, tenant, "alice", "{}"u8.ToArray(), now) { AuthoritySnapshot = snapshot },
            new FormProjectionEnvelope("outbox-1", instance, tenant, Guid.Empty, "alice", form, version, null, "{}"u8.ToArray(), now,
                SchemaRef: schema, RecordTypeId: "records.inspection") { AcceptedCommand = command },
            new FormSubmitReceipt(instance, now, FormProjectionStatus.Pending, []));
        var store = new InMemoryFormSubmissionStore();
        Assert.Equal(FormSubmissionCommitDisposition.Created, (await store.CommitAsync(commit)).Disposition);
        var audit = new KernelAuditEvidence("cancel-1", "tenant-admin", now, "{}"u8.ToArray())
        {
            AuthoritySnapshot = "cancel-decision"u8.ToArray(), DelegatedActor = new("wf.follow-up", "1.0.0"),
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => store.CancelAcceptedCommandAsync(tenant, "outbox-1", audit).AsTask());

        Assert.Equal("audit", error.ParamName);
        Assert.StartsWith("A Forms lifecycle audit names its actor only.", error.Message, StringComparison.Ordinal);
        var pending = Assert.IsType<FormAcceptedRecordCommand>(await store.GetAcceptedCommandAsync(tenant, "outbox-1"));
        Assert.Equal(FormAcceptedCommandState.Pending, pending.State);
        Assert.Null(pending.StateActorId);
        Assert.Null(pending.StateChangedAt);
    }

    [Fact]
    public async Task T616_forms_refuses_a_delegated_actor_on_submission_audit()
    {
        var instance = new EntityId("harborline", "forms", "instance-1");
        var tenant = new TenantId("tenant-a");
        var now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        var submission = new FormSubmissionRecord(instance, tenant, Guid.Empty, "alice", new FormDefinitionId("inspection"),
            new SemanticVersion(1, 0, 0), "fingerprint", ReadOnlyMemory<byte>.Empty, now);
        var audit = new FormMutationAuditEnvelope("audit-1", instance, tenant, "alice", ReadOnlyMemory<byte>.Empty, now);
        var commit = new FormSubmissionCommit("op-1", submission, audit, null!, null!);
        var port = new FormSubmissionKernelTransactionPort(new InMemoryFormSubmissionStore());
        await using var transaction = await port.BeginAsync();
        await transaction.StageOperationAsync(new(instance.ToString(), "op-1", "fingerprint"));
        await transaction.StageRecordAsync(commit);
        var evidence = new KernelAuditEvidence("audit-1", "alice", now, ReadOnlyMemory<byte>.Empty)
            { DelegatedActor = new("wf.follow-up", "1.0.0") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.StageAuditAsync(evidence).AsTask());
    }
}
