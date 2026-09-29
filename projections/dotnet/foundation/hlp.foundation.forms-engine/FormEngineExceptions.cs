namespace Harborline.Foundation.Forms.Engine;

public abstract class FormEngineException : Exception
{
    protected FormEngineException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public sealed class FormEngineNotFoundException()
    : FormEngineException("form.engine.not-found", "The requested form resource was not found.");

public sealed class FormEngineDeniedException()
    : FormEngineException("form.engine.denied", "The form operation was denied.");

/// <summary>
/// Signals from an execution-context adapter that the requested tenant is not available for the
/// current actor. The engine deliberately normalizes this signal at its public boundary so tenant
/// existence and activation state are never disclosed.
/// </summary>
public sealed class FormExecutionTenantUnavailableException()
    : Exception("The current tenant is unavailable.");

public sealed class FormEngineResourceBoundException(Exception? innerException = null)
    : FormEngineException("form.engine.resource-bound", "The form operation exceeded a resource bound.", innerException);

public sealed class FormEngineValidationException(IReadOnlyList<Harborline.Contracts.Forms.ValidationError> errors)
    : FormEngineException("form.engine.validation-failed", "The form candidate is invalid.")
{
    public IReadOnlyList<Harborline.Contracts.Forms.ValidationError> Errors { get; } = errors;
}

public sealed class FormEngineProviderUnavailableException(Exception? innerException = null)
    : FormEngineException("form.engine.provider-unavailable", "A required form provider is unavailable.", innerException);

public sealed class FormEngineIdempotencyConflictException()
    : FormEngineException("form.engine.idempotency-conflict", "The idempotency key is already bound to another request.");

public enum FormGovernanceRefusal
{
    PolicyUnresolved,
    PolicyInvalid,
    ResidencyUnconfigured,
    ResidencyDenied,
    SubjectRequired,
}

public sealed class FormEngineGovernanceException(string field, FormGovernanceRefusal reason)
    : FormEngineException("form.engine.governance-refused", "A field governance requirement refused the operation.")
{
    public string Field { get; } = field;
    public FormGovernanceRefusal Reason { get; } = reason;
}
