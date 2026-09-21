namespace Harborline.Contracts.Fields;

// Promoted from the T-615 Records declarations at a625fdd. The names and three-source
// wire shape are retained so Records binds to this producer rather than converting a copy.

/// <summary>A versioned field-runtime kind named by an author.</summary>
/// <param name="KindId">The registered kind identity.</param>
/// <param name="Version">The admitted immutable kind version.</param>
/// <param name="Parameters">Author-supplied parameters understood by that kind.</param>
public sealed record FieldKindReference(
    string KindId,
    string Version,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>A Taxonomy scheme used as the one permitted-value source.</summary>
/// <param name="SchemeId">The stable scheme identity.</param>
/// <param name="Version">The immutable scheme version.</param>
public sealed record TaxonomySchemeReference(string SchemeId, string Version);

/// <summary>A relationship and predicate used as the one permitted-value source.</summary>
/// <param name="RecordTypeId">The queried record type.</param>
/// <param name="Predicate">The admitted fixed Records query predicate.</param>
public sealed record RecordQueryValueSource(string RecordTypeId, string Predicate);

/// <summary>Exactly one of the three permitted-value sources.</summary>
/// <param name="LiteralValues">A small inline tenant-owned literal set.</param>
/// <param name="TaxonomyScheme">A referenced Taxonomy scheme.</param>
/// <param name="RecordQuery">A relationship to a record type plus predicate.</param>
public sealed record ValueDomainDefinition(
    IReadOnlyList<string>? LiteralValues = null,
    TaxonomySchemeReference? TaxonomyScheme = null,
    RecordQueryValueSource? RecordQuery = null);

/// <summary>The governance facts declared on one field.</summary>
/// <param name="PersonalData">Whether the field contains personal data.</param>
/// <param name="Confidential">Whether the field is confidential.</param>
/// <param name="Masked">Whether the field is masked.</param>
/// <param name="Classification">The classification identity, when one is declared.</param>
public sealed record FieldGovernanceDefinition(
    bool PersonalData,
    bool Confidential,
    bool Masked,
    string? Classification);

/// <summary>The kind/version that supplied materialized creation defaults.</summary>
/// <param name="KindId">The field-kind identity.</param>
/// <param name="KindVersion">The field-kind version.</param>
public sealed record FieldKindDefaultProvenance(string KindId, string KindVersion);

/// <summary>The JSON scalar shape produced by an admitted field-kind revision.</summary>
public enum FieldScalarValueShape
{
    /// <summary>A JSON string.</summary>
    Text,
    /// <summary>A JSON boolean.</summary>
    Boolean,
    /// <summary>A JSON integer.</summary>
    Integer,
    /// <summary>A JSON number, including non-integral values.</summary>
    Number,
}

/// <summary>An admitted field kind and the editable defaults it supplies at field creation.</summary>
/// <param name="KindId">The registered field-kind identity.</param>
/// <param name="Version">The admitted immutable kind version.</param>
/// <param name="GovernanceDefaults">The governance values copied into a newly created field.</param>
/// <param name="ValueShape">The JSON scalar shape produced by this exact kind revision.</param>
public sealed record AdmittedFieldKind(
    string KindId,
    string Version,
    FieldGovernanceDefinition? GovernanceDefaults,
    FieldScalarValueShape ValueShape = FieldScalarValueShape.Text);

/// <summary>Constraints shared by fields and the declarations that narrow them.</summary>
/// <param name="Required">Whether a value is always required at this floor.</param>
/// <param name="MinimumCount">The admitted minimum multiplicity.</param>
/// <param name="MaximumCount">The admitted maximum multiplicity, or no finite maximum.</param>
/// <param name="ReadRoleIds">The roles admitted to read the value.</param>
/// <param name="ValueDomain">The admitted value domain, when constrained.</param>
public sealed record FieldConstraintDefinition(
    bool Required,
    int MinimumCount,
    int? MaximumCount,
    IReadOnlyList<string> ReadRoleIds,
    ValueDomainDefinition? ValueDomain);

/// <summary>A substrate refusal that carries a stable code and authored RFC 6901 location.</summary>
/// <param name="Code">The stable, member-neutral refusal code.</param>
/// <param name="JsonPointer">The authored location, not a value from the record body.</param>
/// <param name="Message">A diagnostic explanation that does not disclose record values.</param>
public sealed record FieldRefusal(string Code, string JsonPointer, string Message);
