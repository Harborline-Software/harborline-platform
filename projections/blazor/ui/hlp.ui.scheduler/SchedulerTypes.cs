namespace Harborline.UIAdapters.Blazor.Components.Scheduling;

/// <summary>The time range the scheduler displays: day, week, month, or agenda.</summary>
public enum SchedulerViewType
{
    /// <summary>Shows a single day.</summary>
    Day,
    /// <summary>Shows a week of days.</summary>
    Week,
    /// <summary>Shows a month grid.</summary>
    Month,
    /// <summary>Shows upcoming events as a list.</summary>
    Agenda
}
/// <summary>A day of the week, used to mark which days the scheduler treats as working days.</summary>
public enum SchedulerWorkDay
{
    /// <summary>Counts Sunday as a working day.</summary>
    Sunday,
    /// <summary>Counts Monday as a working day.</summary>
    Monday,
    /// <summary>Counts Tuesday as a working day.</summary>
    Tuesday,
    /// <summary>Counts Wednesday as a working day.</summary>
    Wednesday,
    /// <summary>Counts Thursday as a working day.</summary>
    Thursday,
    /// <summary>Counts Friday as a working day.</summary>
    Friday,
    /// <summary>Counts Saturday as a working day.</summary>
    Saturday
}
/// <summary>One scheduler view: its type and optional title.</summary>
public sealed record SchedulerView(SchedulerViewType Type, string? Title = null);
/// <summary>The working-hours window shaded in day views, as a start and end hour.</summary>
public sealed record SchedulerWorkingHours(int Start, int End);

/// <summary>A calendar event: id, title, start and end, all-day flag, colour and recurrence details.</summary>
public sealed record SchedulerEvent(
    object Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay = false,
    string? Color = null,
    string? Description = null,
    object? Resource = null,
    string? RecurrenceRule = null,
    object? RecurrenceId = null,
    IReadOnlyList<DateTimeOffset>? RecurrenceExceptions = null,
    DateTimeOffset? OriginalStart = null,
    IReadOnlyDictionary<string, object?>? Fields = null);

/// <summary>The names of the source fields that map onto each scheduler event property.</summary>
public sealed record SchedulerModelFields(
    string Id = "id",
    string Title = "title",
    string Start = "start",
    string End = "end",
    string AllDay = "allDay",
    string Description = "description",
    string Color = "color",
    string RecurrenceRule = "recurrenceRule",
    string RecurrenceId = "recurrenceId",
    string RecurrenceExceptions = "recurrenceExceptions",
    string OriginalStart = "originalStart");

/// <summary>Turns raw data items into scheduler events.</summary>
public static class SchedulerModel
{
/// <summary>Builds a scheduler event from a data item, reading each property from the field name mapped for it.</summary>
    public static SchedulerEvent Normalize(IReadOnlyDictionary<string, object?> item, SchedulerModelFields? fields = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        fields ??= new();
        object Required(string key) => item.TryGetValue(key, out var value) && value is not null ? value : throw new InvalidOperationException("invalid-model-field");
        T? Optional<T>(string key) => item.TryGetValue(key, out var value) && value is T typed ? typed : default;
        var id = Required(fields.Id);
        var title = Required(fields.Title)?.ToString() ?? throw new InvalidOperationException("invalid-model-field");
        var start = ToDate(Required(fields.Start));
        var end = ToDate(Required(fields.End));
        return new(id, title, start, end,
            Optional<bool>(fields.AllDay), Optional<string>(fields.Color), Optional<string>(fields.Description),
            item.TryGetValue("resource", out var resource) ? resource : null,
            Optional<string>(fields.RecurrenceRule), item.TryGetValue(fields.RecurrenceId, out var recurrenceId) ? recurrenceId : null,
            Optional<IReadOnlyList<DateTimeOffset>>(fields.RecurrenceExceptions), Optional<DateTimeOffset?>(fields.OriginalStart),
            new Dictionary<string, object?>(item));
    }

    private static DateTimeOffset ToDate(object value) => value switch
    {
        DateTimeOffset date => date,
        DateTime date => new(date),
        string text when DateTimeOffset.TryParse(text, out var date) => date,
        _ => throw new InvalidOperationException("invalid-model-field"),
    };
}
