using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms;

/// <summary>
/// The creation gate a Records record type declares (DES-0004 section 1, <c>creation_gate</c>): who may create a
/// record of the type. Forms reads it only to decide whether a form targeting the type may be published and submitted.
/// </summary>
public enum RecordTypeCreationGate
{
    /// <summary><c>form_only</c>: records are created only through a gated form.</summary>
    FormOnly,
    /// <summary><c>role</c>: holders of the type's creation role create records; a form may invoke the same operation.</summary>
    Role,
    /// <summary><c>engine</c>: only the engine creates records; no form may target the type.</summary>
    Engine,
}

/// <summary>
/// Narrow lookup seam to a record type's creation policy. Forms has no dependency on the Records definition store;
/// the host adapts it here. The host reader owns every compatibility rule (K6): it reads each published contract
/// version's own spelling and applies a default only where that version's published contract defines one. Forms
/// never defaults a policy itself.
/// </summary>
public interface IFormRecordTypePolicyReader
{
    /// <summary>
    /// Returns the type's effective creation gate in the tenant, or <see langword="null"/> when the tenant has no record
    /// type with that id. A failure to read is reported by throwing.
    /// </summary>
    ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default);
}

/// <summary>A targeted form's creation policy could not be resolved; <see cref="Code"/> names why.</summary>
public sealed class RecordTypeCreationPolicyException : Exception
{
    /// <summary>Creates the refusal with its stable code.</summary>
    public RecordTypeCreationPolicyException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    /// <summary>
    /// <c>form.record_type.policy_unavailable</c> when no reader is configured, the reader fails or reports a value
    /// outside the enumeration; <c>form.record_type.unknown</c> when the tenant has no such record type.
    /// </summary>
    public string Code { get; }
}

/// <summary>Resolves a targeted form's creation gate through the host reader, failing closed.</summary>
public static class RecordTypeCreationPolicy
{
    /// <summary>
    /// Resolves the gate for <paramref name="recordTypeId"/>. With no reader, a failing reader or a value outside the
    /// enumeration it throws <see cref="RecordTypeCreationPolicyException"/> with
    /// <see cref="FormDefinitionCodes.RecordTypePolicyUnavailable"/>; for a type the tenant does not have, with
    /// <see cref="FormDefinitionCodes.RecordTypeUnknown"/>. Caller cancellation propagates unchanged.
    /// </summary>
    public static async ValueTask<RecordTypeCreationGate> ResolveAsync(
        IFormRecordTypePolicyReader? reader,
        TenantId tenant,
        string recordTypeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordTypeId);
        if (reader is null)
            throw new RecordTypeCreationPolicyException(FormDefinitionCodes.RecordTypePolicyUnavailable,
                $"no record type policy reader is configured to resolve '{recordTypeId}'.");
        RecordTypeCreationGate? declared;
        try
        {
            declared = await reader.GetCreationGateAsync(tenant, recordTypeId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception failure)
        {
            throw new RecordTypeCreationPolicyException(FormDefinitionCodes.RecordTypePolicyUnavailable,
                $"the creation policy of record type '{recordTypeId}' could not be read.", failure);
        }

        if (declared is not { } gate)
            throw new RecordTypeCreationPolicyException(FormDefinitionCodes.RecordTypeUnknown,
                $"the tenant has no record type '{recordTypeId}'.");
        if (!Enum.IsDefined(gate))
            throw new RecordTypeCreationPolicyException(FormDefinitionCodes.RecordTypePolicyUnavailable,
                $"record type '{recordTypeId}' reported an unknown creation gate.");
        return gate;
    }
}
