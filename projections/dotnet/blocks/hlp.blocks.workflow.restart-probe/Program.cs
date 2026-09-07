// The workflow durable-store restart probe: a deliberate crasher run as a CHILD PROCESS by
// FileJournalWorkflowStoreTests, so restart durability is proven across a genuine OS process
// boundary (the Forms restart-probe precedent) — never by same-object dispose/reopen.
//
// Modes:
//   write-crash <journalPath>          — create an instance, commit one effecting advance and one
//                                        park (bumped iteration), print "written", then
//                                        Environment.Exit(0) WITHOUT disposing the store: the
//                                        frames must be durable by virtue of the write path alone.
//   write-partial-crash <journalPath>  — the same, then append a 3-byte torn tail so the parent
//                                        proves an authenticated incomplete final frame is
//                                        truncated without losing committed rows.

using Harborline.Blocks.Workflow.Durable;

if (args.Length != 2 || (args[0] != "write-crash" && args[0] != "write-partial-crash"))
{
    Console.Error.WriteLine("usage: restart-probe (write-crash|write-partial-crash) <journalPath>");
    return 2;
}

var mode = args[0];
var journalPath = args[1];

var store = new FileJournalWorkflowStore(new FileJournalWorkflowStoreOptions { JournalPath = journalPath });
await store.CreateInstanceAsync(new WorkflowInstanceRecord
{
    Id = "restart-1",
    TenantId = "tenant:probe",
    DefinitionKey = "invoice-approval",
    DefinitionVersion = "2026-06-23.1",
    CurrentStep = "decide",
    Status = WorkflowStatus.Running,
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow,
});

// One effecting advance — the effect payload must survive the crash exactly once.
await store.AdvanceAsync(
    key: new WorkflowStepKey("restart-1", 0, "post"),
    effect: new WorkflowEffect((unitOfWork, _) =>
    {
        ((FileJournalWorkflowUnitOfWork)unitOfWork).StageEffectPayload("restart-effect");
        return Task.CompletedTask;
    }),
    resultJson: "{\"posted\":true}",
    eventType: "Advanced",
    eventDataJson: "{\"posted\":true}",
    nextStep: "approve",
    nextStatus: WorkflowStatus.Running);

// One park at a BUMPED iteration — the durable loop-back counter must be read back verbatim.
await store.ParkAsync("restart-1", "approve", "{\"kind\":\"cp-approval-basis\"}", iteration: 1);

Console.WriteLine("written");

if (mode == "write-crash")
{
    Environment.Exit(0); // no Dispose — durability must come from the write path, not shutdown flushing.
}

store.Dispose();
await using (var partial = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.None))
{
    await partial.WriteAsync(new byte[] { 0x48, 0x4c, 0x57 });
    partial.Flush(flushToDisk: true);
}
Environment.Exit(0);
return 0;
