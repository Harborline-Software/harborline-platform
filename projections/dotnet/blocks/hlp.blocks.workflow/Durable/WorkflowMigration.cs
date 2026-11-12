using System.Text.Json;

namespace Harborline.Blocks.Workflow.Durable;

/// <summary>A host authorization decision for migration of a named workflow instance.</summary>
public sealed record WorkflowMigrationAuthority(
    string Principal, string Tenant, string Operation, string RecordKind, string RecordId, bool Allowed);

/// <summary>Migrates an instance to an admitted version without recomputing its state.</summary>
public sealed class WorkflowMigration(IWorkflowStore store, IWorkflowDefinitionExecutionStore definitions)
{
    /// <summary>Migrates a named instance using the host's authorization decision and an explicit step map.</summary>
    public async Task MigrateAsync(string instanceId, string targetVersion, IReadOnlyDictionary<string, string> map,
        WorkflowMigrationAuthority authority, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(map);
        var instance = await store.LoadAsync(instanceId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No workflow instance '{instanceId}' exists.");
        if (!authority.Allowed || string.IsNullOrWhiteSpace(authority.Principal)
            || authority.Tenant != instance.TenantId || authority.Operation != "workflow:migrate"
            || authority.RecordKind != "workflow-instance" || authority.RecordId != instance.Id)
            throw new UnauthorizedAccessException("The migration authorization decision does not match this instance.");
        if (instance.Status is not (WorkflowStatus.Running or WorkflowStatus.Parked))
            throw new InvalidOperationException("Only active workflow instances can be migrated.");
        if (!map.TryGetValue(instance.CurrentStep, out var targetStep) || string.IsNullOrWhiteSpace(targetStep))
            throw new InvalidOperationException($"No mapping for active step '{instance.CurrentStep}'.");
        var definition = await definitions.GetAdmittedAsync(instance.TenantId, instance.DefinitionKey, targetVersion, ct)
            .ConfigureAwait(false);
        var model = WorkflowDefinitionWireMapper.ToModel(definition.Authored, instance.TenantId, instance.DefinitionKey, targetVersion);
        if (!model.States.Any(state => state.Id == targetStep))
            throw new InvalidOperationException($"Target for active step '{instance.CurrentStep}' does not exist.");
        var data = JsonSerializer.Serialize(new { principal = authority.Principal, sourceVersion = instance.DefinitionVersion, targetVersion, map });
        await store.MigrateAsync(instance, targetVersion, targetStep, data, ct).ConfigureAwait(false);
    }
}
