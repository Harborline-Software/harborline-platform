using Harborline.Contracts.Fields;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>The immutable kind revisions admitted by the composing platform runtime.</summary>
public sealed class FieldKindRegistry
{
    private readonly Dictionary<(string KindId, string Version), AdmittedFieldKind> _kinds = [];

    /// <summary>Registers exact identities without implicit or latest-version fallback.</summary>
    public FieldKindRegistry(IEnumerable<AdmittedFieldKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        foreach (var kind in kinds)
        {
            if (kind is null || string.IsNullOrWhiteSpace(kind.KindId) || string.IsNullOrWhiteSpace(kind.Version)
                || !Enum.IsDefined(kind.ValueShape))
                throw new FieldAdmissionException([new("field.kind_registration_invalid", "",
                    "A kind registration requires an identity, version and supported scalar shape.")]);

            if (!_kinds.TryAdd((kind.KindId, kind.Version), kind))
                throw new FieldAdmissionException([new("field.kind_registration_duplicate", "",
                    "A kind revision can only be registered once.")]);
        }
    }

    /// <summary>Resolves the exact admitted kind revision or refuses at the authored location.</summary>
    public AdmittedFieldKind Resolve(FieldKindReference reference, string jsonPointer)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.KindId is not null && reference.Version is not null
            && _kinds.TryGetValue((reference.KindId, reference.Version), out var kind)) return kind;
        throw new FieldAdmissionException([new("field.kind_unresolved", jsonPointer,
            "The field kind and version are not admitted.")]);
    }
}
