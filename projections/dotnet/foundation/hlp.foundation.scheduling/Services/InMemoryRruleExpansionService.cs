using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using NodaTime;

namespace Harborline.Foundation.Scheduling;

/// <summary>
/// The in-process <see cref="IRruleExpansionService"/>. Expansion runs on Ical.Net (MIT) behind this interface, and the
/// bounded subset (DES-0057 <c>recurrence-expansion-ck-1</c>) stays the contract as a validator in front of it
/// (ADR-0122, the owner's ruling of 2026-11-07; R-0146 call 5).
/// </summary>
/// <remarks>
/// <para>
/// Ical.Net gives RFC 5545's answers where the hand-written walker this replaces did not (R-0146 call 5): an invalid date
/// such as 31 February is skipped and not counted (§3.3.10), a numeric BYDAY under YEARLY without BYMONTH is an offset
/// within the year (Errata 1913), and a zoned series' UTC UNTIL bounds it at that instant (§3.3.10). Ical.Net accepts a
/// repeated FREQ and a numeric BYDAY under WEEKLY, which <see cref="RruleSubset"/> refuses first.
/// </para>
/// <para>
/// Completeness is unchanged (DES-0057 §10 ruling 3): a walk that would return more than 1 000 occurrences in the
/// requested range refuses with <see cref="RruleExpansionCapExceededException"/> instead of returning a partial list.
/// </para>
/// </remarks>
public sealed class InMemoryRruleExpansionService : IRruleExpansionService
{
    /// <summary>Hard cap on occurrences per call, matching the ui-react <c>expandRecurrence</c> 1 000-occurrence guard.</summary>
    private const int OccurrenceCap = 1_000;

    /// <inheritdoc />
    public IReadOnlyList<DateOnly> ExpandOccurrences(
        string rrule,
        DateOnly start,
        DateOnly? end,
        int lookaheadDays,
        int leadDays,
        DateOnly today,
        string timezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rrule);
        var parsed = RruleSubset.Parse(rrule);

