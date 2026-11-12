using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling;

/// <summary>Classification against the on-time band and, independently, the validity window.</summary>
public enum OccurrenceClassification
{
    /// <summary>Incomplete and not yet due.</summary>
    Pending,
    /// <summary>Within the declared on-time band.</summary>
    OnTime,
    /// <summary>Before the on-time band, inside any validity window.</summary>
    Early,
    /// <summary>After the on-time band, inside any validity window.</summary>
    Late,
    /// <summary>The validity window closed uncompleted, or completion was outside it.</summary>
    Void,
}

/// <summary>A record's binding to a schedule. Fixed-calendar starts and completion-offset first due points live here.</summary>
public sealed record ScheduleSeries(string SeriesId, string SubjectRef, ScheduleDefinition Definition,
    DateTime? LocalStart = null, string? Timezone = null, DateTimeOffset? FirstDue = null,
    IReadOnlySet<DateTime>? ExceptionDates = null, IReadOnlyDictionary<DateTime, DateTime>? Overrides = null);

/// <summary>A completion names the exact occurrence and supplies its actual instant, never its due date.</summary>
public sealed record DueQueueCompletion(string CompletionId, string SeriesId, string OccurrenceId, DateTimeOffset CompletedAt);

/// <summary>A versioned reading supplied by the record; absence is refused.</summary>
public sealed record ScheduleReading(string ReadingId, string SeriesId, string Reading, DateTimeOffset RecordedAt, decimal Value);

/// <summary>The caller selects the query horizon; only the kernel clock decides due and overdue.</summary>
public sealed record DueOccurrenceRequest(ScheduleSeries Series, DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<DueQueueCompletion> Completions, IReadOnlyList<ScheduleReading> Readings,
    RelativeChainExpansionRequest? Chain = null);

/// <summary>One stable occurrence, including its validity window and completion classification.</summary>
public sealed record SchedulingOccurrence(string OccurrenceId, string SeriesId, string SubjectRef,
    DateTimeOffset Due, DateTimeOffset? Earliest, DateTimeOffset? Latest,
    OccurrenceClassification Classification, DateTimeOffset? CompletedAt,
    bool IsDue, TimeSpan Overdue, DateTime? RecurrenceLocal, string? Timezone, DateTime AsOfLocal);

/// <summary>A missing or invalid input is an explicit refusal, never an empty successful query.</summary>
public sealed class OccurrenceRefusalException(string code) : InvalidOperationException(code)
{
    /// <summary>The stable reason.</summary>
    public string Code { get; } = code;
}

/// <summary>Every due generator lives in Scheduling and receives one pinned kernel instant.</summary>
public interface IDueOccurrenceSource
{
    /// <summary>Generate a complete result or refuse, using the supplied kernel instant.</summary>
    IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant);
}

/// <summary>Kernel-clock entry point for all declared bases and the lone due date.</summary>
public interface IDueQueueQueryService
{
    /// <summary>Generate occurrences within the inclusive query horizon.</summary>
    IReadOnlyList<SchedulingOccurrence> Query(DueOccurrenceRequest request);
}
