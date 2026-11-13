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
        try
        {
            var budget = request.Series.Definition.Basis is FixedCalendarBasis ? RruleDueOccurrenceSource.CreateBudget(request) : null;
            var refusals = new List<Harborline.Blocks.BuilderDefinitions.DefinitionRefusal>();
            ScheduleDefinitionAdmission.Generation(request.Series.Definition, refusals);
            if (refusals.Count != 0) throw new OccurrenceRefusalException(refusals[0].Code);
            var now = kernelClock.GetUtcNow();
            IDueOccurrenceSource source = request.Series.Definition.Basis switch
            {
                null => new LoneDueOccurrenceSource(),
                FixedCalendarBasis => new RruleDueOccurrenceSource(producer, budget),
                CompletionOffsetBasis => new CompletionOffsetDueOccurrenceSource(),
                ThresholdBasis => new ThresholdDueOccurrenceSource(),
                EventAnchoredChainBasis => new RelativeChainDueOccurrenceSource(),
                _ => throw new OccurrenceRefusalException("scheduling.occurrence.basis_invalid"),
            };
            return source.DeriveDue(request, now).OrderBy(o => o.Due).ThenBy(o => o.OccurrenceId, StringComparer.Ordinal).ToArray();
        }
        catch (RruleExpansionBudgetExceededException exception) { throw RruleDueOccurrenceSource.BudgetRefusal(exception); }
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
public sealed class RruleDueOccurrenceSource(IRruleExpansionService rrule, RruleExpansionBudget? budget = null) : IDueOccurrenceSource
{
    private const int OccurrenceLimit = 1_000;

    internal static RruleExpansionBudget CreateBudget(DueOccurrenceRequest request)
    {
        var series = request.Series;
        var zone = WallClockResolver.IsKnownZone(series.Timezone!) ? series.Timezone! : "UTC";
        var from = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.From, zone));
        var to = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.To, zone));
        return new(((FixedCalendarBasis)series.Definition.Basis!).Rrule ?? string.Empty,
            DateOnly.FromDateTime(series.LocalStart ?? DateTime.MinValue),
            from > DateOnly.MinValue ? from.AddDays(-1) : from, to < DateOnly.MaxValue ? to.AddDays(1) : to);
    }

    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant)
    {
        var series = request.Series;
        if (series.LocalStart is not { } start || series.Timezone is not { } zone || !WallClockResolver.IsKnownZone(zone))
            throw new OccurrenceRefusalException("scheduling.occurrence.series_start_and_zone_required");
        var basis = (FixedCalendarBasis)series.Definition.Basis!;
        var from = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.From, zone));
        var to = DateOnly.FromDateTime(WallClockResolver.ToLocalDateTime(request.To, zone));
        if (to.DayNumber - from.DayNumber >= 3_660)
            throw new OccurrenceRefusalException("scheduling.occurrence.window_maximum_exceeded");
        if (from > DateOnly.MinValue) from = from.AddDays(-1);
        if (to < DateOnly.MaxValue) to = to.AddDays(1);
        var time = TimeOnly.FromDateTime(start);
        var work = budget ?? CreateBudget(request);
        var end = to;
        var relevantOverrides = new HashSet<DateTime>();
        foreach (var (original, moved) in series.Overrides ?? new Dictionary<DateTime, DateTime>())
        {
            work.Charge();
            if (original < start || TimeOnly.FromDateTime(original) != time) continue;
            var due = WallClockResolver.ToUtcInstant(DateOnly.FromDateTime(moved), TimeOnly.FromDateTime(moved), zone);
            if (!OccurrenceTime.InRange(request, due)) continue;
            relevantOverrides.Add(original);
            var date = DateOnly.FromDateTime(original);
            if (date > end) end = date;
        }
        work.RequestedRangeEnd = end;
        var result = new List<SchedulingOccurrence>();
        void Visit(DateOnly date)
        {
            var original = date.ToDateTime(time);
            if ((date < from || date > to) && !relevantOverrides.Contains(original)) return;
            if (series.ExceptionDates?.Contains(original) == true) return;
            var local = series.Overrides is { } overrides && overrides.TryGetValue(original, out var moved) ? moved : original;
            var due = WallClockResolver.ToUtcInstant(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), zone);
            if (!OccurrenceTime.InRange(request, due)) return;
            if (result.Count == OccurrenceLimit) throw new OccurrenceRefusalException("scheduling.occurrence.occurrence_cap_exceeded");
            var identity = OccurrenceTime.Key(series.SeriesId, original.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture));
            result.Add(OccurrenceTime.Create(request, kernelInstant, identity, due, series.Definition.Window, series.Definition.Tolerance, original));
        }
        try { rrule.VisitZonedOccurrences(basis.Rrule, DateOnly.FromDateTime(start), time, zone, end, from, Visit, budget: work); }
        catch (RruleExpansionBudgetExceededException exception)
        {
            throw BudgetRefusal(exception);
        }
        return result;
    }

    internal static OccurrenceRefusalException BudgetRefusal(RruleExpansionBudgetExceededException exception)
    {
        var refusal = new OccurrenceRefusalException(exception.Code);
        refusal.Data["RecurrenceId"] = exception.RecurrenceId;
        refusal.Data["Anchor"] = exception.Anchor;
        refusal.Data["RequestedRangeStart"] = exception.RequestedRangeStart;
        refusal.Data["RequestedRangeEnd"] = exception.RequestedRangeEnd;
        refusal.Data["CandidateLimit"] = exception.CandidateLimit;
        refusal.Data["CandidatesExamined"] = exception.CandidatesExamined;
        refusal.Data["LastEvaluatedOccurrence"] = exception.LastEvaluatedOccurrence;
        refusal.Data["Bound"] = exception.Bound;
        return refusal;
    }
}
