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
}
