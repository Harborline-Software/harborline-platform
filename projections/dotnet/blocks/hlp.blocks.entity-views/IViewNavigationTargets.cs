namespace Harborline.Blocks.EntityViews;

public sealed record FillFormNavigationTarget(string Uri, string RecordName);

public interface IViewNavigationTargets
{
    FillFormNavigationTarget FillForm(string definition, string entityId, string recordName);
    string ViewSubmission(string formId, string instanceId);
}

public sealed class ViewNavigationTargets : IViewNavigationTargets
{
    public FillFormNavigationTarget FillForm(string definition, string entityId, string recordName) =>
        new($"/forms?form={Uri.EscapeDataString(definition)}&into={Uri.EscapeDataString(entityId)}", recordName);

    public string ViewSubmission(string formId, string instanceId) =>
        $"/forms?form={Uri.EscapeDataString(formId)}&instance={Uri.EscapeDataString(instanceId)}";
}
