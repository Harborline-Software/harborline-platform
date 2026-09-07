namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>Ambient metadata shared by a form-field wrapper and its compatible control.</summary>
public sealed record FormFieldContextValue(
    string Id,
    string LabelId,
    string? DescribedBy,
    bool Required,
    bool Disabled);
