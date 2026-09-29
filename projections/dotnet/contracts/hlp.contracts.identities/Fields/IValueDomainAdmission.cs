namespace Harborline.Contracts.Fields;

/// <summary>
/// Admits one authored permitted-value declaration. Implemented by the field runtime so tiers that
/// may not reference foundation (such as the kernel's Records grammar) bind to the one shared rule
/// rather than restating it.
/// </summary>
public interface IValueDomainAdmission
{
    /// <summary>Returns every refusal for the declaration, each at an authored RFC 6901 location.</summary>
    IReadOnlyList<FieldRefusal> Validate(ValueDomainDefinition domain, string jsonPointer);
}
