using Harborline.Blocks.Workflow.Durable;
using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowLifecycleHistoryTests
{
    [Fact]
    public async Task Lifecycle_history_is_ordered_and_survives_reopening_the_journal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hl488le2-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var options = new FileJournalWorkflowStoreOptions { JournalPath = Path.Combine(directory, "journal") };
        try
        {
            using (var journal = new FileJournalWorkflowStore(options, TimeProvider.System))
            {
                IWorkflowStore store = journal;
                await store.CreateInstanceAsync(new WorkflowInstanceRecord
                {
                    Id = "run", TenantId = "tenant", DefinitionKey = "flow", DefinitionVersion = "1",
                    CurrentStep = "step", Status = WorkflowStatus.Running,
                });
                await store.AdvanceAsync(new WorkflowStepKey("run", 0, "step"), null, "{}", "Advanced", "{\"principal\":\"user\"}", "next", WorkflowStatus.Running);
                await store.ParkAsync("run", "next", "{}");
            }
            using var reopenedJournal = new FileJournalWorkflowStore(options, TimeProvider.System);
            IWorkflowStore reopened = reopenedJournal;
            var events = await reopened.EventsAsync("run");
            Assert.Equal(new long[] { 1, 2 }, events.Select(e => e.Seq));
            Assert.Equal(new[] { "Advanced", "Parked" }, events.Select(e => e.EventType));
            Assert.Equal("{\"principal\":\"user\"}", events[0].DataJson);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
