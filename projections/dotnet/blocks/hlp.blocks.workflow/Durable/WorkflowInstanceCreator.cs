namespace Harborline.Blocks.Workflow.Durable;

/// <summary>An immutable identity of one subject record in the instance tenant.</summary>
public sealed record WorkflowSubjectRecordReference
{
    /// <summary>Names one record; both parts must be nonblank.</summary>
    public WorkflowSubjectRecordReference(string recordType, string recordId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        RecordType = recordType;
        RecordId = recordId;
    }

    /// <summary>The subject's Record Type.</summary>
    public string RecordType { get; }
    /// <summary>The record id within that type and the instance tenant.</summary>
    public string RecordId { get; }
}

/// <summary>
/// Creates an authored workflow run against its admitted pinned definition. Hosts supply the subject here;
/// event-trigger-start supplies the committed event's record. The store is the persistence seam, not a
/// definition resolver. Set membership is owned by T-488/set-and-case-subjects.
/// </summary>
public sealed class WorkflowInstanceCreator(IWorkflowDefinitionExecutionStore definitions, IWorkflowStore store)
{
    /// <summary>Refuses a missing or mismatched record subject before writing any instance.</summary>
    public async Task CreateAsync(WorkflowInstanceRecord instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var admitted = await definitions.GetAdmittedAsync(
            instance.TenantId, instance.DefinitionKey, instance.DefinitionVersion, ct).ConfigureAwait(false);
        var definition = WorkflowDefinitionWireMapper.ToModel(
            admitted.Authored, instance.TenantId, instance.DefinitionKey, instance.DefinitionVersion);
        if (definition.Subject?.Kind == WorkflowSubjectKind.Record)
        {
            if (instance.SubjectRecord is null || instance.SubjectRecord.RecordType != definition.Subject.RecordType)
                throw new InvalidOperationException("A record-subject run requires a reference of its declared Record Type.");
        }
        else if (instance.SubjectRecord is not null)
            throw new InvalidOperationException("A run without a single record subject cannot carry a record reference.");
        await store.CreateInstanceAsync(instance, ct).ConfigureAwait(false);
    }
}
