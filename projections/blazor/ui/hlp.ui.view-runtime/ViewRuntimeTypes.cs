namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

public sealed record ViewDefinitionField(string Id, string? Label = null);
public sealed record ViewDefinitionBody(IReadOnlyList<ViewDefinitionField> Fields);
public sealed record ViewDefinition(string Id, string Kind, string Version, ViewDefinitionBody Body, string? PackKey = null);
public sealed record ViewRuntimeRow(string Id, IReadOnlyDictionary<string, object?> Values);
