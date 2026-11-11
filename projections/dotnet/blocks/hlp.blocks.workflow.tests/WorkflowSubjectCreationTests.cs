using System.Text.Json;
using Harborline.Blocks.Workflow.Durable;
using Xunit;
namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowSubjectCreationTests
{
    private sealed class Definitions(string? kind) : IWorkflowDefinitionExecutionStore
    {
        public ValueTask<WorkflowDefinitionRecord?> GetAdmittedCurrentPublishedAsync(string tenant, string key, CancellationToken ct = default)
            => throw new NotSupportedException();
        public ValueTask<WorkflowDefinitionRecord> GetAdmittedAsync(string tenant, string key, string version, CancellationToken ct = default)
        {
            Assert.Equal("tenant", tenant);
            Assert.Equal("definition", key);
            Assert.Equal("1.0.0", version);
            return ValueTask.FromResult(new WorkflowDefinitionRecord(tenant, key, version, WorkflowDefinitionStatus.Published,
                JsonSerializer.SerializeToElement(kind is null
                    ? new Dictionary<string, object> { ["initialState"] = "Start" }
                    : new Dictionary<string, object> { ["initialState"] = "Start", ["subject"] = new Dictionary<string, object> { [kind] = new { recordType = "invoice" } } })));
        }
    }

    [Theory]
    [InlineData("record", "invoice", true)]
    [InlineData("record", null, false)]
    [InlineData("record", "other", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "invoice", false)]
    [InlineData("set", null, true)]
    [InlineData("set", "invoice", false)]
    [Trait("Holds", "workflows-auth-11")]
    public async Task workflows_auth_11_creation_checks_the_declared_subject_and_persists_it(string? kind, string? type, bool accepted)
    {
        var path = Path.Combine(Path.GetTempPath(), "standing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var options = new FileJournalWorkflowStoreOptions { JournalPath = Path.Combine(path, "journal") };
            var instance = new WorkflowInstanceRecord
            {
                Id = "run", TenantId = "tenant", DefinitionKey = "definition", DefinitionVersion = "1.0.0", CurrentStep = "Start",
                SubjectRecord = type is null ? null : new(type, "record-42"),
            };
            using (var store = new FileJournalWorkflowStore(options, TimeProvider.System))
            {
                var creator = new WorkflowInstanceCreator(new Definitions(kind), store);
                if (accepted) await creator.CreateAsync(instance);
                else
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => creator.CreateAsync(instance));
                    Assert.Null(await store.LoadAsync("run"));
                }
            }
            using var reopened = new FileJournalWorkflowStore(options, TimeProvider.System);
            var loaded = await reopened.LoadAsync("run");
            if (accepted)
            {
                Assert.NotNull(loaded);
                Assert.Equal(type is null ? null : new WorkflowSubjectRecordReference("invoice", "record-42"), loaded.SubjectRecord);
                // Position changes cannot replace the immutable subject.
                await reopened.ParkAsync("run", "Next", "{}", 1);
                Assert.Equal(type is null ? null : new WorkflowSubjectRecordReference("invoice", "record-42"), (await reopened.LoadAsync("run"))!.SubjectRecord);
            }
            else Assert.Null(loaded);
        }
        finally { Directory.Delete(path, true); }
    }

    [Theory]
    [InlineData("", "id")]
    [InlineData(" ", "id")]
    [InlineData("type", "")]
    [InlineData("type", " ")]
    [Trait("Holds", "workflows-auth-11")]
    public void workflows_auth_11_subject_identity_parts_must_be_nonblank(string type, string id)
        => Assert.Throws<ArgumentException>(() => new WorkflowSubjectRecordReference(type, id));
}