        // A date-valued series (RFC 5545 DTSTART;VALUE=DATE): UNTIL bounds by date. A DATE-TIME UNTIL keeps its UTC date
        // here, as DES-0057 ck-7's date-granular contract always has; a timed series uses ExpandZonedOccurrences instead.
        var occurrences = Evaluate(parsed, new CalDateTime(start)).Select(occurrence => occurrence.Date);
        return Collect(rrule, parsed, start, occurrences, end ?? today.AddDays(lookaheadDays), today.AddDays(leadDays),
            static (date, until) => date > until.Date);
    }

    /// <inheritdoc />
    public IReadOnlyList<DateOnly> ExpandZonedOccurrences(
        string rrule,
        DateOnly start,
        TimeOnly startTime,
        string timezone,
        DateOnly? end,
        int lookaheadDays,
        int leadDays,
        DateOnly today)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rrule);
        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone) is null)
            throw new ArgumentException($"'{timezone}' is not an IANA time zone id.", nameof(timezone));
        var parsed = RruleSubset.Parse(rrule);

        // A timed series: the occurrence's nominal local date is what callers key on (EXDATE, RECURRENCE-ID), and a
        // DATE-TIME UNTIL is a UTC instant that bounds the series inclusively (RFC 5545 §3.3.10). A DATE UNTIL on a timed
        // series keeps bounding by local date. The rule is evaluated on the floating wall clock, as RFC 5545 computes
        // recurrences in local time, and each nominal time resolves to its instant through the one resolver. Evaluating in
        // the zone instead lets the engine shift a spring-forward gap time past midnight (23:30 on 28 March 2026 in Nuuk
        // became 00:30 on the 29th), which would change the occurrence's date.
        var dated = Evaluate(parsed, new CalDateTime(start, startTime, null))
            .Select(occurrence => (occurrence.Date, Instant: WallClockResolver.ToUtcInstant(occurrence.Date, startTime, timezone)));
        var bounded = parsed.Until is { Instant: { } untilInstant }
            ? dated.TakeWhile(occurrence => occurrence.Instant <= untilInstant)
            : dated;
        return Collect(rrule, parsed, start, bounded.Select(occurrence => occurrence.Date),
            end ?? today.AddDays(lookaheadDays), today.AddDays(leadDays),
            static (date, until) => until.Instant is null && date > until.Date);
    }

    /// <summary>
    /// The most frequency steps the engine may take without a match before the rule is refused. The engine's enumeration
    /// is unbounded, and a subset rule that can never match (a fifth Monday that must also be the 1st; a weekly Monday
    /// every seven days from a Tuesday) would otherwise never yield, so the horizon check below would never run. The
    /// sparsest subset rule that does match, a fifth Monday in February, needs about 336 monthly steps, and a daily
    /// BYMONTH, BYMONTHDAY and BYDAY intersection under about 4 100 daily steps.
    /// </summary>
    private const int UnmatchedStepLimit = 10_000;

    private static IEnumerable<CalDateTime> Evaluate(ParsedRrule parsed, CalDateTime dtStart)
    {
        using var periods = new RecurrencePatternEvaluator(new RecurrenceRule(parsed.ToRuleWithoutUntil()))
            .Evaluate(dtStart, null, new EvaluationOptions { MaxUnmatchedIncrementsLimit = UnmatchedStepLimit })
            .GetEnumerator();
        while (true)
        {
            try
            {
                if (!periods.MoveNext())
                    yield break;
            }
            catch (EvaluationLimitExceededException exception)
            {
                throw new FormatException(
                    $"RRULE '{parsed.ToRuleWithoutUntil()}' produces no further occurrence within {UnmatchedStepLimit} steps; "
                    + "its parts cannot all match.", exception);
            }
            catch (EvaluationOutOfRangeException)
            {
                // The next occurrence would fall after year 9999, which no DateOnly horizon reaches: the series has ended.
                yield break;
            }

            yield return periods.Current.StartTime;
        }
    }

    /// <summary>
    /// Walks ascending occurrences from the anchor. COUNT is applied by the engine and counts from the anchor; UNTIL, the
    /// explicit end, the lookahead horizon and the lead filter bound the walk here, exactly as DES-0057 ck-4 to ck-6 state.
    /// </summary>
    private static List<DateOnly> Collect(
        string rrule,
        ParsedRrule parsed,
        DateOnly start,
        IEnumerable<DateOnly> occurrences,
        DateOnly horizon,
        DateOnly earliest,
        Func<DateOnly, RruleUntil, bool> pastUntil)
    {
        var rangeEnd = parsed.Until is { } bound && bound.Date < horizon ? bound.Date : horizon;
        var result = new List<DateOnly>();
        var examined = 0;
        DateOnly? lastEvaluated = null;

        foreach (var date in occurrences)
        {
            if (date > horizon || (parsed.Until is { } until && pastUntil(date, until)))
                break;
            examined++;
            lastEvaluated = date;
            if (date < earliest)
                continue;

            // DES-0057 §10 ruling 3 (2026-09-20): occurrences may be returned only when every occurrence relevant to the
            // requested range has been evaluated. One more occurrence than the cap admits means the range was not
            // evaluated, so the expansion refuses rather than returning a partial a caller cannot tell from a complete one.
            if (result.Count == OccurrenceCap)
                throw new RruleExpansionCapExceededException(
                    recurrenceId: rrule,
                    anchor: start,
                    requestedRangeStart: earliest,
                    requestedRangeEnd: rangeEnd,
                    candidateLimit: OccurrenceCap,
                    candidatesExamined: examined,
                    lastEvaluatedOccurrence: lastEvaluated,
                    bound: parsed.Until is not null ? RecurrenceBound.Until
                        : parsed.Count is not null ? RecurrenceBound.Count
                        : RecurrenceBound.None);
            result.Add(date);
        }

        return result;
    }
}
