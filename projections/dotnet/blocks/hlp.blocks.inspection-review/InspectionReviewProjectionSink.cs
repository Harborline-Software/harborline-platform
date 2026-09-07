using System.Text.Json;
using Harborline.Contracts.Workflow;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.MultiTenancy;
using Harborline.Kernel.WorkItems;

namespace Harborline.Blocks.InspectionReview;

/// <summary>Converts accepted Forms Engine projection envelopes into deterministic review work.</summary>
public sealed class InspectionReviewProjectionSink : IFormProjectionSink
{
    private readonly ITenantContext _tenants;
    private readonly IPartyContext _parties;
    private readonly IInspectionReviewBindingResolver _bindings;
    private readonly IInspectionSubjectDirectory _subjects;
    private readonly IWorkItemKernel _workItems;

    /// <summary>Composes the projection over authenticated scope, frozen bindings, subjects, and Work Items.</summary>
    public InspectionReviewProjectionSink(
        ITenantContext tenants,
        IPartyContext parties,
        IInspectionReviewBindingResolver bindings,
        IInspectionSubjectDirectory subjects,
        IWorkItemKernel workItems)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _parties = parties ?? throw new ArgumentNullException(nameof(parties));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _subjects = subjects ?? throw new ArgumentNullException(nameof(subjects));
        _workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
    }

    /// <inheritdoc />
    public async ValueTask<FormProjectionDeliveryResult> DeliverAsync(
        FormProjectionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var activeTenant = await InspectionReviewScope.ResolveAsync(_tenants, _parties, cancellationToken).ConfigureAwait(false);
        if (activeTenant is null || activeTenant.Value != envelope.Tenant)
            throw Pending(InspectionReviewCodes.ProjectionScopeMismatch, "The trusted projection tenant is not the current active tenant.");

        var binding = await _bindings.ResolveAsync(
            envelope.Tenant,
            envelope.FormId,
            envelope.DefinitionVersion,
            cancellationToken).ConfigureAwait(false);
        if (binding is null) return Complete();
        try { binding.Validate(); }
        catch (ArgumentException ex) { throw Pending("inspection-review.binding-invalid", ex.Message); }

        JsonDocument accepted;
        try { accepted = JsonDocument.Parse(envelope.ProtectedAcceptedValues); }
        catch (JsonException ex) { throw Pending("inspection-review.projection-payload-invalid", ex.Message); }
        using (accepted)
        {
            if (!InspectionReviewJson.TryResolve(accepted.RootElement, binding.ConditionPointer, out var condition))
                return Skip(InspectionReviewCodes.ConditionMissing, binding.ConditionPointer);
            if (condition.ValueKind != JsonValueKind.Number || !condition.TryGetInt32(out var score)
                || score < binding.MinimumScore || score > binding.MaximumScore)
                return Skip(InspectionReviewCodes.ConditionInvalid, binding.ConditionPointer);

            var subjectReference = ResolveSubject(binding, envelope.CaseReference, accepted.RootElement);
            if (string.IsNullOrWhiteSpace(subjectReference))
                return Skip(InspectionReviewCodes.SubjectUnresolved, binding.SubjectPointer ?? binding.ConditionPointer);
            var subject = await _subjects.GetAsync(envelope.Tenant, subjectReference, cancellationToken).ConfigureAwait(false);
            if (subject is null) return Skip(InspectionReviewCodes.SubjectNotFound, binding.SubjectPointer ?? binding.ConditionPointer);

            var outcome = InspectionReviewProcess.Classify(binding, score);
            if (outcome == InspectionConditionOutcome.Accepted) return Complete();

            var submissionId = envelope.InstanceId.ToString();
            var reviewId = InspectionReviewProcess.DeriveReviewId(submissionId);
            var state = new PersistedInspectionReview(
                submissionId,
                subject.Reference,
                binding.FormId.Value,
                binding.FormVersion.ToString(),
                score,
                outcome,
                envelope.SubmittedAt,
                envelope.PartyId,
                envelope.ActorId);
            var stateJson = InspectionReviewJson.Serialize(state);
            var workflow = InspectionReviewProcess.BuildWorkflow(envelope.Tenant.Value, binding, envelope.SubmittedAt);
            var admission = WorkflowAdmissionValidator.Validate(
                workflow,
                new ImmutableWorkflowAuthorityResolver(new Dictionary<string, ActionClassification>()));
            if (!admission.IsValid) throw Pending("inspection-review.binding-invalid", "The frozen review process failed admission.");

            var created = await _workItems.CreateAsync(new CreateWorkItemRequest
            {
                Id = reviewId,
                SubjectRef = subject.Reference,
                DefinitionKey = workflow.Key,
                DefinitionVersion = workflow.Version,
                InitialStep = workflow.InitialState,
                InitialStatus = WorkItemStatus.Parked,
                StateJson = stateJson,
                BasisJson = stateJson,
                AllowedOutcomes = workflow.Transitions.Select(transition => new WorkItemOutcome(
                    transition.Id,
                    transition.From,
                    transition.To,
                    WorkItemStatus.Completed)).ToArray(),
                IdempotencyKey = $"inspection-review.create:{reviewId}",
            }, cancellationToken).ConfigureAwait(false);

            if (!created.IsSuccess)
                throw Pending($"inspection-review.work-item-{created.Disposition.ToString().ToLowerInvariant()}", "Review work could not be committed.");
            return Complete();
        }
    }

    private static string? ResolveSubject(InspectionReviewBinding binding, string? caseReference, JsonElement accepted)
    {
        if (binding.SubjectSource == InspectionSubjectSource.CaseReference) return NullIfBlank(caseReference);
        if (binding.SubjectPointer is null
            || !InspectionReviewJson.TryResolve(accepted, binding.SubjectPointer, out var value)
            || value.ValueKind != JsonValueKind.String) return null;
        return NullIfBlank(value.GetString());
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static FormProjectionDeliveryResult Complete() => new(Array.Empty<FormProjectionSkip>());
    private static FormProjectionDeliveryResult Skip(string reason, string field) => new([new FormProjectionSkip(reason, field)]);
    private static InspectionReviewProjectionException Pending(string code, string message) => new(code, message);
}
