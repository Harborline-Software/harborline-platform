using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Assets.Common;

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

    /// <summary>Admits and creates a draft.</summary>
    ValueTask<FormDefinition> CreateAsync(FormDefinition definition, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Draft creation is not supported by this publisher.");

    /// <summary>Admits and saves the next draft revision.</summary>
    ValueTask<FormDefinition> SaveNextAsync(FormDefinition definition, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Draft saving is not supported by this publisher.");

    /// <summary>Admits and restores an existing revision as a new draft.</summary>
    ValueTask<FormDefinition> RestoreAsDraftAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Draft restoration is not supported by this publisher.");

    /// <summary>Admits and publishes an existing draft.</summary>
    ValueTask<FormDefinition> PublishAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Stored draft publication is not supported by this publisher.");

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
