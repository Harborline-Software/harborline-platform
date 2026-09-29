namespace Harborline.Foundation.Forms.Engine;

/// <summary>Represents the form engine exception contract used by this package.</summary>
public abstract class FormEngineException : Exception
{
    /// <summary>Initializes the form engine exception instance with the supplied dependencies.</summary>
    protected FormEngineException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    /// <summary>Provides the code associated with this value.</summary>
    public string Code { get; }
}

/// <summary>Represents the form engine not found exception contract used by this package.</summary>
public sealed class FormEngineNotFoundException()
    : FormEngineException("form.engine.not-found", "The requested form resource was not found.");

/// <summary>Represents the form engine denied exception contract used by this package.</summary>
public sealed class FormEngineDeniedException()
    : FormEngineException("form.engine.denied", "The form operation was denied.");

/// <summary>
/// Signals from an execution-context adapter that the requested tenant is not available for the
/// current actor. The engine deliberately normalizes this signal at its public boundary so tenant
/// existence and activation state are never disclosed.
/// </summary>
public sealed class FormExecutionTenantUnavailableException()
    : Exception("The current tenant is unavailable.");

/// <summary>Represents the form engine resource bound exception contract used by this package.</summary>
public sealed class FormEngineResourceBoundException(Exception? innerException = null)
    : FormEngineException("form.engine.resource-bound", "The form operation exceeded a resource bound.", innerException);

/// <summary>Represents the form engine validation exception contract used by this package.</summary>
public sealed class FormEngineValidationException(IReadOnlyList<Harborline.Contracts.Forms.ValidationError> errors)
    : FormEngineException("form.engine.validation-failed", "The form candidate is invalid.")
{
    /// <summary>Provides the errors associated with this value.</summary>
    public IReadOnlyList<Harborline.Contracts.Forms.ValidationError> Errors { get; } = errors;
}

/// <summary>Represents the form engine provider unavailable exception contract used by this package.</summary>
public sealed class FormEngineProviderUnavailableException(Exception? innerException = null)
    : FormEngineException("form.engine.provider-unavailable", "A required form provider is unavailable.", innerException);

/// <summary>Represents the form engine idempotency conflict exception contract used by this package.</summary>
public sealed class FormEngineIdempotencyConflictException()
    : FormEngineException("form.engine.idempotency-conflict", "The idempotency key is already bound to another request.");

/// <summary>Identifies the governance condition that refused a form operation.</summary>
public enum FormGovernanceRefusal
{
    /// <summary>The policy unresolved option.</summary>
    PolicyUnresolved,
    /// <summary>The policy invalid option.</summary>
    PolicyInvalid,
    /// <summary>The residency unconfigured option.</summary>
    ResidencyUnconfigured,
    /// <summary>The residency denied option.</summary>
    ResidencyDenied,
    /// <summary>The subject required option.</summary>
    SubjectRequired,
}

/// <summary>Represents the form engine governance exception contract used by this package.</summary>
public sealed class FormEngineGovernanceException(string field, FormGovernanceRefusal reason)
    : FormEngineException("form.engine.governance-refused", "A field governance requirement refused the operation.")
{
    /// <summary>Provides the field associated with this value.</summary>
    public string Field { get; } = field;
    /// <summary>Provides the reason associated with this value.</summary>
    public FormGovernanceRefusal Reason { get; } = reason;
}
