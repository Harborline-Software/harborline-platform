using System.Globalization;

namespace Harborline.Blocks.EntityViews;

public interface ISubmissionLinkingService
{
    ValueTask<SubmissionSummary?> LinkFromCase(
        string? caseRef, string formId, string instanceId, DateTimeOffset submittedAt, string? assessorRef);
}

/// <summary>Resolves an Into-Case-Ref through an injected case port and links the submission.</summary>
public sealed class SubmissionLinking(
    IEntityReadStore entities,
    IEntityViewsArtifactWriter writer,
    Func<string, ValueTask<string?>> resolveCaseTarget) : ISubmissionLinkingService
{
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
