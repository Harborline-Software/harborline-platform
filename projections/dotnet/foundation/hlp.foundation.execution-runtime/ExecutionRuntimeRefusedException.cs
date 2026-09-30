namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>A request the execution runtime refuses, carrying one stable refusal code.</summary>
public sealed class ExecutionRuntimeRefusedException : InvalidOperationException
{
    /// <summary>Creates a refusal with its stable code and a diagnostic message.</summary>
    public ExecutionRuntimeRefusedException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    /// <summary>The stable refusal code, one of <see cref="ExecutionRuntimeRefusals"/>.</summary>
    public string Code { get; }
}

/// <summary>The closed set of refusal codes the execution runtime raises.</summary>
public static class ExecutionRuntimeRefusals
{
    /// <summary>A run kind name is not a lower-case kebab token.</summary>
    public const string RunKindInvalid = "execution.run_kind_invalid";

    /// <summary>A run kind is registered a second time.</summary>
    public const string RunKindDuplicate = "execution.run_kind_duplicate";

    /// <summary>A run is started for a kind no engine registered.</summary>
    public const string RunKindUnregistered = "execution.run_kind_unregistered";

    /// <summary>A run identity is stored twice.</summary>
    public const string RunDuplicate = "execution.run_duplicate";

    /// <summary>A run identity is not in the tenant's store.</summary>
    public const string RunUnknown = "execution.run_unknown";

    /// <summary>A stored run changed under a writer that read an earlier version.</summary>
    public const string RunConcurrencyConflict = "execution.run_concurrency_conflict";

    /// <summary>A <c>caused_by</c> link names the run itself or a run the tenant does not hold.</summary>
    public const string CausedByInvalid = "execution.caused_by_invalid";

    /// <summary>A status change the closed transition table does not allow.</summary>
    public const string TransitionIllegal = "execution.transition_illegal";

    /// <summary>A retry profile name the substrate does not define, including any raw retry parameter.</summary>
    public const string RetryProfileUnknown = "execution.retry_profile_unknown";

    /// <summary>A retry profile the capability does not allow.</summary>
    public const string RetryProfileNotAllowed = "execution.retry_profile_not_allowed";

    /// <summary>A run awaiting retry is started before its next attempt is due.</summary>
    public const string RetryNotDue = "execution.retry_not_due";

    /// <summary>An effect identity is missing or cannot safely serve as a stable opaque identity.</summary>
    public const string EffectIdentityInvalid = "execution.effect_identity_invalid";

    /// <summary>A second receipt is recorded for one logical effect in the same tenant.</summary>
    public const string EffectReceiptDuplicate = "execution.effect_receipt_duplicate";

    /// <summary>Required effect evidence is absent, inconsistent or outside its closed domain.</summary>
    public const string EffectReceiptInvalid = "execution.effect_receipt_invalid";

    /// <summary>An effect receipt contains a secret value instead of an opaque secret reference.</summary>
    public const string SecretReferenceInvalid = "execution.secret_reference_invalid";

    /// <summary>An ambiguous effect receipt lacks the reconciliation state the capability owes.</summary>
    public const string EffectReconciliationRequired = "execution.effect_reconciliation_required";

    /// <summary>A compensation is recorded while the original effect remains ambiguous.</summary>
    public const string BlindCompensationRefused = "execution.blind_compensation_refused";
}
