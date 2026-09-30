namespace Harborline.UIAdapters.Blazor.Components.Scheduling;

/// <summary>The time scale of the Gantt timeline.</summary>
public enum GanttZoom
{
    /// <summary>Scales the timeline so each column is one day.</summary>
    Day,
    /// <summary>Scales the timeline so each column is one week.</summary>
    Week,
    /// <summary>Scales the timeline so each column is one month.</summary>
    Month
}
/// <summary>Which task field a Gantt list column shows.</summary>
public enum GanttColumnField
{
    /// <summary>Shows the task title in the column.</summary>
    Title,
    /// <summary>Shows the task start date in the column.</summary>
    Start,
    /// <summary>Shows the task end date in the column.</summary>
    End,
    /// <summary>Shows the task completion progress in the column.</summary>
    Progress
}

/// <summary>One Gantt task: id, title, start and end dates, optional progress and colour.</summary>
public sealed record GanttTask(string Id, string Title, DateOnly Start, DateOnly End, double? Progress = null, string? Color = null);
/// <summary>A dependency between two Gantt tasks: the task it starts from and the task it leads to.</summary>
public sealed record GanttDependency(string FromId, string ToId);
/// <summary>A Gantt table column: the task field it shows, an optional title and an optional width.</summary>
public sealed record GanttColumn(GanttColumnField Field, string? Title = null, int? Width = null);
