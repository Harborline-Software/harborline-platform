using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Kernel.Core;
using Harborline.Foundation.Forms.Models;

// write-crash / write-partial-crash: an ordinary submission (journal format 2).
// accept-crash: a durably accepted Records command (ADR-0110, plan W6; journal format 3) for the given schema reference, then
// process loss before any execution. claim-crash: the same, then the worker's durable claim, then process loss before the
// completion is recorded.
var accepted = args is [var acceptMode, _, _] && acceptMode is "accept-crash" or "claim-crash";
if (!accepted && (args is not [var mode, _] || mode is not ("write-crash" or "write-partial-crash"))) return 2;
var journalPath = args[1];

var store = new FileJournalFormSubmissionStore(new() { JournalPath = journalPath });
var instant = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
if (accepted)
{
    var tenant = new TenantId("tenant-engine");
    var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var acceptedInstance = new EntityId("harborline", "forms", "restart-accepted");
    var schema = new SchemaId(args[2]);
    var values = "{\"name\":\"Ada\"}"u8.ToArray();
    // The submit gate's allowed decision for alice, in the shared authority-snapshot grammar, written as literal bytes.
    var snapshot = "{\"tenant\":\"tenant-engine\",\"principal\":\"alice\",\"version\":2,\"steps\":[{\"ordinal\":1,\"stage\":\"act\",\"facts\":[\"kind:Gate\",\"act:forms:submit-gate\",\"target:form/inspection@1.0.0\",\"principal:alice\",\"tenant:tenant-engine\",\"at:2026-08-08T12:00:00.0000000+00:00\"]},{\"ordinal\":2,\"stage\":\"effective-roles\",\"facts\":[\"roles:none\",\"deciding:grant:probe@1\"]},{\"ordinal\":3,\"stage\":\"standings\",\"facts\":[\"standings:none\"]},{\"ordinal\":4,\"stage\":\"verdict\",\"facts\":[\"verdict:allowed\",\"refusal:None\",\"version:2\"]}]}"u8.ToArray();
    var acceptedCommit = new FormSubmissionCommit(
        "restart-accepted-key",
        new(acceptedInstance, tenant, party, "alice", new FormDefinitionId("inspection"), new SemanticVersion(1, 0, 0), "restart-accepted-fingerprint",
            values, instant, schema, "notes.entry"),
        new("audit-restart-accepted", acceptedInstance, tenant, "alice", "{\"op\":\"submit\"}"u8.ToArray(), instant) { AuthoritySnapshot = snapshot },
        new("outbox-restart-accepted", acceptedInstance, tenant, party, "alice", new FormDefinitionId("inspection"), new SemanticVersion(1, 0, 0),
            null, values, instant, SchemaRef: schema, RecordTypeId: "notes.entry")
        {
            AcceptedCommand = new FormAcceptedRecordCommand("outbox-restart-accepted", acceptedInstance.ToString(), "notes.entry",
                "restart-accepted-fingerprint", instant, snapshot)
            {
                AcceptanceAuditId = "audit-restart-accepted",
                AcceptanceDecidedAt = instant,
                BodyDigestEncoding = "records-write-intent/1",
                BodyDigest = "restart-accepted-body-digest",
            },
        },
        new(acceptedInstance, instant, FormProjectionStatus.Pending, []));
    var acceptance = await KernelTransactionBoundary.ExecuteAsync(
        [new KernelCommand<FormSubmissionCommit>(
            new(acceptedInstance.ToString(), acceptedCommit.IdempotencyKey, acceptedCommit.Submission.RequestFingerprint),
            acceptedCommit,
            new(acceptedCommit.Audit.AuditId, acceptedCommit.Audit.ActorId, acceptedCommit.Audit.RecordedAt, acceptedCommit.Audit.Payload)
            {
                AuthoritySnapshot = snapshot,
            })],
        new FormSubmissionKernelTransactionPort(store));
    if (acceptance.Value?.Disposition != FormSubmissionCommitDisposition.Created) return 3;
    if (args[0] == "claim-crash"
        && (await store.ClaimAcceptedCommandAsync("outbox-restart-accepted", "sys.forms-worker", instant))?.Changed != true)
        return 5;
    Console.WriteLine("written");
    Console.Out.Flush();
    Environment.Exit(0);
}

var instance = new EntityId("harborline", "forms", "restart-probe");
var commit = new FormSubmissionCommit(
    "restart-key",
    new(instance, new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice",
        new FormDefinitionId("inspection"), new SemanticVersion(1, 0, 0), "restart-fingerprint", "{\"protected\":true}"u8.ToArray(), instant),
    new("audit-restart", instance, new TenantId("tenant-engine"), "alice", "{\"op\":\"submit\"}"u8.ToArray(), instant),
    new("outbox-restart", instance, new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "alice", new FormDefinitionId("inspection"), new SemanticVersion(1, 0, 0), "case-restart", "{\"accepted\":true}"u8.ToArray(), instant),
    new(instance, instant, FormProjectionStatus.Pending, []));
var command = new KernelCommand<FormSubmissionCommit>(
    new(instance.ToString(), commit.IdempotencyKey, commit.Submission.RequestFingerprint),
    commit,
    new(commit.Audit.AuditId, commit.Audit.ActorId, commit.Audit.RecordedAt, commit.Audit.Payload));
var transaction = await KernelTransactionBoundary.ExecuteAsync(
    [command],
    new FormSubmissionKernelTransactionPort(store));
var result = transaction.Value
    ?? throw new InvalidOperationException(transaction.Refusal?.Code ?? "The kernel transaction did not return a result.");
if (result.Disposition != FormSubmissionCommitDisposition.Created) return 3;

Console.WriteLine("written");
Console.Out.Flush();
if (args[0] == "write-crash") Environment.Exit(0);

store.Dispose();
await using (var partial = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.None))
{
    await partial.WriteAsync(new byte[] { 0x48, 0x4c, 0x46 });
    partial.Flush(flushToDisk: true);
    Environment.Exit(0);
}
return 4;
