using System.Globalization;

namespace Harborline.Blocks.EntityViews;

/// <summary>Port that records a condition assessment from an accepted form submission.</summary>
public interface IConditionCaptureService
{
    /// <summary>Records a graded condition assessment for an entity from a submitted form field.</summary>
    ValueTask<ConditionCaptureResult> CaptureFromSubmission(
        string entityId, string formId, string fieldId, int grade, int? scaleMax,
        DateTimeOffset observedAt, string? assessorRef, string? observations);
}

/// <summary>Projects one accepted submission act into a condition-history artifact.</summary>
public sealed class ConditionCapture(IEntityReadStore entities, IEntityViewsArtifactWriter writer) : IConditionCaptureService
{
    private int nextAssessment;

    /// <summary>Validates the grade against the scale (the supplied maximum, else <see cref="EntityViewsModel.DefaultConditionScaleMax"/>) and returns status "skipped" with refusal code grade-out-of-range when it falls outside 1..scale; throws <see cref="EntityViewsException"/> with <see cref="EntityViewsCodes.UnknownEntity"/> for an unknown entity, otherwise writes the assessment (score = grade/scale) and returns it.</summary>
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
