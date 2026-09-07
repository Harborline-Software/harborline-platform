using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms;

internal static class FormDefinitionAuthoringValidation
{
    private static readonly HashSet<string> AllowedCodes = new(StringComparer.Ordinal)
    {
        "required", "minLength", "maxLength", "pattern", "minimum", "maximum",
    };

    public static void ValidateOrThrow(FormDefinition definition)
    {
        if (definition.Authoring is null)
        {
            return;
        }

        var expected = definition.Overlay.Fields.Keys.ToHashSet(StringComparer.Ordinal);
        var actual = definition.Authoring.Fields.Keys.ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual))
        {
            throw new FormDefinitionValidationException(
                definition.Id,
                "authoring fields must exactly match the overlay field registry.");
        }

        foreach (var (field, metadata) in definition.Authoring.Fields)
        {
            if (string.IsNullOrWhiteSpace(metadata.Type))
            {
                throw new FormDefinitionValidationException(definition.Id, $"authoring field '{field}' has no type.");
            }

            var validations = metadata.Validations;
            if (validations is not null)
            {
                var duplicateCode = validations
                    .GroupBy(item => item.Code, StringComparer.Ordinal)
                    .FirstOrDefault(group => group.Count() > 1)?.Key;
                if (duplicateCode is not null)
                {
                    throw new FormDefinitionValidationException(
                        definition.Id,
                        $"authoring field '{field}' repeats validation '{duplicateCode}'.");
                }

                foreach (var validation in validations)
                {
                    if (!AllowedCodes.Contains(validation.Code))
                    {
                        throw new FormDefinitionValidationException(
                            definition.Id,
                            $"authoring field '{field}' uses unknown validation '{validation.Code}'.");
                    }
                }

                var required = validations.Any(item => item.Code == "required");
                if (required != metadata.Required)
                {
                    throw new FormDefinitionValidationException(
                        definition.Id,
                        $"authoring field '{field}' has inconsistent required metadata.");
                }
            }

            if (metadata.Options is { } options && options.Count != options.Distinct(StringComparer.Ordinal).Count())
            {
                throw new FormDefinitionValidationException(
                    definition.Id,
                    $"authoring field '{field}' contains duplicate option values.");
            }
        }
    }
}
