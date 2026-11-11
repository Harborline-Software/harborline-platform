using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Scheduling;
using Harborline.Kernel.Core;

namespace Harborline.Blocks.Scheduling;

/// <summary>Dispatches all concern-one generators under one kernel clock.</summary>
public sealed class RruleDueQueueQueryService(IRruleExpansionService rrule, KernelClock clock) : IDueQueueQueryService
{
    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> Query(DueOccurrenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.To < request.From) throw new ArgumentException("Query end precedes start.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Series.SeriesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Series.SubjectRef);
        var now = clock.GetUtcNow();
        IDueOccurrenceSource source = request.Series.Definition.Basis switch
        {
            null => new LoneDueOccurrenceSource(),
            FixedCalendarBasis => new RruleDueOccurrenceSource(rrule),
            CompletionOffsetBasis => new CompletionOffsetDueOccurrenceSource(),
            ThresholdBasis => new ThresholdDueOccurrenceSource(),
            EventAnchoredChainBasis => new RelativeChainDueOccurrenceSource(),
            _ => throw new OccurrenceRefusalException("scheduling.occurrence.basis_invalid"),
        };
        return source.DeriveDue(request, now).OrderBy(o => o.Due).ThenBy(o => o.OccurrenceId, StringComparer.Ordinal).ToArray();
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
    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant)
    {
        var series = request.Series;
        if (series.LocalStart is not { } start || series.Timezone is not { } zone || !WallClockResolver.IsKnownZone(zone))
            throw new OccurrenceRefusalException("scheduling.occurrence.series_start_and_zone_required");
        var basis = (FixedCalendarBasis)series.Definition.Basis!;
        var from = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.From, zone));
        var to = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.To, zone));
        var time = TimeOnly.FromDateTime(start);
        var dates = rrule.ExpandZonedOccurrences(basis.Rrule, DateOnly.FromDateTime(start), time, zone, to, 0, 0, from);
        var originals = dates.Select(d => d.ToDateTime(time)).ToHashSet();
        // An override can move an original from outside the query horizon into it, retaining the original identity.
        foreach (var original in (series.Overrides ?? new Dictionary<DateTime, DateTime>()).Keys)
        {
            if (original < start || TimeOnly.FromDateTime(original) != time) continue;
            var date = DateOnly.FromDateTime(original);
            if (rrule.ExpandZonedOccurrences(basis.Rrule, DateOnly.FromDateTime(start), time, zone, date, 0, 0, date).Contains(date))
                originals.Add(original);
        }
        var result = new List<SchedulingOccurrence>();
        foreach (var original in originals.Order())
        {
            if (series.ExceptionDates?.Contains(original) == true) continue;
            var local = series.Overrides is { } overrides && overrides.TryGetValue(original, out var moved) ? moved : original;
            var due = WallClockResolver.ToUtcInstant(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), zone);
            if (!OccurrenceTime.InRange(request, due)) continue;
            var identity = OccurrenceTime.Key(series.SeriesId, original.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture));
            result.Add(OccurrenceTime.Create(request, kernelInstant, identity, due, series.Definition.Window, series.Definition.Tolerance, original));
        }
        return result;
    }
}
