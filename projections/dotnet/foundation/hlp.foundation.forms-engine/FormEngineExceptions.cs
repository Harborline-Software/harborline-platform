namespace Harborline.Foundation.Forms.Engine;

/// <summary>Base exception for stable, machine-readable Forms Engine failures.</summary>
public abstract class FormEngineException : Exception
{
    /// <summary>Initializes a failure with its stable code, message, and optional underlying exception.</summary>
    protected FormEngineException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    /// <summary>Gets the stable machine-readable code for this engine failure.</summary>
    public string Code { get; }
}

/// <summary>Signals that the requested form resource does not exist.</summary>
public sealed class FormEngineNotFoundException()
    : FormEngineException("form.engine.not-found", "The requested form resource was not found.");

/// <summary>Signals that the caller is not authorized for the requested form operation.</summary>
public sealed class FormEngineDeniedException()
    : FormEngineException("form.engine.denied", "The form operation was denied.");

/// <summary>
/// Signals from an execution-context adapter that the requested tenant is not available for the
/// current actor. The engine deliberately normalizes this signal at its public boundary so tenant
/// existence and activation state are never disclosed.
/// </summary>
public sealed class FormExecutionTenantUnavailableException()
    : Exception("The current tenant is unavailable.");

/// <summary>Signals that a configured resource bound prevented the operation from completing.</summary>
public sealed class FormEngineResourceBoundException(Exception? innerException = null)
    : FormEngineException("form.engine.resource-bound", "The form operation exceeded a resource bound.", innerException);

/// <summary>Signals that candidate validation failed and exposes the stable validation errors.</summary>
public sealed class FormEngineValidationException(IReadOnlyList<Harborline.Contracts.Forms.ValidationError> errors)
    : FormEngineException("form.engine.validation-failed", "The form candidate is invalid.")
{
    /// <summary>Gets the validation errors that caused candidate rejection.</summary>
    public IReadOnlyList<Harborline.Contracts.Forms.ValidationError> Errors { get; } = errors;
}

/// <summary>Signals that a required provider failed or could not be reached.</summary>
public sealed class FormEngineProviderUnavailableException(Exception? innerException = null)
    : FormEngineException("form.engine.provider-unavailable", "A required form provider is unavailable.", innerException);

/// <summary>Signals reuse of an idempotency key with a different request fingerprint.</summary>
public sealed class FormEngineIdempotencyConflictException()
    : FormEngineException("form.engine.idempotency-conflict", "The idempotency key is already bound to another request.");

/// <summary>Identifies the governance refusal that prevented a sensitive form operation.</summary>
public enum FormGovernanceRefusal
{
    /// <summary>The policy could not be resolved.</summary>
    PolicyUnresolved,
    /// <summary>The resolved policy is invalid.</summary>
    PolicyInvalid,
    /// <summary>Residency requirements were not configured.</summary>
    ResidencyUnconfigured,
    /// <summary>The requested residency was denied.</summary>
    ResidencyDenied,
    /// <summary>The subject required for governance is missing.</summary>
    SubjectRequired,
}

/// <summary>Signals that field governance refused an operation and identifies the affected field.</summary>
public sealed class FormEngineGovernanceException(string field, FormGovernanceRefusal reason)
    : FormEngineException("form.engine.governance-refused", "A field governance requirement refused the operation.")
{
    /// <summary>Gets the field whose governance requirement refused the operation.</summary>
    public string Field { get; } = field;
    /// <summary>Gets the governance refusal reason.</summary>
    public FormGovernanceRefusal Reason { get; } = reason;
}
