namespace Harborline.Blocks.EntityViews;

/// <summary>Tenant-scoped catalog of entity types: pack seeds plus tenant overrides and created types.</summary>
public interface ITypeCatalogStore
{
    /// <summary>Returns every type visible in the tenant scope as catalog rows.</summary>
    ValueTask<IReadOnlyList<TypeWire>> GetEffectiveCatalogAsync();
    /// <summary>Returns null only when the id is unknown in this ambient tenant scope.</summary>
    ValueTask<TypeDetailWire?> GetTypeAsync(string id);
    /// <summary>Creates a tenant type; throws <see cref="EntityViewsException"/> with an <see cref="EntityViewsTypeCodes"/> code when the body is invalid or the id already exists.</summary>
    ValueTask<TypeDetailWire> CreateTypeAsync(TypeUpsertBody body);
    /// <summary>Editing a pack seed creates a tenant override; it never mutates the seed.</summary>
    ValueTask<TypeDetailWire?> UpdateTypeAsync(string id, TypeUpsertBody body);
    /// <summary>Returns null when the id is not a pack seed.</summary>
    ValueTask<TypeDetailWire?> RevertTypeAsync(string id);
}

/// <summary>Wave B constants to merge into the existing EntityViewsCodes static class.</summary>
public static class EntityViewsTypeCodes
{
    /// <summary>The type has no display name.</summary>
    public const string DisplayNameRequired = "views.type.display_name_required";
    /// <summary>The type declares no recognised trait (container, maintainable or movable).</summary>
    public const string AtLeastOneTraitRequired = "views.type.at_least_one_trait_required";
    /// <summary>A referenced form has a definition but no version.</summary>
    public const string FormVersionRequired = "views.type.form_version_required";
    /// <summary>A referenced form version is not a semantic version.</summary>
    public const string InvalidFormVersion = "views.type.invalid_form_version";
    /// <summary>The condition scale maximum is below 2.</summary>
    public const string ConditionScaleMinTwo = "views.type.condition_scale_min_two";
    /// <summary>The expected useful life in years is negative.</summary>
    public const string LifeYearsNonNegative = "views.type.life_years_non_negative";
    /// <summary>An inspection form has no discipline.</summary>
    public const string InspectionFormDisciplineRequired = "views.type.inspection_form_discipline_required";
    /// <summary>An inspection form has no form definition.</summary>
    public const string InspectionFormRefRequired = "views.type.inspection_form_ref_required";
    /// <summary>The id belongs to a pack seed, so it must be edited with PUT rather than created.</summary>
    public const string SeedExistsUsePut = "views.type.seed_exists_use_put";
    /// <summary>The tenant already created this type id, so it must be edited with PUT rather than created.</summary>
    public const string TypeExistsUsePut = "views.type.type_exists_use_put";
}
