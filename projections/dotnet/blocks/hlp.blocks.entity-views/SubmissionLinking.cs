using System.Globalization;

namespace Harborline.Blocks.EntityViews;

/// <summary>Port that links a submitted form instance to the entity a case points at.</summary>
public interface ISubmissionLinkingService
{
    /// <summary>Links a submission to the case's target entity and returns the summary; returns null when there is nothing to link.</summary>
    ValueTask<SubmissionSummary?> LinkFromCase(
        string? caseRef, string formId, string instanceId, DateTimeOffset submittedAt, string? assessorRef);
}

/// <summary>Resolves an Into-Case-Ref through an injected case port and links the submission.</summary>
public sealed class SubmissionLinking(
    IEntityReadStore entities,
    IEntityViewsArtifactWriter writer,
    Func<string, ValueTask<string?>> resolveCaseTarget) : ISubmissionLinkingService
{
    /// <summary>Returns null, writing nothing, when the case reference is blank, the case port resolves no target, or the target entity does not exist; otherwise records and returns a <see cref="SubmissionSummary"/> with the submission time in ISO-8601.</summary>
    public async ValueTask<SubmissionSummary?> LinkFromCase(
        string? caseRef, string formId, string instanceId, DateTimeOffset submittedAt, string? assessorRef)
    {
        if (string.IsNullOrWhiteSpace(caseRef)) return null;
        var target = await resolveCaseTarget(caseRef).ConfigureAwait(false);
        if (target is null || await entities.GetEntityAsync(target).ConfigureAwait(false) is null) return null;
        var submission = new SubmissionSummary(
            instanceId, formId, submittedAt.ToString("O", CultureInfo.InvariantCulture), assessorRef);
        await writer.AddSubmissionAsync(target, submission).ConfigureAwait(false);
        return submission;
    }
}
