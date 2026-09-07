namespace Harborline.UIAdapters.Blazor.Components.Scheduling;

public enum SchedulerViewType { Day, Week, Month, Agenda }
public enum SchedulerWorkDay { Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday }
public sealed record SchedulerView(SchedulerViewType Type, string? Title = null);
public sealed record SchedulerWorkingHours(int Start, int End);

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

public static class SchedulerModel
{
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
