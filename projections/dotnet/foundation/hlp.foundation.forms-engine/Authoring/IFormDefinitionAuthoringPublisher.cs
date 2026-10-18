using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Authoring;

/// <summary>
/// The authoring-host boundary for admitting and publishing an immutable form-definition revision.
/// </summary>
public interface IFormDefinitionAuthoringPublisher
{
    /// <summary>
    /// Compiles every executable rule and page guard before the definition store is mutated, then
    /// registers and publishes the admitted draft revision.
    /// </summary>
    ValueTask<FormDefinition> RegisterAndPublishAsync(
        FormDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes through the same admission boundary and returns compute-only impact evidence.</summary>
    /// <param name="definition">The Draft revision to admit and atomically publish.</param>
    /// <param name="context">Trusted historical and opaque publication bindings.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    async ValueTask<FormDefinitionPublicationResult> RegisterAndPublishWithImpactAsync(
        FormDefinition definition, FormPublicationAssessmentContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var published = await RegisterAndPublishAsync(definition, cancellationToken).ConfigureAwait(false);
        // Existing third-party implementations remain source-compatible. They do not pretend to assess.
        return new(published, FormPublicationAssessment.Unavailable("publisher_assessment_unsupported"));
    }
}
