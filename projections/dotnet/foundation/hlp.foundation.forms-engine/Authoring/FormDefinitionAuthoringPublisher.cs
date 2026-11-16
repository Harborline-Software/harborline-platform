using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Assets.Common;
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
    public ValueTask<FormDefinition> CreateAsync(FormDefinition definition, CancellationToken cancellationToken = default)
        => SaveAsync(definition, store.CreateAsync, cancellationToken);

    /// <inheritdoc />
    public ValueTask<FormDefinition> SaveNextAsync(FormDefinition definition, CancellationToken cancellationToken = default)
        => SaveAsync(definition, store.SaveNextAsync, cancellationToken);

    private async ValueTask<FormDefinition> SaveAsync(FormDefinition definition,
        Func<FormDefinition, CancellationToken, ValueTask<FormDefinition>> write, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.Status != FormDefinitionStatus.Draft)
                throw new FormDefinitionValidationException(definition.Id, "authoring save requires a Draft definition.");
            await ValidateAsync(definition, cancellationToken).ConfigureAwait(false);
            return await write(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (FormDefinitionLifecycleException exception) { throw new DefinitionRefusalException(DefinitionAdmissionPhase.Author, [new(exception.Code, "")]); }
        catch (FieldAdmissionException exception) { throw FieldRefusal(DefinitionAdmissionPhase.Author, exception); }
        catch (FormDefinitionValidationException exception) { throw ValidationRefusal(DefinitionAdmissionPhase.Author, exception); }
    }

    /// <inheritdoc />
    public async ValueTask<FormDefinition> RestoreAsDraftAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken cancellationToken = default)
    {
        try
        {
            var source = await store.GetAsync(tenant, id, version, cancellationToken).ConfigureAwait(false);
            await ValidateAsync(source with { Status = FormDefinitionStatus.Draft }, cancellationToken).ConfigureAwait(false);
            return await store.RestoreAsDraftAsync(tenant, id, version, cancellationToken).ConfigureAwait(false);
        }
        catch (FormDefinitionLifecycleException exception) { throw new DefinitionRefusalException(DefinitionAdmissionPhase.Author, [new(exception.Code, "")]); }
        catch (FieldAdmissionException exception) { throw FieldRefusal(DefinitionAdmissionPhase.Author, exception); }
        catch (FormDefinitionValidationException exception) { throw ValidationRefusal(DefinitionAdmissionPhase.Author, exception); }
    }

    /// <inheritdoc />
    public async ValueTask<FormDefinition> PublishAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken cancellationToken = default)
    {
        try
        {
            var draft = await store.GetAsync(tenant, id, version, cancellationToken).ConfigureAwait(false);
            await ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
            return await store.PublishAsync(tenant, id, version, cancellationToken).ConfigureAwait(false);
        }
        catch (FormDefinitionLifecycleException exception) { throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new(exception.Code, "")]); }
        catch (FieldAdmissionException exception) { throw FieldRefusal(DefinitionAdmissionPhase.Publish, exception); }
        catch (FormDefinitionValidationException exception) { throw ValidationRefusal(DefinitionAdmissionPhase.Publish, exception); }
    }

    private static DefinitionRefusalException ValidationRefusal(DefinitionAdmissionPhase phase, FormDefinitionValidationException exception)
    {
        var code = exception.Code ?? "form_definition.validation_refused";
        // Target holds a pointer only where the submit-gate and record-type checks throw; elsewhere it is an
        // author-chosen node id, so the code picks the pointer and Target is never echoed for those codes.
        var pointer = code.StartsWith("form.pages.", StringComparison.Ordinal) || code == "form.rules.guard_uncompilable" ? "/overlay/pages"
            : code.StartsWith("form.rules.", StringComparison.Ordinal) ? "/overlay/rules"
            : code.StartsWith("form.tree.", StringComparison.Ordinal) || code.StartsWith("form.layout.", StringComparison.Ordinal) ? "/overlay/sections"
            : code.StartsWith("form.checks.", StringComparison.Ordinal) || code == "aspect.sensitive_input_unacknowledged" ? "/overlay/asyncChecks"
            : (code.StartsWith("form.submit_gate.", StringComparison.Ordinal) || code.StartsWith("form.record_type.", StringComparison.Ordinal))
                && exception.Target?.StartsWith('/') == true ? exception.Target
            : "/overlay";
        return new(phase, [new(code, pointer)]);
    }

    private static DefinitionRefusalException FieldRefusal(DefinitionAdmissionPhase phase, FieldAdmissionException exception)
        => new(phase, exception.Refusals.Select(refusal => new DefinitionRefusal(refusal.Code, refusal.JsonPointer)));

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
        catch (FormDefinitionLifecycleException exception)
        {
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new(exception.Code, "")]);
        }
        catch (FieldAdmissionException exception)
        {
            throw FieldRefusal(DefinitionAdmissionPhase.Publish, exception);
        }
        catch (FormDefinitionValidationException exception)
        {
            throw ValidationRefusal(DefinitionAdmissionPhase.Publish, exception);
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

        await ValidateAsync(definition, cancellationToken).ConfigureAwait(false);

        var assessment = context is null ? FormPublicationAssessment.Unavailable("publication_basis_unavailable")
            : await new FormPublicationImpactAssessment().AssessAsync(definition, store, context, cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var published = await store.RegisterAndPublishAsync(definition, cancellationToken).ConfigureAwait(false);
        return new(published, assessment);
    }
    private async ValueTask ValidateAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
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

    }

}
