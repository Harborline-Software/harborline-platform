using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;

if (args is not [var mode, var journalPath] || mode is not ("write-crash" or "write-partial-crash")) return 2;

var store = new FileJournalFormSubmissionStore(new() { JournalPath = journalPath });
var instant = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
var instance = new EntityId("harborline", "forms", "restart-probe");
var commit = new FormSubmissionCommit(
    "restart-key",
    new(instance, new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice",
        new FormDefinitionId("inspection"), new SemanticVersion(1, 0, 0), "restart-fingerprint", "{\"protected\":true}"u8.ToArray(), instant),
    new("audit-restart", instance, new TenantId("tenant-engine"), "alice", "{\"op\":\"submit\"}"u8.ToArray(), instant),
    new("outbox-restart", instance, new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "alice", new FormDefinitionId("inspection"), new SemanticVersion(1, 0, 0), "case-restart", "{\"accepted\":true}"u8.ToArray(), instant),
    new(instance, instant, FormProjectionStatus.Pending, []));
var result = await store.CommitAsync(commit);
if (result.Disposition != FormSubmissionCommitDisposition.Created) return 3;

Console.WriteLine("written");
Console.Out.Flush();
if (mode == "write-crash") Environment.Exit(0);

store.Dispose();
await using (var partial = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.None))
{
    await partial.WriteAsync(new byte[] { 0x48, 0x4c, 0x46 });
    partial.Flush(flushToDisk: true);
    Environment.Exit(0);
}
return 4;
