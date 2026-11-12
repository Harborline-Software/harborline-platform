using System.Text.Json;
using Harborline.Blocks.Workflow.Durable;
using Xunit;
using static Harborline.Blocks.Workflow.Tests.FileJournalWorkflowStoreTests;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowMigrationTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("principal")]
    [InlineData("tenant")]
    [InlineData("operation")]
    [InlineData("kind")]
    [InlineData("run")]
    public async Task Missing_denied_or_mismatched_host_decision_moves_and_records_nothing(string invalid)
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "tenant-1", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "old", Status = WorkflowStatus.Running,
        });
        var authority = new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true);
        authority = invalid switch
        {
            "missing" => null!,
            "denied" => authority with { Allowed = false },
            "principal" => authority with { Principal = "" },
            "tenant" => authority with { Tenant = "other" },
            "operation" => authority with { Operation = "other" },
            "kind" => authority with { RecordKind = "other" },
            _ => authority with { RecordId = "other" },
        };
        var error = await Record.ExceptionAsync(() => new WorkflowMigration(store, new Definitions()).MigrateAsync(
            "run-1", "2.0.0", new Dictionary<string, string> { ["old"] = "new" }, authority));
        Assert.True(error is UnauthorizedAccessException or ArgumentNullException);
        Assert.Equal("1.0.0", (await store.LoadAsync("run-1"))!.DefinitionVersion);
        Assert.Equal("old", (await store.LoadAsync("run-1"))!.CurrentStep);
        Assert.Empty(await store.EventsAsync("run-1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_mapping_or_missing_target_refuses_without_moving_and_names_source_step(bool missingTarget)
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "tenant-1", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "old", Status = WorkflowStatus.Parked, StateJson = "{\"wait\":true}",
        });
        var map = new Dictionary<string, string>();
        if (missingTarget) map.Add("old", "missing");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowMigration(store, new Definitions())
            .MigrateAsync("run-1", "2.0.0", map,
                new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true)));
        Assert.Contains("old", error.Message, StringComparison.Ordinal);
        var run = (await store.LoadAsync("run-1"))!;
        Assert.Equal("1.0.0", run.DefinitionVersion);
        Assert.Equal("old", run.CurrentStep);
        Assert.Equal("{\"wait\":true}", run.StateJson);
        Assert.Empty(await store.EventsAsync("run-1"));
    }

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
