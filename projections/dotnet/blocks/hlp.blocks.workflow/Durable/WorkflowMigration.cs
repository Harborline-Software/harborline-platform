using System.Text.Json;

namespace Harborline.Blocks.Workflow.Durable;

/// <summary>A host authorization decision for migration of a named workflow instance.</summary>
public sealed record WorkflowMigrationAuthority(
    string Principal, string Tenant, string Operation, string RecordKind, string RecordId, bool Allowed);

/// <summary>Migrates an instance to an admitted version without recomputing its state.</summary>
public sealed class WorkflowMigration
{
    private readonly IWorkflowStore store;
    private readonly IWorkflowDefinitionExecutionStore definitions;
    private readonly WorkflowTriggerDispatcher dispatcher;

    internal WorkflowMigration(IWorkflowStore store, IWorkflowDefinitionExecutionStore definitions, WorkflowTriggerDispatcher dispatcher)
    {
        this.store = store;
        this.definitions = definitions;
        this.dispatcher = dispatcher;
    }

    /// <summary>Migrates a named instance using the host's authorization decision and an explicit step map.</summary>
    public async Task MigrateAsync(string instanceId, string targetVersion, IReadOnlyDictionary<string, string> map,
        WorkflowMigrationAuthority authority, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(map);
        if (!authority.Allowed || string.IsNullOrWhiteSpace(authority.Principal)
            || string.IsNullOrWhiteSpace(authority.Tenant) || authority.Operation != "workflow:migrate"
            || authority.RecordKind != "workflow-instance" || authority.RecordId != instanceId)
            throw new UnauthorizedAccessException("The migration authorization decision does not match this instance.");
        var instance = await store.LoadAsync(instanceId, ct).ConfigureAwait(false);
        if (instance is null || authority.Tenant != instance.TenantId)
            throw new UnauthorizedAccessException("The migration authorization decision does not match this instance.");
        if (instance.Status is not (WorkflowStatus.Running or WorkflowStatus.Parked))
            throw new InvalidOperationException("Only active workflow instances can be migrated.");
        if (targetVersion == instance.DefinitionVersion)
            throw new InvalidOperationException("Migration requires another definition version.");
        map = new Dictionary<string, string>(map, StringComparer.Ordinal);
        if (!map.TryGetValue(instance.CurrentStep, out var targetStep) || string.IsNullOrWhiteSpace(targetStep))
            throw new InvalidOperationException($"No mapping for active step '{instance.CurrentStep}'.");
        if (dispatcher.HasTypedHandler(instance.DefinitionKey) && targetStep != instance.CurrentStep)
            throw new InvalidOperationException($"Typed handler cannot migrate active step '{instance.CurrentStep}' to '{targetStep}'.");
        var definition = await definitions.GetAdmittedAsync(instance.TenantId, instance.DefinitionKey, targetVersion, ct)
            .ConfigureAwait(false);
        var model = WorkflowDefinitionWireMapper.ToModel(definition.Authored, instance.TenantId, instance.DefinitionKey, targetVersion);
        WorkflowInstanceCreator.ValidateSubject(model, instance);
        if (!model.States.Any(state => state.Id == targetStep))
            throw new InvalidOperationException($"Target for active step '{instance.CurrentStep}' does not exist.");
        var data = JsonSerializer.Serialize(new { principal = authority.Principal, sourceVersion = instance.DefinitionVersion, targetVersion, map });
        var migrationStore = store as IWorkflowMigrationStore
            ?? throw new NotSupportedException("The workflow store does not support fenced migration.");
        await migrationStore.MigrateAsync(instance, targetVersion, targetStep, data, ct).ConfigureAwait(false);
    }
}
