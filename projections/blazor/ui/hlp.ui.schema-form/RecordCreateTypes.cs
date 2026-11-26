namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>The host's create verdict and localized names for a reference target kind.</summary>
public sealed record RecordCreateCapability(string TargetKind, string DisplayName, string IndefiniteName, bool CanCreate);

/// <summary>A request to open the target's form; it does not select or save a value.</summary>
public sealed record RecordCreateRequest(string FieldName, string TargetKind, string Text);
