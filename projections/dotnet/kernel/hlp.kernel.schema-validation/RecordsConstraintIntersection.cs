using Harborline.Contracts.Fields;

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
}
