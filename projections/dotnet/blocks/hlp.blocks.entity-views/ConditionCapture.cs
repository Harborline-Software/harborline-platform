using System.Globalization;

namespace Harborline.Blocks.EntityViews;

public interface IConditionCaptureService
{
    ValueTask<ConditionCaptureResult> CaptureFromSubmission(
        string entityId, string formId, string fieldId, int grade, int? scaleMax,
        DateTimeOffset observedAt, string? assessorRef, string? observations);
}

/// <summary>Projects one accepted submission act into a condition-history artifact.</summary>
public sealed class ConditionCapture(IEntityReadStore entities, IEntityViewsArtifactWriter writer) : IConditionCaptureService
{
    private int nextAssessment;

    public async ValueTask<ConditionCaptureResult> CaptureFromSubmission(
        string entityId, string formId, string fieldId, int grade, int? scaleMax,
        DateTimeOffset observedAt, string? assessorRef, string? observations)
    {
        var effectiveScale = scaleMax ?? EntityViewsModel.DefaultConditionScaleMax;
        if (grade < 1 || grade > effectiveScale)
            return new(null, "skipped", [new("grade-out-of-range", fieldId)]);

        if (await entities.GetEntityAsync(entityId).ConfigureAwait(false) is null)
            throw new EntityViewsException(EntityViewsCodes.UnknownEntity, "The condition target is unknown.");

        var assessment = new ConditionAssessment(
            $"condition:generated/{++nextAssessment}", entityId, grade, effectiveScale, null,
            (double)grade / effectiveScale,
            observedAt.ToString("O", CultureInfo.InvariantCulture), assessorRef, formId, fieldId, observations);
        await writer.AddConditionAsync(assessment).ConfigureAwait(false);
        return new(assessment, null, null);
    }
}
