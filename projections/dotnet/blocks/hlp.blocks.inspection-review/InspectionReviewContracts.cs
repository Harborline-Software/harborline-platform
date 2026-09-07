using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Blocks.InspectionReview;

/// <summary>Authoritative condition classification derived from accepted form values.</summary>
public enum InspectionConditionOutcome
{
    /// <summary>The configured condition threshold was satisfied without human review.</summary>
    Accepted,
    /// <summary>A human must review the submitted condition.</summary>
    ReviewRequired,
}

/// <summary>The only decisions admitted by the inspection-review process.</summary>
public enum InspectionReviewDecision
{
    /// <summary>Accept the inspection outcome.</summary>
    Approve,
    /// <summary>Return the subject for corrective work or another inspection.</summary>
    Rework,
}

/// <summary>Where the review subject identifier is resolved from.</summary>
public enum InspectionSubjectSource
{
    /// <summary>Use the trusted Forms Engine case reference.</summary>
    CaseReference,
    /// <summary>Read a string from one accepted-value JSON pointer.</summary>
    AcceptedValue,
}

/// <summary>Frozen tenant-owned mapping from one form revision to inspection review.</summary>
public sealed record InspectionReviewBinding
{
    /// <summary>Form definition admitted by this binding.</summary>
    public required FormDefinitionId FormId { get; init; }
    /// <summary>Exact immutable form revision admitted by this binding.</summary>
    public required SemanticVersion FormVersion { get; init; }
    /// <summary>RFC-6901 pointer to the accepted integer condition score.</summary>
    public required string ConditionPointer { get; init; }
    /// <summary>Source of the review subject reference.</summary>
    public InspectionSubjectSource SubjectSource { get; init; } = InspectionSubjectSource.CaseReference;
    /// <summary>RFC-6901 pointer used when <see cref="SubjectSource"/> is <see cref="InspectionSubjectSource.AcceptedValue"/>.</summary>
    public string? SubjectPointer { get; init; }
    /// <summary>Lowest admitted condition score.</summary>
    public int MinimumScore { get; init; } = 1;
    /// <summary>Highest admitted condition score.</summary>
    public int MaximumScore { get; init; } = 5;
    /// <summary>Scores at or below this value require human review.</summary>
    public int ReviewRequiredAtOrBelow { get; init; } = 2;

    /// <summary>Fails closed when the binding cannot be applied deterministically.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FormId.Value);
        if (FormVersion.Major < 0 || FormVersion.Minor < 0 || FormVersion.Patch < 0)
            throw new ArgumentOutOfRangeException(nameof(FormVersion));
        if (string.IsNullOrWhiteSpace(ConditionPointer) || !ConditionPointer.StartsWith('/', StringComparison.Ordinal))
            throw new ArgumentException("ConditionPointer must be an RFC-6901 pointer.", nameof(ConditionPointer));
        if (MinimumScore > MaximumScore) throw new ArgumentOutOfRangeException(nameof(MinimumScore));
        if (ReviewRequiredAtOrBelow < MinimumScore || ReviewRequiredAtOrBelow >= MaximumScore)
            throw new ArgumentOutOfRangeException(nameof(ReviewRequiredAtOrBelow));
        if (SubjectSource == InspectionSubjectSource.AcceptedValue
            && (string.IsNullOrWhiteSpace(SubjectPointer) || !SubjectPointer.StartsWith('/', StringComparison.Ordinal)))
            throw new ArgumentException("SubjectPointer must be an RFC-6901 pointer for accepted-value subjects.", nameof(SubjectPointer));
        if (SubjectSource == InspectionSubjectSource.CaseReference && SubjectPointer is not null)
            throw new ArgumentException("Case-reference bindings cannot also declare SubjectPointer.", nameof(SubjectPointer));
    }
}

/// <summary>Minimal same-tenant subject projection needed by the first vertical.</summary>
public sealed record InspectionSubject(string Reference, string DisplayName, string MetadataJson = "{}");

