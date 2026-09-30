namespace Harborline.Blocks.EntityViews;

/// <summary>A view-owned projection of the frozen forms definition wire.</summary>
/// <remarks><see cref="FormsFormDefinition.Id"/> and <see cref="FormsFormDefinition.Version"/> are the authoritative scalar shapes.</remarks>
public sealed record BoundFormDescriptor(string Definition, string Version, string DisplayLabel);

/// <summary>One submitted form instance as the views see it: instance id, form id, submission time and optional assessor.</summary>
public sealed record SubmittedInstanceDescriptor(string InstanceId, string FormId, string SubmittedAt, string? AssessorRef);

/// <summary>Source of the forms bound to a record type and of the instances already submitted for an entity.</summary>
public interface IFormBindingSource
{
    /// <summary>Returns the forms bound to the record type.</summary>
    ValueTask<IReadOnlyList<BoundFormDescriptor>> GetBoundFormsAsync(string recordType);
    /// <summary>Returns the form instances submitted for the entity.</summary>
    ValueTask<IReadOnlyList<SubmittedInstanceDescriptor>> GetSubmittedInstancesAsync(string entityId);
}
