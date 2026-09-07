namespace Harborline.UIAdapters.Blazor.Components.Scheduling;

public enum GanttZoom { Day, Week, Month }
public enum GanttColumnField { Title, Start, End, Progress }

public sealed record GanttTask(string Id, string Title, DateOnly Start, DateOnly End, double? Progress = null, string? Color = null);
public sealed record GanttDependency(string FromId, string ToId);
public sealed record GanttColumn(GanttColumnField Field, string? Title = null, int? Width = null);
