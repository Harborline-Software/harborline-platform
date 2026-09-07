using System.Text.RegularExpressions;

namespace Harborline.Blocks.EntityViews;

/// <summary>Shared immutable pack seeds plus tenant-local override and created-type maps.</summary>
public sealed partial class InMemoryTypeCatalogStore
{
    private readonly IReadOnlyDictionary<string, TypeDetailWire> seeds;
    private readonly Dictionary<string, TenantRows> tenants = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public InMemoryTypeCatalogStore(IEnumerable<TypeDetailWire> packSeeds)
    {
        ArgumentNullException.ThrowIfNull(packSeeds);
        seeds = packSeeds.ToDictionary(
            seed => seed.Id,
            seed => Clone(seed) with
            {
                Provenance = "Pack", OverridesSeed = false, SeedExists = true, HasTenantRow = false,
            },
            StringComparer.Ordinal);
    }

    public ITypeCatalogStore ForTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        lock (sync)
        {
            if (!tenants.TryGetValue(tenantId, out var rows))
            {
                rows = new TenantRows();
                tenants.Add(tenantId, rows);
            }
            return new Scope(this, rows);
        }
    }

    private sealed class Scope(InMemoryTypeCatalogStore owner, TenantRows rows) : ITypeCatalogStore
    {
        public ValueTask<IReadOnlyList<TypeWire>> GetEffectiveCatalogAsync()
        {
            lock (owner.sync)
            {
                var effective = owner.seeds.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var pair in rows.Overrides) effective[pair.Key] = pair.Value;
                foreach (var pair in rows.Created) effective[pair.Key] = pair.Value;
                IReadOnlyList<TypeWire> result = effective.Values.Select(ToRow)
                    .OrderBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
                return ValueTask.FromResult(result);
            }
        }

        public ValueTask<TypeDetailWire?> GetTypeAsync(string id)
        {
            lock (owner.sync)
            {
                var value = rows.Overrides.GetValueOrDefault(id)
                    ?? rows.Created.GetValueOrDefault(id)
                    ?? owner.seeds.GetValueOrDefault(id);
                return ValueTask.FromResult(value is null ? null : Clone(value));
            }
        }

        public ValueTask<TypeDetailWire> CreateTypeAsync(TypeUpsertBody body)
        {
            var valid = Validate(body);
            var id = string.IsNullOrWhiteSpace(body.Id) ? $"type:generated/{Guid.NewGuid():N}" : body.Id.Trim();
            lock (owner.sync)
            {
                if (owner.seeds.ContainsKey(id)) throw Failure(EntityViewsTypeCodes.SeedExistsUsePut);
                if (rows.Created.ContainsKey(id)) throw Failure(EntityViewsTypeCodes.TypeExistsUsePut);
                var detail = Build(id, valid, false, false, true);
                rows.Created.Add(id, detail);
                return ValueTask.FromResult(Clone(detail));
            }
        }

        public ValueTask<TypeDetailWire?> UpdateTypeAsync(string id, TypeUpsertBody body)
        {
            var valid = Validate(body);
            lock (owner.sync)
            {
                if (owner.seeds.ContainsKey(id))
                {
                    var detail = Build(id, valid, true, true, true);
                    rows.Overrides[id] = detail;
                    return ValueTask.FromResult<TypeDetailWire?>(Clone(detail));
                }
                if (!rows.Created.ContainsKey(id)) return ValueTask.FromResult<TypeDetailWire?>(null);
                var updated = Build(id, valid, false, false, true);
                rows.Created[id] = updated;
                return ValueTask.FromResult<TypeDetailWire?>(Clone(updated));
            }
        }

        public ValueTask<TypeDetailWire?> RevertTypeAsync(string id)
        {
            lock (owner.sync)
            {
                if (!owner.seeds.TryGetValue(id, out var seed)) return ValueTask.FromResult<TypeDetailWire?>(null);
                rows.Overrides.Remove(id);
                return ValueTask.FromResult<TypeDetailWire?>(Clone(seed));
            }
        }
    }

    private sealed class TenantRows
    {
        public Dictionary<string, TypeDetailWire> Overrides { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, TypeDetailWire> Created { get; } = new(StringComparer.Ordinal);
    }

    private static TypeUpsertBody Validate(TypeUpsertBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.DisplayName)) throw Failure(EntityViewsTypeCodes.DisplayNameRequired);
        if (body.Traits is null || !body.Traits.Any(KnownTrait)) throw Failure(EntityViewsTypeCodes.AtLeastOneTraitRequired);
        if (body.ConditionScaleMax is < 2) throw Failure(EntityViewsTypeCodes.ConditionScaleMinTwo);
        if (body.ExpectedUsefulLifeYears is < 0) throw Failure(EntityViewsTypeCodes.LifeYearsNonNegative);
        ValidateForm(body.PropertyForm);
        foreach (var form in body.InspectionForms ?? [])
        {
            if (string.IsNullOrWhiteSpace(form.Discipline)) throw Failure(EntityViewsTypeCodes.InspectionFormDisciplineRequired);
            if (string.IsNullOrWhiteSpace(form.Definition)) throw Failure(EntityViewsTypeCodes.InspectionFormRefRequired);
            ValidateForm(new TypeFormRefBody(form.Definition, form.Version));
        }
        return body;
    }

    private static void ValidateForm(TypeFormRefBody? form)
    {
        if (form is null || string.IsNullOrWhiteSpace(form.Definition)) return;
        if (string.IsNullOrWhiteSpace(form.Version)) throw Failure(EntityViewsTypeCodes.FormVersionRequired);
        if (!SemanticVersion().IsMatch(form.Version.Trim())) throw Failure(EntityViewsTypeCodes.InvalidFormVersion);
    }

    private static bool KnownTrait(string trait) => trait.Trim().ToLowerInvariant() is "container" or "maintainable" or "movable";

    private static TypeDetailWire Build(string id, TypeUpsertBody body, bool overrides, bool seedExists, bool tenantRow) => new(
        id, body.DisplayName!.Trim(), body.Traits!.Where(KnownTrait).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray(),
        NullIfBlank(body.ParentType), body.Disciplines?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToArray() ?? [],
        "Tenant", overrides, seedExists, tenantRow,
        body.PropertyForm is null || string.IsNullOrWhiteSpace(body.PropertyForm.Definition) ? null : new(body.PropertyForm.Definition.Trim(), body.PropertyForm.Version!.Trim()),
        body.InspectionForms?.Select(form => new InspectionBindingWire(form.Discipline!.Trim(), form.Definition!.Trim(), form.Version!.Trim()))
            .OrderBy(form => form.Discipline, StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
        body.ConditionScaleMax, body.ExpectedUsefulLifeYears,
        body.TypicalReplacementCost is null ? null : new MoneyWire(
            body.TypicalReplacementCost.Amount,
            string.IsNullOrWhiteSpace(body.TypicalReplacementCost.Currency) ? "USD" : body.TypicalReplacementCost.Currency.Trim().ToUpperInvariant()));

    private static TypeWire ToRow(TypeDetailWire detail) => new(
        detail.Id, detail.DisplayName, [.. detail.Traits], detail.ParentType, [.. detail.Disciplines], detail.Provenance,
        detail.OverridesSeed, detail.PropertyForm is not null, detail.InspectionForms.Count,
        detail.ConditionScaleMax, detail.ExpectedUsefulLifeYears, detail.TypicalReplacementCost);

    private static TypeDetailWire Clone(TypeDetailWire detail) => detail with
    {
        Traits = [.. detail.Traits], Disciplines = [.. detail.Disciplines], InspectionForms = [.. detail.InspectionForms],
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static EntityViewsException Failure(string code) => new(code, "The entity type request is invalid.");

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();
}
