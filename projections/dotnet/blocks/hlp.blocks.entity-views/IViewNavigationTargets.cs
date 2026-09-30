namespace Harborline.Blocks.EntityViews;

/// <summary>Navigation target for filling a form: the destination URI and the record name to display.</summary>
public sealed record FillFormNavigationTarget(string Uri, string RecordName);

/// <summary>Builds the forms-app navigation targets that views link to.</summary>
public interface IViewNavigationTargets
{
    /// <summary>Builds the target that opens a form definition to fill into an entity.</summary>
    FillFormNavigationTarget FillForm(string definition, string entityId, string recordName);
    /// <summary>Builds the URI that opens an already submitted form instance.</summary>
    string ViewSubmission(string formId, string instanceId);
}

/// <summary>Builds the forms-app URIs, escaping every segment, under the <c>/forms</c> path.</summary>
public sealed class ViewNavigationTargets : IViewNavigationTargets
{
    /// <summary>Returns <c>/forms?form=&lt;definition&gt;&amp;into=&lt;entityId&gt;</c> with both values URL-escaped, paired with the record name.</summary>
    public FillFormNavigationTarget FillForm(string definition, string entityId, string recordName) =>
        new($"/forms?form={Uri.EscapeDataString(definition)}&into={Uri.EscapeDataString(entityId)}", recordName);

    /// <summary>Returns <c>/forms?form=&lt;formId&gt;&amp;instance=&lt;instanceId&gt;</c> with both values URL-escaped.</summary>
    public string ViewSubmission(string formId, string instanceId) =>
        $"/forms?form={Uri.EscapeDataString(formId)}&instance={Uri.EscapeDataString(instanceId)}";
}
