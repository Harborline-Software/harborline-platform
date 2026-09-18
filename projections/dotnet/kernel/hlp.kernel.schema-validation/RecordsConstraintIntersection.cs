namespace Harborline.Kernel.SchemaValidation;

internal static class RecordsConstraintIntersection
{
    internal static IReadOnlyList<FieldConstraintDefinition> ForField(
        RecordTypeDefinition definition,
        RecordFieldDefinition field)
    {
        var constraints = new List<FieldConstraintDefinition>();
        var hasMultiplicity = false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (RecordFieldDefinition? current = field; current is not null && visited.Add(current.Key);)
        {
            if (current.Constraints is not null)
            {
                constraints.Add(current.Constraints);
                hasMultiplicity = true;
            }
            if (current.ValueDomain is not null) constraints.Add(new(false, 0, null, [], current.ValueDomain));
            foreach (var binding in definition.TraitBindings.Where(binding => binding.FieldKey == current.Key))
            {
                var slot = definition.Traits
                    .FirstOrDefault(trait => trait.TraitId == binding.TraitId && trait.Version == binding.TraitVersion)?
                    .Slots.FirstOrDefault(candidate => candidate.SlotKey == binding.SlotKey);
                if (slot is not null)
                {
                    constraints.Add(slot.Constraints);
                    hasMultiplicity = true;
                }
            }
            current = current.RefinesFieldKey is null ? null
                : definition.Fields.FirstOrDefault(candidate => candidate.Key == current.RefinesFieldKey);
        }
        if (!hasMultiplicity)
            constraints.Add(new(false, 0, field.Reference?.Cardinality == ReferenceCardinality.Many ? null : 1, [], null));
        return constraints;
    }

    internal static bool TryResolve(
        IEnumerable<FieldConstraintDefinition> constraints,
        out FieldConstraintDefinition result)
    {
        var required = false;
        var minimum = 0;
        int? maximum = null;
        HashSet<string>? roles = null;
        ValueDomainDefinition? domain = null;
        var compatible = true;
        foreach (var constraint in constraints)
        {
            required |= constraint.Required;
            minimum = Math.Max(minimum, constraint.MinimumCount);
            if (constraint.MaximumCount is { } upper)
                maximum = maximum is null ? upper : Math.Min(maximum.Value, upper);
            if (constraint.ReadRoleIds.Count > 0)
            {
                if (roles is null) roles = new(constraint.ReadRoleIds, StringComparer.Ordinal);
                else roles.IntersectWith(constraint.ReadRoleIds);
            }
            if (constraint.ValueDomain is not { } next) continue;
            if (domain is null) domain = next;
            else if (domain.LiteralValues is { } left && next.LiteralValues is { } right)
                domain = new(LiteralValues: left.Intersect(right, StringComparer.Ordinal).ToArray());
            else if (domain != next) compatible = false;
        }

        result = new(required, minimum, maximum, roles?.ToArray() ?? [], domain);
        return compatible
            && (maximum is null || maximum >= Math.Max(minimum, required ? 1 : 0))
            && (roles is null || roles.Count > 0)
            && domain?.LiteralValues is not { Count: 0 };
    }
}
