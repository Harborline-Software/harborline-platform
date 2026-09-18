using Harborline.Contracts.Fields;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>Admits the shared three-source permitted-value declaration.</summary>
public static class ValueDomainAdmission
{
    /// <summary>Refuses declarations that do not name exactly one permitted-value source.</summary>
    public static IReadOnlyList<FieldRefusal> Validate(ValueDomainDefinition domain, string jsonPointer)
    {
        ArgumentNullException.ThrowIfNull(domain);

        // Promoted from RecordsIntentValidator at a625fdd; absence of an optional
        // domain is handled by its consumer, not confused with an empty declaration.
        var sourceCount = (domain.LiteralValues is null ? 0 : 1)
            + (domain.TaxonomyScheme is null ? 0 : 1)
            + (domain.RecordQuery is null ? 0 : 1);

        return sourceCount == 1
            ? []
            : [new FieldRefusal("field.value_domain_source_count", jsonPointer,
                "A value domain must name exactly one permitted-value source.")];
    }
}
