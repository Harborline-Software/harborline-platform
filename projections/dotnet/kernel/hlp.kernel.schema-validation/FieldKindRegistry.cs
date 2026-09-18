namespace Harborline.Kernel.SchemaValidation;

/// <summary>The immutable field-kind revisions admitted by the composing platform runtime.</summary>
public sealed class FieldKindRegistry
{
    private readonly Dictionary<(string KindId, string Version), AdmittedFieldKind> _kinds;

    /// <summary>Registers exact kind/version identities without implicit or latest-version fallback.</summary>
    public FieldKindRegistry(IEnumerable<AdmittedFieldKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds.ToDictionary(kind => (kind.KindId, kind.Version));
    }

    internal AdmittedFieldKind Resolve(FieldKindReference reference)
        => _kinds[(reference.KindId, reference.Version)];

    internal IReadOnlyList<RecordsRefusal> Validate(RecordTypeDefinition definition)
        => definition.Fields
            .Select((field, index) => (field, index))
            .Where(item => !_kinds.ContainsKey((item.field.Kind.KindId, item.field.Kind.Version)))
            .Select(item => new RecordsRefusal(
                "records.field.kind_unresolved",
                $"/fields/{item.index}/kind",
                "The field kind and version are not admitted."))
            .ToArray();
}
