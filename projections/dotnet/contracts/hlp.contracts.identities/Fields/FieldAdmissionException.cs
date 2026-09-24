namespace Harborline.Contracts.Fields;

/// <summary>A failed shared-field admission, with detached, member-neutral refusals.</summary>
public sealed class FieldAdmissionException : Exception
{
    /// <summary>Creates an admission failure without exposing the author's mutable refusal list.</summary>
    public FieldAdmissionException(IReadOnlyList<FieldRefusal> refusals)
        : base("The field declaration is not admitted.")
    {
        ArgumentNullException.ThrowIfNull(refusals);
        Refusals = Array.AsReadOnly(refusals.ToArray());
    }

    /// <summary>The stable codes and authored locations that refused this declaration.</summary>
    public IReadOnlyList<FieldRefusal> Refusals { get; }
}
