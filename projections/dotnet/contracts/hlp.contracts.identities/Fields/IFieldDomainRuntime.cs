using Harborline.Foundation.Assets.Common;
using System.Text.Json;

namespace Harborline.Contracts.Fields;

/// <summary>The three authored sources of permitted field values.</summary>
public enum ValueDomainSourceKind
{
    /// <summary>A tenant-owned literal set.</summary>
    LiteralSet,
    /// <summary>An exact Taxonomy scheme revision.</summary>
    TaxonomyScheme,
    /// <summary>A record type constrained by a predicate.</summary>
    RecordQuery,
}

/// <summary>The runtime's editor choice after read-authority filtering.</summary>
public enum FieldEditorKind
{
    /// <summary>No permitted readable value is available.</summary>
    None,
    /// <summary>Exactly one permitted readable value is available.</summary>
    SingleValue,
    /// <summary>A choice among multiple literal values.</summary>
    ChoiceList,
    /// <summary>A choice supplied by Taxonomy.</summary>
    TaxonomyPicker,
    /// <summary>A choice among predicate-matching records.</summary>
    RecordPicker,
    /// <summary>A small set of permitted readable choices.</summary>
    RadioGroup,
}

/// <summary>The explicit tenant and principal for a domain read.</summary>
/// <param name="Tenant">The non-sentinel tenant isolation scope.</param>
/// <param name="Principal">The caller whose read authority applies.</param>
public sealed record FieldDomainScope(TenantId Tenant, string Principal);

/// <summary>A caller-visible resolution; unreadable members and their counts are not exposed.</summary>
/// <param name="SourceKind">The source named by the declaration.</param>
/// <param name="Values">The permitted values this principal may read.</param>
/// <param name="Editor">The choice derived from source and readable cardinality.</param>
/// <param name="Predicate">The original record-query predicate, or null for other sources.</param>
/// <param name="SnapshotRevision">The source revision evaluated for this resolution.</param>
public sealed record ResolvedValueDomain(
    ValueDomainSourceKind SourceKind,
    IReadOnlyList<string> Values,
    FieldEditorKind Editor,
    string? Predicate,
    string SnapshotRevision);

/// <summary>An original source participating in a resolved constraint proof; never a synthetic source.</summary>
/// <param name="SourceKind">One of the three declared source kinds.</param>
/// <param name="TaxonomyScheme">The exact scheme reference when the source is Taxonomy.</param>
/// <param name="RecordQuery">The original record type and predicate when the source is a query.</param>
public sealed record FieldDomainAttribution(
    ValueDomainSourceKind SourceKind,
    TaxonomySchemeReference? TaxonomyScheme,
    RecordQueryValueSource? RecordQuery);

/// <summary>A detached constraint proof at one pinned revision, with only readable membership.</summary>
/// <param name="Required">Whether a value is required.</param>
/// <param name="MinimumCount">The greatest declared minimum.</param>
/// <param name="MaximumCount">The least declared maximum, or no finite maximum.</param>
/// <param name="ReadRoleIds">The intersected roles; empty means unrestricted.</param>
/// <param name="Values">Readable permitted values; null means no domain was declared.</param>
/// <param name="Sources">Original source and predicate attribution for each contributing domain.</param>
/// <param name="SnapshotRevision">The complete pinned revision used for the proof.</param>
/// <param name="Editor">The shared runtime's choice for the final readable intersection, absent without a domain.</param>
public sealed record ResolvedFieldConstraints(
    bool Required,
    int MinimumCount,
    int? MaximumCount,
    IReadOnlyList<string> ReadRoleIds,
    IReadOnlyList<string>? Values,
    IReadOnlyList<FieldDomainAttribution> Sources,
    string SnapshotRevision,
    FieldEditorKind? Editor = null);

/// <summary>The shared permitted-value and editor-choice interpreter used by member consumers.</summary>
public interface IFieldDomainRuntime
{
    /// <summary>Checks required, multiplicity, readable membership and each original scalar against its compiled kind.
    /// Undefined or null denotes an absent value; arrays are repeated values, never scalar containers.</summary>
    IReadOnlyList<FieldRefusal> Validate(
        ResolvedFieldConstraints constraints,
        ICompiledFieldKind kind,
        JsonElement value,
        string jsonPointer);

    /// <summary>Resolves one declaration without returning any unreadable value.</summary>
    ValueTask<ResolvedValueDomain> ResolveAsync(
        ValueDomainDefinition domain,
        FieldDomainScope scope,
        string jsonPointer,
        CancellationToken cancellationToken = default);

    /// <summary>Intersects supplied constraints, refusing impossible floors using complete private membership.</summary>
    ValueTask<ResolvedFieldConstraints> IntersectAsync(
        IReadOnlyList<FieldConstraintDefinition> constraints,
        FieldDomainScope scope,
        string jsonPointer,
        CancellationToken cancellationToken = default);

    /// <summary>Proves a complete consumer declaration does not widen its declared floor or drop a requirement.</summary>
    ValueTask<ResolvedFieldConstraints> NarrowAsync(
        FieldConstraintDefinition declared,
        FieldConstraintDefinition narrowed,
        FieldDomainScope scope,
        string jsonPointer,
        CancellationToken cancellationToken = default);
}
