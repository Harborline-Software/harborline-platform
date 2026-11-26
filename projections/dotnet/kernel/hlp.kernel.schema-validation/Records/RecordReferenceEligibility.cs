namespace Harborline.Kernel.SchemaValidation.Records;

/// <summary>The shared write/picker target predicate (DES-0015 records-eng-16, records-auth-34; ADR-0054).</summary>
public static class RecordReferenceEligibility
{
    /// <summary>Admits exactly the declared type or Class, and its required trait, using ordinal identities.</summary>
    public static bool Admits(RecordReferenceDefinition reference, string targetRecordTypeId,
        string? targetClassId, IReadOnlyList<TraitReference>? targetTraits)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var typeTarget = !string.IsNullOrWhiteSpace(reference.TargetTypeId);
        var classTarget = !string.IsNullOrWhiteSpace(reference.TargetClassId);
        if (typeTarget == classTarget || string.IsNullOrWhiteSpace(targetRecordTypeId)) return false;
        if (typeTarget ? !StringComparer.Ordinal.Equals(reference.TargetTypeId, targetRecordTypeId)
            : !StringComparer.Ordinal.Equals(reference.TargetClassId, targetClassId)) return false;
        return reference.RequiredTraitId is null
            || targetTraits?.Any(trait => StringComparer.Ordinal.Equals(trait.TraitId, reference.RequiredTraitId)) == true;
    }
}
