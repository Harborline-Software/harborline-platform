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
    IFormFieldBindingSource? fieldBindings = null,
    IFieldKindRuntime? fieldKinds = null,
    IFieldDomainRuntime? fieldDomains = null,
    IAuthenticatedActorContext? actor = null)
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
        if (fieldBindings is not null || fieldKinds is not null || fieldDomains is not null)
        {
            if (fieldBindings is null || fieldKinds is null || fieldDomains is null)
                throw FormFieldBinding.Refuse("field.binding_unresolved", "");
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

        return await store.RegisterAndPublishAsync(definition, cancellationToken).ConfigureAwait(false);
    }
}