/// <summary>Tenant-bound source of frozen inspection bindings.</summary>
public interface IInspectionReviewBindingResolver
{
    /// <summary>Returns the exact binding for one tenant and immutable form revision.</summary>
    ValueTask<InspectionReviewBinding?> ResolveAsync(
        TenantId tenant,
        FormDefinitionId formId,
        SemanticVersion formVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>Host-owned same-tenant subject query port.</summary>
public interface IInspectionSubjectDirectory
{
    /// <summary>Lists subjects visible in one tenant.</summary>
    ValueTask<IReadOnlyList<InspectionSubject>> ListAsync(TenantId tenant, CancellationToken cancellationToken = default);
    /// <summary>Gets one subject in one tenant; absent and foreign both return null.</summary>
    ValueTask<InspectionSubject?> GetAsync(TenantId tenant, string subjectReference, CancellationToken cancellationToken = default);
}

/// <summary>Lifecycle exposed by an inspection-review decision receipt.</summary>
public enum InspectionReviewLifecycle
{
    /// <summary>The review awaits an authorized human decision.</summary>
    Pending,
    /// <summary>The inspection was approved.</summary>
    Approved,
    /// <summary>The inspection was returned for rework.</summary>
    Rework,
}

/// <summary>Typed caller projection of one inspection review.</summary>
public sealed record InspectionReviewView(
    string ReviewId,
    string SubmissionId,
    InspectionSubject Subject,
    int ConditionScore,
    InspectionConditionOutcome ConditionOutcome,
    InspectionReviewLifecycle Lifecycle,
    long Version,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<InspectionReviewDecision> AllowedDecisions);

/// <summary>Operations guarded by current server-side review policy.</summary>
public enum InspectionReviewOperation
{
    /// <summary>Read subjects that can be inspected.</summary>
    ReadSubjects,
    /// <summary>Read pending inspection-review work.</summary>
    ReadReviews,
    /// <summary>Apply an approve or rework decision.</summary>
    Decide,
}

/// <summary>Mandatory server-side authorization port; roles are never supplied by caller payloads.</summary>
public interface IInspectionReviewAuthorizer
{
    /// <summary>Returns whether the current authenticated actor may perform one operation.</summary>
    ValueTask<bool> IsAllowedAsync(InspectionReviewOperation operation, CancellationToken cancellationToken = default);
}

/// <summary>Expected-version and fingerprint-bound request to complete one review.</summary>
public sealed record DecideInspectionReviewRequest
{
    /// <summary>Target review identifier.</summary>
    public required string ReviewId { get; init; }
    /// <summary>Version read from the current review projection.</summary>
    public required long ExpectedVersion { get; init; }
    /// <summary>One closed review decision.</summary>
    public required InspectionReviewDecision Decision { get; init; }
    /// <summary>Caller-stable retry key.</summary>
    public required string IdempotencyKey { get; init; }
    /// <summary>Optional bounded audit note.</summary>
    public string? Note { get; init; }
}

/// <summary>Stable mutation outcome exposed by the block.</summary>
public enum InspectionReviewMutationDisposition
{
    /// <summary>A new mutation committed.</summary>
    Committed,
    /// <summary>The exact prior receipt was replayed.</summary>
    Replayed,
    /// <summary>The retry key was already used for different content.</summary>
    IdempotencyConflict,
    /// <summary>The expected version is stale.</summary>
    VersionConflict,
    /// <summary>No actionable current-tenant review exists.</summary>
    NotFound,
    /// <summary>No authenticated active-tenant Party scope exists.</summary>
    Denied,
    /// <summary>The decision is invalid for the current review.</summary>
    InvalidDecision,
}

/// <summary>Review mutation result and replayable current projection.</summary>
public sealed record InspectionReviewMutationResult(
    InspectionReviewMutationDisposition Disposition,
    InspectionReviewView? Review)
{
    /// <summary>True when a new or replayed mutation is durable.</summary>
    public bool IsSuccess => Disposition is InspectionReviewMutationDisposition.Committed or InspectionReviewMutationDisposition.Replayed;
}

/// <summary>Authenticated tenant-scoped query and decision facade.</summary>
public interface IInspectionReviewService
{
    /// <summary>Lists current-tenant subjects available for inspection.</summary>
    Task<IReadOnlyList<InspectionSubject>> ListSubjectsAsync(CancellationToken cancellationToken = default);
    /// <summary>Gets one current-tenant subject.</summary>
    Task<InspectionSubject?> GetSubjectAsync(string subjectReference, CancellationToken cancellationToken = default);
    /// <summary>Lists pending current-tenant inspection reviews.</summary>
    Task<IReadOnlyList<InspectionReviewView>> ListPendingAsync(CancellationToken cancellationToken = default);
    /// <summary>Gets one pending current-tenant inspection review.</summary>
    Task<InspectionReviewView?> GetAsync(string reviewId, CancellationToken cancellationToken = default);
    /// <summary>Applies one expected-version approve or rework decision.</summary>
    Task<InspectionReviewMutationResult> DecideAsync(DecideInspectionReviewRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Stable machine codes for projection skips and recoverable failures.</summary>
public static class InspectionReviewCodes
{
    /// <summary>No condition value was accepted.</summary>
    public const string ConditionMissing = "inspection-review.condition-missing";
    /// <summary>The accepted condition value is not an admitted integer score.</summary>
    public const string ConditionInvalid = "inspection-review.condition-invalid";
    /// <summary>No subject reference could be resolved.</summary>
    public const string SubjectUnresolved = "inspection-review.subject-unresolved";
    /// <summary>The subject is absent or belongs to another tenant.</summary>
    public const string SubjectNotFound = "inspection-review.subject-not-found";
    /// <summary>The current authenticated scope cannot deliver this tenant's envelope.</summary>
    public const string ProjectionScopeMismatch = "inspection-review.projection-scope-mismatch";
}
