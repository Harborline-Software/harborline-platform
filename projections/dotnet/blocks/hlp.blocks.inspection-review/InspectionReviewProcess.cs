using System.Security.Cryptography;
using System.Text;
using Harborline.Contracts.Forms;
using Harborline.Contracts.Workflow;

namespace Harborline.Blocks.InspectionReview;

/// <summary>Versioned deterministic inspection classification and process definition.</summary>
public static class InspectionReviewProcess
{
    /// <summary>Frozen process key.</summary>
    public const string DefinitionKey = "inspection-review.v1";
    /// <summary>Frozen process version.</summary>
    public const string DefinitionVersion = "1.0.0";
    /// <summary>Human review state.</summary>
    public const string ReviewState = "review";
    /// <summary>Approved terminal state.</summary>
    public const string ApprovedState = "approved";
    /// <summary>Rework terminal state.</summary>
    public const string ReworkState = "rework";
    /// <summary>Approve outcome identifier.</summary>
    public const string ApproveOutcome = "approve";
    /// <summary>Rework outcome identifier.</summary>
    public const string ReworkOutcome = "rework";

    /// <summary>Classifies one admitted score without ambient time or mutable state.</summary>
    public static InspectionConditionOutcome Classify(InspectionReviewBinding binding, int score)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate();
        if (score < binding.MinimumScore || score > binding.MaximumScore)
            throw new ArgumentOutOfRangeException(nameof(score));
        return score <= binding.ReviewRequiredAtOrBelow
            ? InspectionConditionOutcome.ReviewRequired
            : InspectionConditionOutcome.Accepted;
    }

    /// <summary>Derives one opaque collision-resistant review identifier from a persisted form instance.</summary>
    public static string DeriveReviewId(string submissionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ir1\0{submissionId}"));
        return $"ir1:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>Builds the frozen human-action workflow contract admitted by this block.</summary>
    public static WorkflowDefinition BuildWorkflow(string tenant, InspectionReviewBinding binding, DateTimeOffset frozenAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate();
        var title = new Harborline.Contracts.Forms.InternationalizedText
        {
            DefaultLocale = "en",
            Values = new Dictionary<string, string>(StringComparer.Ordinal) { ["en"] = "Inspection review" },
        };
        return new WorkflowDefinition
        {
            Key = DefinitionKey,
            Version = DefinitionVersion,
            Status = WorkflowDefinitionStatus.Published,
            Tenant = tenant,
            Owner = new IdentityRef { Scheme = "system", Value = DefinitionKey },
            Title = title,
            SubjectFormRef = new FormDefinitionRef { FormId = binding.FormId.Value, Version = binding.FormVersion.ToString() },
            Mutability = WorkflowMutability.Locked,
            InitialState = ReviewState,
            States =
            [
                new WorkflowState { Id = ReviewState, Label = title, Kind = WorkflowStateKind.Normal },
                new WorkflowState { Id = ApprovedState, Label = Text("Approved"), Kind = WorkflowStateKind.Terminal },
                new WorkflowState { Id = ReworkState, Label = Text("Rework"), Kind = WorkflowStateKind.Terminal },
            ],
            Transitions =
            [
                new WorkflowTransition { Id = ApproveOutcome, From = ReviewState, On = ApproveOutcome, To = ApprovedState },
                new WorkflowTransition { Id = ReworkOutcome, From = ReviewState, On = ReworkOutcome, To = ReworkState },
            ],
            Triggers =
            [
                new WorkflowTriggerBinding { Id = ApproveOutcome, Kind = WorkflowTriggerKind.HumanAction, Task = DefinitionKey },
                new WorkflowTriggerBinding { Id = ReworkOutcome, Kind = WorkflowTriggerKind.HumanAction, Task = DefinitionKey },
            ],
            Actions = [],
            Guards = [],
            CreatedAt = frozenAt.ToUniversalTime().ToString("O"),
            UpdatedAt = frozenAt.ToUniversalTime().ToString("O"),
        };
    }

    private static Harborline.Contracts.Forms.InternationalizedText Text(string value) => new()
    {
        DefaultLocale = "en",
        Values = new Dictionary<string, string>(StringComparer.Ordinal) { ["en"] = value },
    };
}
