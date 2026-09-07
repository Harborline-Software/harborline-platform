namespace Harborline.Blocks.EntityViews;

public interface ITypeCatalogStore
{
    ValueTask<IReadOnlyList<TypeWire>> GetEffectiveCatalogAsync();
    /// <summary>Returns null only when the id is unknown in this ambient tenant scope.</summary>
    ValueTask<TypeDetailWire?> GetTypeAsync(string id);
    ValueTask<TypeDetailWire> CreateTypeAsync(TypeUpsertBody body);
    /// <summary>Editing a pack seed creates a tenant override; it never mutates the seed.</summary>
    ValueTask<TypeDetailWire?> UpdateTypeAsync(string id, TypeUpsertBody body);
    /// <summary>Returns null when the id is not a pack seed.</summary>
    ValueTask<TypeDetailWire?> RevertTypeAsync(string id);
}

/// <summary>Wave B constants to merge into the existing EntityViewsCodes static class.</summary>
public static class EntityViewsTypeCodes
{
    public const string DisplayNameRequired = "views.type.display_name_required";
    public const string AtLeastOneTraitRequired = "views.type.at_least_one_trait_required";
    public const string FormVersionRequired = "views.type.form_version_required";
    public const string InvalidFormVersion = "views.type.invalid_form_version";
    public const string ConditionScaleMinTwo = "views.type.condition_scale_min_two";
    public const string LifeYearsNonNegative = "views.type.life_years_non_negative";
    public const string InspectionFormDisciplineRequired = "views.type.inspection_form_discipline_required";
    public const string InspectionFormRefRequired = "views.type.inspection_form_ref_required";
    public const string SeedExistsUsePut = "views.type.seed_exists_use_put";
    public const string TypeExistsUsePut = "views.type.type_exists_use_put";
}
