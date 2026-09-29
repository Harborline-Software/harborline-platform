namespace Harborline.Foundation.Forms.Engine;

/// <inheritdoc />
public abstract class FormEngineException : Exception
{
    /// <inheritdoc />
    protected FormEngineException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    /// <inheritdoc />
    public string Code { get; }
}

/// <inheritdoc />
public sealed class FormEngineNotFoundException()
    : FormEngineException("form.engine.not-found", "The requested form resource was not found.");

/// <inheritdoc />
public sealed class FormEngineDeniedException()
    : FormEngineException("form.engine.denied", "The form operation was denied.");

/// <summary>
/// Signals from an execution-context adapter that the requested tenant is not available for the
/// current actor. The engine deliberately normalizes this signal at its public boundary so tenant
/// existence and activation state are never disclosed.
/// </summary>
public sealed class FormExecutionTenantUnavailableException()
    : Exception("The current tenant is unavailable.");

/// <inheritdoc />
public sealed class FormEngineResourceBoundException(Exception? innerException = null)
    : FormEngineException("form.engine.resource-bound", "The form operation exceeded a resource bound.", innerException);

/// <inheritdoc />
public sealed class FormEngineValidationException(IReadOnlyList<Harborline.Contracts.Forms.ValidationError> errors)
    : FormEngineException("form.engine.validation-failed", "The form candidate is invalid.")
{
    /// <inheritdoc />
    public IReadOnlyList<Harborline.Contracts.Forms.ValidationError> Errors { get; } = errors;
}

/// <inheritdoc />
public sealed class FormEngineProviderUnavailableException(Exception? innerException = null)
    : FormEngineException("form.engine.provider-unavailable", "A required form provider is unavailable.", innerException);

/// <inheritdoc />
public sealed class FormEngineIdempotencyConflictException()
    : FormEngineException("form.engine.idempotency-conflict", "The idempotency key is already bound to another request.");

/// <inheritdoc />
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

/// <inheritdoc />
public sealed class FormEngineGovernanceException(string field, FormGovernanceRefusal reason)
    : FormEngineException("form.engine.governance-refused", "A field governance requirement refused the operation.")
{
    /// <inheritdoc />
    public string Field { get; } = field;
    /// <inheritdoc />
    public FormGovernanceRefusal Reason { get; } = reason;
}
