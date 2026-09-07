namespace Harborline.UIAdapters.Blazor.Components.Forms;

internal sealed record SchemaFormProjection(
    IReadOnlyList<SchemaFormSection> Sections,
    IReadOnlyDictionary<string, object?> ComputedValues);

internal static class SchemaFormRuleProjector
{
    public static SchemaFormProjection Project(SchemaFormView view, SchemaFormRuleEvaluation? evaluation)
    {
        if (evaluation is null) return new(view.Sections, new Dictionary<string, object?>(StringComparer.Ordinal));
        var computed = evaluation.Values
            .Where(pair => pair.Key.StartsWith("field:", StringComparison.Ordinal)
                && pair.Value.State == SchemaFormRuleValueState.Resolved)
            .ToDictionary(pair => pair.Key["field:".Length..], pair => pair.Value.Value, StringComparer.Ordinal);
        var sections = new List<SchemaFormSection>(view.Sections.Count);
        foreach (var section in view.Sections)
        {
            if (evaluation.Visibility.TryGetValue($"section:{section.Id}", out var sectionState)
                && !sectionState.Visible) continue;
            var fields = section.Fields.Select(field => ProjectField(field, evaluation)).OfType<SchemaFormField>().ToArray();
            var items = section.Items is null ? null : ProjectItems(section.Items, evaluation);
            if (FieldsUnchanged(section.Fields, fields) && ReferenceEquals(items, section.Items)) sections.Add(section);
            else sections.Add(section with { Fields = fields, Items = items });
        }

        return new(sections, computed);
    }

    private static SchemaFormField? ProjectField(SchemaFormField field, SchemaFormRuleEvaluation evaluation)
    {
        evaluation.Visibility.TryGetValue($"field:{field.Name}", out var visibility);
        if (visibility is not null && !visibility.Visible) return null;
        evaluation.Presentations.TryGetValue($"field:{field.Name}", out var presentation);
        var required = field.Required || visibility?.Required == true;
        var readOnly = field.ReadOnly || visibility?.ReadOnly == true;
        if (required == field.Required && readOnly == field.ReadOnly && presentation is null) return field;
        return field with
        {
            Required = required,
            ReadOnly = readOnly,
            Presentation = presentation ?? field.Presentation,
        };
    }

    private static IReadOnlyList<SchemaFormItem> ProjectItems(
        IReadOnlyList<SchemaFormItem> source,
        SchemaFormRuleEvaluation evaluation)
    {
        List<SchemaFormItem>? changed = null;
        for (var index = 0; index < source.Count; index++)
        {
            var item = source[index];
            SchemaFormItem? projected = item switch
            {
                SchemaFormFieldItem fieldItem => ProjectField(fieldItem.Field, evaluation) is { } field
                    ? ReferenceEquals(field, fieldItem.Field) ? fieldItem : fieldItem with { Field = field }
                    : null,
                SchemaFormGroupItem group => ProjectChildren(group, evaluation),
                SchemaFormCollectionItem collection => ProjectChildren(collection, evaluation),
                _ => item,
            };
            if (projected is null || !ReferenceEquals(projected, item))
            {
                changed ??= source.Take(index).ToList();
            }

            if (changed is not null && projected is not null) changed.Add(projected);
        }

        return changed ?? source;
    }

    private static SchemaFormGroupItem ProjectChildren(
        SchemaFormGroupItem group,
        SchemaFormRuleEvaluation evaluation)
    {
        var items = ProjectItems(group.Items, evaluation);
        return ReferenceEquals(items, group.Items) ? group : group with { Items = items };
    }

    private static SchemaFormCollectionItem ProjectChildren(
        SchemaFormCollectionItem collection,
        SchemaFormRuleEvaluation evaluation)
    {
        var items = ProjectItems(collection.Items, evaluation);
        return ReferenceEquals(items, collection.Items) ? collection : collection with { Items = items };
    }

    private static bool FieldsUnchanged(
        IReadOnlyList<SchemaFormField> previous,
        IReadOnlyList<SchemaFormField> next) =>
        previous.Count == next.Count && previous.Zip(next).All(pair => ReferenceEquals(pair.First, pair.Second));
}
