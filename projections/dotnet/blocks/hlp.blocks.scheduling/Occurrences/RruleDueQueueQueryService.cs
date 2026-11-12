using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Scheduling;
using Harborline.Kernel.Core;

namespace Harborline.Blocks.Scheduling;

/// <summary>Dispatches all concern-one generators under one kernel clock.</summary>
public sealed class RruleDueQueueQueryService(IRruleExpansionService rrule, KernelClock clock) : IDueQueueQueryService
{
    private readonly IRruleExpansionService producer = rrule ?? throw new ArgumentNullException(nameof(rrule));
    private readonly KernelClock kernelClock = clock ?? throw new ArgumentNullException(nameof(clock));
    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> Query(DueOccurrenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Series);
        ArgumentNullException.ThrowIfNull(request.Series.Definition);
        ArgumentNullException.ThrowIfNull(request.Completions);
        ArgumentNullException.ThrowIfNull(request.Readings);
        if (request.To < request.From) throw new ArgumentException("Query end precedes start.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Series.SeriesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Series.SubjectRef);
        var refusals = new List<Harborline.Blocks.BuilderDefinitions.DefinitionRefusal>();
        ScheduleDefinitionAdmission.Generation(request.Series.Definition, refusals);
        if (refusals.Count != 0) throw new OccurrenceRefusalException(refusals[0].Code);
        var now = kernelClock.GetUtcNow();
        IDueOccurrenceSource source = request.Series.Definition.Basis switch
        {
            null => new LoneDueOccurrenceSource(),
            FixedCalendarBasis => new RruleDueOccurrenceSource(producer),
            CompletionOffsetBasis => new CompletionOffsetDueOccurrenceSource(),
            ThresholdBasis => new ThresholdDueOccurrenceSource(),
            EventAnchoredChainBasis => new RelativeChainDueOccurrenceSource(),
            _ => throw new OccurrenceRefusalException("scheduling.occurrence.basis_invalid"),
        };
        try { return source.DeriveDue(request, now).OrderBy(o => o.Due).ThenBy(o => o.OccurrenceId, StringComparer.Ordinal).ToArray(); }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw new OccurrenceRefusalException("scheduling.occurrence.date_arithmetic_overflow");
        }
    }
}

/// <summary>A no-basis schedule emits its own authored due instant; the window never supplies it.</summary>
public sealed class LoneDueOccurrenceSource : IDueOccurrenceSource
{
    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant)
    {
        var definition = request.Series.Definition;
        var due = ScheduleDefinitionAdmission.Instant(definition.DueDate)
            ?? throw new OccurrenceRefusalException("scheduling.definition.due_date_invalid");
        return OccurrenceTime.InRange(request, due)
            ? [OccurrenceTime.Create(request, kernelInstant, OccurrenceTime.Key(request.Series.SeriesId, "lone"), due, definition.Window, definition.Tolerance)] : [];
    }
}

/// <summary>The fixed-calendar generator owns exclusions and overrides above the shared RRULE producer.</summary>
public sealed class RruleDueOccurrenceSource(IRruleExpansionService rrule) : IDueOccurrenceSource
{
    private const int OccurrenceLimit = 1_000;

    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant)
    {
        var series = request.Series;
        if (series.LocalStart is not { } start || series.Timezone is not { } zone || !WallClockResolver.IsKnownZone(zone))
            throw new OccurrenceRefusalException("scheduling.occurrence.series_start_and_zone_required");
        var basis = (FixedCalendarBasis)series.Definition.Basis!;
        var from = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.From, zone));
        var to = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.To, zone));
        if (from > DateOnly.MinValue) from = from.AddDays(-1);
        if (to < DateOnly.MaxValue) to = to.AddDays(1);
        var time = TimeOnly.FromDateTime(start);
        IReadOnlyList<DateOnly> Expand(DateOnly first, DateOnly last) =>
            rrule.ExpandZonedOccurrences(basis.Rrule, DateOnly.FromDateTime(start), time, zone, last, 0, 0, first);
        IEnumerable<DateTime> Originals()
        {
            // The bounded subset emits at most one date per day; paging keeps padding out of the result budget.
            for (var first = from; first <= to;)
            {
                var last = DateOnly.FromDayNumber(Math.Min(to.DayNumber, first.DayNumber + OccurrenceLimit - 1));
                foreach (var date in Expand(first, last)) yield return date.ToDateTime(time);
                if (last == to) break;
                first = last.AddDays(1);
            }
            // An override can move an original from outside the expanded range into the query, keeping its identity.
            foreach (var original in (series.Overrides?.Keys ?? Enumerable.Empty<DateTime>()).Order())
            {
                if (original < start || TimeOnly.FromDateTime(original) != time) continue;
                var date = DateOnly.FromDateTime(original);
                if (date >= from && date <= to) continue;
                if (Expand(date, date).Contains(date)) yield return original;
            }
        }
        var result = new List<SchedulingOccurrence>();
        foreach (var original in Originals())
        {
            if (series.ExceptionDates?.Contains(original) == true) continue;
            var local = series.Overrides is { } overrides && overrides.TryGetValue(original, out var moved) ? moved : original;
            var due = WallClockResolver.ToUtcInstant(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), zone);
            if (!OccurrenceTime.InRange(request, due)) continue;
            if (result.Count == OccurrenceLimit) throw new OccurrenceRefusalException("scheduling.occurrence.occurrence_cap_exceeded");
            var identity = OccurrenceTime.Key(series.SeriesId, original.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture));
            result.Add(OccurrenceTime.Create(request, kernelInstant, identity, due, series.Definition.Window, series.Definition.Tolerance, original));
        }
        return result;
    }
}
