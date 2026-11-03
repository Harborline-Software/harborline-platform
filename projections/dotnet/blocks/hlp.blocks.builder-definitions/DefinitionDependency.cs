namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The definition types a Definition Dependency end names: the platform package's seeded <c>platform.definition-type.*</c>
/// ids, each a member of the Catalogue Class (DES-0004 §2; ADR-0054). Only the types Records definitions reach are listed;
/// each other primitive adds its own when it adopts Definition Dependency.
/// </summary>
public static class CatalogueDefinitionType
{
    /// <summary>A Record Type.</summary>
    public const string RecordType = "platform.definition-type.record-type";
    /// <summary>A Class.</summary>
    public const string Class = "platform.definition-type.class";
    /// <summary>A Trait.</summary>
    public const string Trait = "platform.definition-type.trait";
}

/// <summary>
/// One end of a Definition Dependency: a qualified Catalogue Class reference that carries the definition's type beside its
/// id (L1426; ADR-0054), and the version where the reference is pinned.
/// </summary>
/// <param name="DefinitionType">The definition's type, a <see cref="CatalogueDefinitionType"/> id.</param>
/// <param name="DefinitionId">The definition's stable id within the tenant.</param>
/// <param name="Version">The pinned version; null where the reference follows the target's published head.</param>
public sealed record CatalogueDefinitionReference(string DefinitionType, string DefinitionId, string? Version = null);

/// <summary>How a Definition Dependency came to exist (DES-0004 §2).</summary>
public enum DefinitionDependencyKind
{
    /// <summary>The source declared it: a cross-package reference its envelope <c>requires</c>, sealed by a pin.</summary>
    Declared,
    /// <summary>Derived from what the published body references inside its own package.</summary>
    Derived,
}

/// <summary>
/// One Definition Dependency (DES-0004 §2; L769): <c>from_definition_id</c> and <c>to_definition_id</c> each target the
/// Catalogue Class, so a dependency between any two definitions is a stored edge and impact analysis is a query.
/// </summary>
/// <param name="FromDefinitionId">The depending definition at its published version.</param>
/// <param name="ToDefinitionId">The definition depended on.</param>
/// <param name="Kind">Declared or derived.</param>
/// <param name="EdgeReason">The DES-0004 §1 member that carries the reference, for example <c>class_id</c>.</param>
public sealed record DefinitionDependency(
    CatalogueDefinitionReference FromDefinitionId,
    CatalogueDefinitionReference ToDefinitionId,
    DefinitionDependencyKind Kind,
    string EdgeReason);

/// <summary>Both directions for one definition, read from the published heads of a tenant's catalogue.</summary>
/// <param name="DependsOn">The edges whose <c>from_definition_id</c> is the definition.</param>
/// <param name="Dependents">The edges whose <c>to_definition_id</c> is the definition.</param>
public sealed record DefinitionDependencies(IReadOnlyList<DefinitionDependency> DependsOn, IReadOnlyList<DefinitionDependency> Dependents);

/// <summary>
/// A registry's trusted dependency derivation, registered by the host beside its admission. The store runs it while it commits
/// a publication, over that commit's own view of the tenant's published heads, and stores the edges it returns in the same
/// unit. It throws <see cref="DefinitionRefusalException"/> for an end that names no catalogue definition, and the
/// publication then commits nothing.
/// </summary>
/// <param name="published">The document being published.</param>
/// <param name="publishedHead">The highest published revision of a key at the commit, or null when none is published.</param>
public delegate IReadOnlyList<DefinitionDependency> DefinitionDependencyDerivation(
    DefinitionDocument published, Func<DefinitionKey, DefinitionRevision?> publishedHead);
