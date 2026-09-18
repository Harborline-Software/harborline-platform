namespace Harborline.Kernel.SchemaValidation;

/// <summary>The immutable field-kind revisions admitted by the composing platform runtime.</summary>
public sealed class FieldKindRegistry
{
    private readonly Dictionary<(string KindId, string Version), AdmittedFieldKind> _kinds;

    /// <summary>Registers exact kind/version identities without implicit or latest-version fallback.</summary>
    public FieldKindRegistry(IEnumerable<AdmittedFieldKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var registrations = kinds.ToArray();
        if (registrations.Any(kind => !Enum.IsDefined(kind.ValueShape)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kinds),
                "Every admitted field-kind revision must declare a supported scalar value shape.");
        }

        _kinds = registrations.ToDictionary(kind => (kind.KindId, kind.Version));
    }

    internal AdmittedFieldKind Resolve(FieldKindReference reference)
        => _kinds[(reference.KindId, reference.Version)];

    internal IReadOnlyList<RecordsRefusal> Validate(RecordTypeDefinition definition)
    {
        var refusals = new List<RecordsRefusal>();
        for (var index = 0; index < definition.Fields.Count; index++)
        {
            var field = definition.Fields[index];
            if (!_kinds.TryGetValue((field.Kind.KindId, field.Kind.Version), out var kind))
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.kind_unresolved",
                    $"/fields/{index}/kind",
                    "The field kind and version are not admitted."));
                continue;
            }

            if (field.IsTranslatable && kind.ValueShape != FieldScalarValueShape.Text)
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.translatable_shape_incompatible",
                    $"/fields/{index}/is_translatable",
                    "A translatable field kind must produce text values."));
            }

            if (field.Reference is not null && kind.ValueShape != FieldScalarValueShape.Text)
            {
                refusals.Add(new RecordsRefusal(
                    "records.field.reference_shape_incompatible",
                    $"/fields/{index}/reference",
                    "A reference field kind must produce text values."));
            }
        }

        return refusals;
    }
}
