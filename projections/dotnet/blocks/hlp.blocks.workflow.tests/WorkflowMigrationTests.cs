using System.Text.Json;
using Harborline.Blocks.Workflow.Durable;
using Xunit;
using static Harborline.Blocks.Workflow.Tests.FileJournalWorkflowStoreTests;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowMigrationTests
{
    [Fact]
    public async Task Absent_and_cross_tenant_runs_have_the_same_migration_refusal()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "other", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "old", Status = WorkflowStatus.Running,
        });
        var migration = new WorkflowMigration(store, new Definitions());
        var authority = new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true);
        var existing = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => migration.MigrateAsync("run-1", "2.0.0", new Dictionary<string, string>(), authority));
        var absent = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => migration.MigrateAsync("missing", "2.0.0", new Dictionary<string, string>(), authority with { RecordId = "missing" }));
        Assert.Equal(existing.Message, absent.Message);
        Assert.Empty(await store.EventsAsync("run-1"));
    }

    [Fact]
    public async Task Migration_refuses_an_already_advanced_target_without_moving_or_recording()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "tenant-1", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "new", Status = WorkflowStatus.Running,
        });
        await store.AdvanceAsync(new WorkflowStepKey("run-1", 0, "new"), null, "{}", "Advanced", "{}", "old", WorkflowStatus.Running);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowMigration(store, new Definitions())
            .MigrateAsync("run-1", "2.0.0", new Dictionary<string, string> { ["old"] = "new" },
                new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true)));
        Assert.Contains("new", error.Message, StringComparison.Ordinal);
        var run = (await store.LoadAsync("run-1"))!;
        Assert.Equal("old", run.CurrentStep);
        Assert.Equal("1.0.0", run.DefinitionVersion);
        Assert.Equal(0, run.Iteration);
        Assert.Equal("Advanced", Assert.Single(await store.EventsAsync("run-1")).EventType);
    }

    [Fact]
    public async Task Publishing_a_version_never_moves_a_run_but_an_authorized_explicit_migration_repins_it()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        var definitions = new InMemoryWorkflowDefinitionStore();
        var authored = JsonElement.Parse("{\"initialState\":\"new\",\"states\":[{\"id\":\"new\",\"kind\":\"Terminal\"}]}");
        await definitions.RegisterAsync(new WorkflowDefinition
        {
            Tenant = "tenant-1", Key = "flow", Version = "2.0.0", InitialState = "new",
            States = [new WorkflowStateDef { Id = "new", Kind = WorkflowStateKind.Terminal }],
        }, authored);
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "tenant-1", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "old", Status = WorkflowStatus.Parked,
        });
        await definitions.PublishAsync("tenant-1", "flow", "2.0.0");
        Assert.Equal("1.0.0", (await store.LoadAsync("run-1"))!.DefinitionVersion);
        Assert.Equal("old", (await store.LoadAsync("run-1"))!.CurrentStep);
        Assert.Empty(await store.EventsAsync("run-1"));
        await new WorkflowMigration(store, definitions).MigrateAsync("run-1", "2.0.0",
            new Dictionary<string, string> { ["old"] = "new" },
            new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true));
        Assert.Equal("2.0.0", (await store.LoadAsync("run-1"))!.DefinitionVersion);
        Assert.Equal("new", (await store.LoadAsync("run-1"))!.CurrentStep);
    }

    [Fact]
    public async Task Migration_refuses_if_the_instance_changes_while_the_target_is_loaded_even_at_the_same_clock_instant()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open(new FixedClock());
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "tenant-1", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "old", Status = WorkflowStatus.Parked, UpdatedAt = DateTimeOffset.Parse("2026-11-12T12:00:00Z"),
        });
        var definitions = new PausedDefinitions();
        var migrating = new WorkflowMigration(store, definitions).MigrateAsync("run-1", "2.0.0",
            new Dictionary<string, string> { ["old"] = "new" },
            new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true));
        await definitions.Entered.Task;
        await store.ParkAsync("run-1", "old", "{}");
        definitions.Continue.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => migrating);
        Assert.Equal("1.0.0", (await store.LoadAsync("run-1"))!.DefinitionVersion);
        Assert.Equal("old", (await store.LoadAsync("run-1"))!.CurrentStep);
        Assert.Equal("Parked", Assert.Single(await store.EventsAsync("run-1")).EventType);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-11-12T12:00:00Z");
    }

    private sealed class PausedDefinitions : IWorkflowDefinitionExecutionStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<WorkflowDefinitionRecord?> GetAdmittedCurrentPublishedAsync(string tenant, string key, CancellationToken ct = default)
            => throw new NotSupportedException();
        public async ValueTask<WorkflowDefinitionRecord> GetAdmittedAsync(string tenant, string key, string version, CancellationToken ct = default)
        {
            Entered.SetResult();
            await Continue.Task;
            return await new Definitions().GetAdmittedAsync(tenant, key, version, ct);
        }
    }

    [Fact]
    public async Task Migration_requires_another_version_without_moving_or_recording()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = "run-1", TenantId = "tenant-1", DefinitionKey = "flow", DefinitionVersion = "1.0.0",
            CurrentStep = "old", Status = WorkflowStatus.Running,
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowMigration(store, new Definitions()).MigrateAsync(
            "run-1", "1.0.0", new Dictionary<string, string> { ["old"] = "new" },
            new WorkflowMigrationAuthority("operator-1", "tenant-1", "workflow:migrate", "workflow-instance", "run-1", true)));
        Assert.Equal("1.0.0", (await store.LoadAsync("run-1"))!.DefinitionVersion);
        Assert.Equal("old", (await store.LoadAsync("run-1"))!.CurrentStep);
        Assert.Empty(await store.EventsAsync("run-1"));
    }

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
