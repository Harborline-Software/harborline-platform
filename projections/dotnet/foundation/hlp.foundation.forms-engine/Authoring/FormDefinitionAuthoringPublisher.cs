using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.MultiTenancy;

namespace Harborline.Foundation.Forms.Engine.Authoring;

/// <summary>
/// Fail-closed implementation of the Harborline App authoring-host save-and-publish sequence.
/// </summary>
public sealed class FormDefinitionAuthoringPublisher(
    IFormDefinitionStore store,
    IFormDefinitionPolicyAdmission policyAdmission,
    IFormFieldBindingSource fieldBindings,
    IFieldKindRuntime fieldKinds,
    IFieldDomainRuntime fieldDomains,
    IAuthenticatedActorContext? actor = null)
    : IFormDefinitionAuthoringPublisher
{
    /// <inheritdoc />
    public async ValueTask<FormDefinition> RegisterAndPublishAsync(
        FormDefinition definition,
        CancellationToken cancellationToken = default)
        => (await PublishAsync(definition, null, cancellationToken).ConfigureAwait(false)).Definition;

    /// <inheritdoc />
    public ValueTask<FormDefinitionPublicationResult> RegisterAndPublishWithImpactAsync(
        FormDefinition definition, FormPublicationAssessmentContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return PublishAsync(definition, context, cancellationToken);
    }

    private async ValueTask<FormDefinitionPublicationResult> PublishAsync(FormDefinition definition,
        FormPublicationAssessmentContext? context, CancellationToken cancellationToken)
    {
        try { return await PublishCoreAsync(definition, context, cancellationToken).ConfigureAwait(false); }
        catch (FieldAdmissionException exception)
        {
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish,
                exception.Refusals.Select(refusal => new DefinitionRefusal(refusal.Code, refusal.JsonPointer)));
        }
    }

    private async ValueTask<FormDefinitionPublicationResult> PublishCoreAsync(FormDefinition definition,
        FormPublicationAssessmentContext? context, CancellationToken cancellationToken)
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
        {
            if (string.IsNullOrWhiteSpace(actor?.UserId))
                throw FormFieldBinding.Refuse("field.value_domain_principal_required", "");
            var tenant = actor.Tenant;
            if (tenant is null || tenant.Id.IsSystemSentinel || tenant.Status != TenantStatus.Active)
                throw FormFieldBinding.Refuse("field.value_domain_tenant_required", "");
            if (tenant.Id != definition.Tenant)
                throw FormFieldBinding.Refuse("field.value_domain_tenant_mismatch", "");
            await new FormFieldBinding(fieldBindings, fieldKinds, fieldDomains)
                .ResolveAsync(new FieldDomainScope(tenant.Id, actor.UserId), definition, cancellationToken).ConfigureAwait(false);
        }

        var assessment = context is null ? FormPublicationAssessment.Unavailable("publication_basis_unavailable")
            : await new FormPublicationImpactAssessment().AssessAsync(definition, store, context, cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var published = await store.RegisterAndPublishAsync(definition, cancellationToken).ConfigureAwait(false);
        return new(published, assessment);
    }
}
