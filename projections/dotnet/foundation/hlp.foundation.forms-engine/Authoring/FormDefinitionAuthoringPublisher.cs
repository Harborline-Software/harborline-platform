using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Authoring;

/// <summary>
/// Fail-closed implementation of the Harborline App authoring-host save-and-publish sequence.
/// </summary>
public sealed class FormDefinitionAuthoringPublisher(
    IFormDefinitionStore store,
    IFormDefinitionPolicyAdmission policyAdmission)
    : IFormDefinitionAuthoringPublisher
{
    /// <inheritdoc />
    public async ValueTask<FormDefinition> RegisterAndPublishAsync(
        FormDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();

        if (definition.Status != FormDefinitionStatus.Draft)
        {
            throw new FormDefinitionValidationException(
                definition.Id,
                $"authoring publication requires a Draft definition, but '{definition.Id}' v{definition.Version} is {definition.Status}.");
        }

        // This must remain before the first store mutation. It is the destination equivalent of
        // Harborline App's FormDefinitionPublishAdmission call in the authoring endpoint.
        RuleCompileAdmission.ValidateOrThrow(definition);
        policyAdmission.ValidateOrThrow(definition);

        return await store.RegisterAndPublishAsync(definition, cancellationToken).ConfigureAwait(false);
    }
}
