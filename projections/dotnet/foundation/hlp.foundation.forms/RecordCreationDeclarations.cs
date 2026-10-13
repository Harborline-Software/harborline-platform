using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms;

/// <summary>
/// K9 (approved 2026-10-10): the producer-side declaration of which Records Record Types a host's submission producers
/// (its projection sinks) create from a form. A form's own <c>record_type_id</c> is metadata the author may omit; the
/// producer that actually writes the records is the authority on whether a submission creates any. Publication and submit
/// compare the two, so missing target metadata never exempts a form whose submissions create Records.
/// </summary>
/// <remarks>
/// A host that wires a projection sink which creates Records records implements this port (the sink itself may implement
/// it). A host with no such producer supplies none, and every form keeps its current behaviour. The definition store and
/// the form engine choose the declaration by the one rule <see cref="RecordCreationDeclarations.Select"/>, so publication
/// and submit always read the same source and refuse consistently. Independently of this
/// port, the Records create enforces the bound Record Type's creation gate on every write, so a producer that is not
/// declared still cannot create a <c>form_only</c> or <c>engine</c> type's records directly.
/// </remarks>
public interface IFormRecordCreationDeclarations
{
    /// <summary>
    /// Returns the ids of the Record Types the host's producers create from a submission of <paramref name="formId"/> in
    /// <paramref name="tenant"/>, for any of its revisions; empty when the form creates no Records records. A failure to
    /// read is reported by throwing.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> GetCreatedRecordTypesAsync(
        TenantId tenant, FormDefinitionId formId, CancellationToken cancellationToken = default);
}

/// <summary>Resolves a form's declared record creation through the host port, failing closed.</summary>
public static class RecordCreationDeclarations
{
    /// <summary>
    /// The one source rule (K9) shared by <see cref="InMemoryFormDefinitionStore"/> and the form engine: the explicit
    /// <paramref name="declarations"/> when supplied, otherwise <paramref name="producer"/> (the host's submission
    /// producer, for example its projection sink) when it implements <see cref="IFormRecordCreationDeclarations"/>,
    /// otherwise none.
    /// </summary>
    public static IFormRecordCreationDeclarations? Select(IFormRecordCreationDeclarations? declarations, object? producer)
        => declarations ?? producer as IFormRecordCreationDeclarations;

    /// <summary>
    /// The one Record Type the host's producers declare <paramref name="formId"/> creates, or <see langword="null"/> when
    /// no port is configured or the port declares none. A failing port, a blank declared id, or more than one distinct
    /// declared type (compared ordinally, never trimmed) throws <see cref="RecordTypeCreationPolicyException"/> with
    /// <see cref="FormDefinitionCodes.RecordTypePolicyUnavailable"/> or <see cref="FormDefinitionCodes.RecordTypeTargetAmbiguous"/>.
    /// Caller cancellation propagates unchanged.
    /// </summary>
    public static async ValueTask<string?> ResolveAsync(
        IFormRecordCreationDeclarations? declarations,
        TenantId tenant,
        FormDefinitionId formId,
        CancellationToken cancellationToken = default)
    {
        if (declarations is null) return null;
        IReadOnlyCollection<string> declared;
        try
        {
            declared = await declarations.GetCreatedRecordTypesAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception failure)
        {
            throw new RecordTypeCreationPolicyException(FormDefinitionCodes.RecordTypePolicyUnavailable,
                $"the record creation declared for form '{formId}' could not be read.", failure);
        }

        if (declared is null || declared.Any(string.IsNullOrWhiteSpace))
            throw new RecordTypeCreationPolicyException(FormDefinitionCodes.RecordTypePolicyUnavailable,
                $"the record creation declared for form '{formId}' is malformed.");
        var distinct = declared.Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length switch
        {
            0 => null,
            1 => distinct[0],
            _ => throw new RecordTypeCreationPolicyException(FormDefinitionCodes.RecordTypeTargetAmbiguous,
                $"form '{formId}' is declared to create more than one record type."),
        };
    }
}
