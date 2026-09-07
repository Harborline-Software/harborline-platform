namespace Harborline.Blocks.EntityViews;

/// <summary>A view-owned projection of the frozen forms definition wire.</summary>
/// <remarks><see cref="FormsFormDefinition.Id"/> and <see cref="FormsFormDefinition.Version"/> are the authoritative scalar shapes.</remarks>
public sealed record BoundFormDescriptor(string Definition, string Version, string DisplayLabel);

public sealed record SubmittedInstanceDescriptor(string InstanceId, string FormId, string SubmittedAt, string? AssessorRef);

public interface IFormBindingSource
{
    ValueTask<IReadOnlyList<BoundFormDescriptor>> GetBoundFormsAsync(string recordType);
    ValueTask<IReadOnlyList<SubmittedInstanceDescriptor>> GetSubmittedInstancesAsync(string entityId);
}
