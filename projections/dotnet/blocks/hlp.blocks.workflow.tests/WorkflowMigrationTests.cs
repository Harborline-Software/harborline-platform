using System.Text.Json;
using Harborline.Blocks.Workflow.Durable;
using Xunit;
using static Harborline.Blocks.Workflow.Tests.FileJournalWorkflowStoreTests;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowMigrationTests
{
    [Fact]
    public async Task Authorized_operator_migrates_named_run_with_explicit_map_and_records_history()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "tenant-1", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "old", Status = WorkflowStatus.Parked,
            StateJson = "{\"due\":\"2026-11-12T12:00:00Z\",\"correlation\":\"event-1\"}",
        });
        var migration = new WorkflowMigration(store, new Definitions());
        await migration.MigrateAsync("run-1", "2.0.0", new Dictionary<string, string> { ["old"] = "new" },
            new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true));

        var run = (await store.LoadAsync("run-1"))!;
        Assert.Equal("2.0.0", run.DefinitionVersion);
        Assert.Equal("new", run.CurrentStep);
        Assert.Equal(WorkflowStatus.Parked, run.Status);
        Assert.Equal("{\"due\":\"2026-11-12T12:00:00Z\",\"correlation\":\"event-1\"}", run.StateJson);
        var entry = Assert.Single(await store.EventsAsync("run-1"));
        Assert.Equal("Migrated", entry.EventType);
        using var data = JsonDocument.Parse(entry.DataJson);
        Assert.Equal("operator-1", data.RootElement.GetProperty("principal").GetString());
        Assert.Equal("1.0.0", data.RootElement.GetProperty("sourceVersion").GetString());
        Assert.Equal("2.0.0", data.RootElement.GetProperty("targetVersion").GetString());
        Assert.Equal("new", data.RootElement.GetProperty("map").GetProperty("old").GetString());
    }

    private sealed class Definitions : IWorkflowDefinitionExecutionStore
    {
        public ValueTask<WorkflowDefinitionRecord?> GetAdmittedCurrentPublishedAsync(string tenant, string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask<WorkflowDefinitionRecord> GetAdmittedAsync(string tenant, string key, string version, CancellationToken ct = default)
            => ValueTask.FromResult(new WorkflowDefinitionRecord(tenant, key, version, WorkflowDefinitionStatus.Published,
                JsonDocument.Parse("{\"initialState\":\"new\",\"states\":[{\"id\":\"new\"}]}").RootElement.Clone()));
    }
}
