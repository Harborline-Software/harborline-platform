using System.Text.Json;
using Harborline.Contracts.Workflow;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Blocks.InspectionReview;

/// <summary>Thread-safe local binding registry for tests, previews, and single-process hosts.</summary>
public sealed class InMemoryInspectionReviewBindingResolver : IInspectionReviewBindingResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Tenant, string Form, SemanticVersion Version), InspectionReviewBinding> _bindings = [];

    /// <summary>Registers one immutable tenant/form/version binding; conflicting replacement is refused.</summary>
    public void Register(TenantId tenant, InspectionReviewBinding binding)
    {
        if (tenant.IsSystemSentinel) throw new ArgumentException("System tenant cannot own inspection bindings.", nameof(tenant));
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate();
        var workflow = InspectionReviewProcess.BuildWorkflow(tenant.Value, binding, DateTimeOffset.UnixEpoch);
        var admission = WorkflowAdmissionValidator.Validate(
            workflow,
            new ImmutableWorkflowAuthorityResolver(new Dictionary<string, ActionClassification>()));
        if (!admission.IsValid) throw new ArgumentException("Inspection workflow contract failed admission.", nameof(binding));

        var key = (tenant.Value, binding.FormId.Value, binding.FormVersion);
        lock (_gate)
        {
            if (_bindings.TryGetValue(key, out var existing) && existing != binding)
                throw new InvalidOperationException("An immutable inspection binding already exists for this tenant and form revision.");
            _bindings[key] = binding;
        }
    }

    /// <inheritdoc />
    public ValueTask<InspectionReviewBinding?> ResolveAsync(
        TenantId tenant,
        FormDefinitionId formId,
        SemanticVersion formVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _bindings.TryGetValue((tenant.Value, formId.Value, formVersion), out var binding);
            return ValueTask.FromResult(binding);
        }
    }
}

/// <summary>Thread-safe tenant-partitioned subject directory for tests, previews, and migration mocks.</summary>
public sealed class InMemoryInspectionSubjectDirectory : IInspectionSubjectDirectory
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Tenant, string Subject), InspectionSubject> _subjects = [];

    /// <summary>Adds or replaces one subject inside one non-system tenant.</summary>
    public void Upsert(TenantId tenant, InspectionSubject subject)
    {
        if (tenant.IsSystemSentinel) throw new ArgumentException("System tenant cannot own inspection subjects.", nameof(tenant));
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject.Reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject.DisplayName);
        using var _ = JsonDocument.Parse(subject.MetadataJson);
        lock (_gate) _subjects[(tenant.Value, subject.Reference)] = subject;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<InspectionSubject>> ListAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<InspectionSubject> rows = _subjects
                .Where(row => StringComparer.Ordinal.Equals(row.Key.Tenant, tenant.Value))
                .Select(row => row.Value)
                .OrderBy(row => row.DisplayName, StringComparer.Ordinal)
                .ThenBy(row => row.Reference, StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult(rows);
        }
    }

    /// <inheritdoc />
    public ValueTask<InspectionSubject?> GetAsync(
        TenantId tenant,
        string subjectReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectReference);
        lock (_gate)
        {
            _subjects.TryGetValue((tenant.Value, subjectReference), out var subject);
            return ValueTask.FromResult(subject);
        }
    }
}
