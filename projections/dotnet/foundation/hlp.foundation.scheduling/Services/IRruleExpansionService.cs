namespace Harborline.Foundation.Scheduling;

/// <summary>
/// Expand an RFC 5545 RRULE string into a concrete sequence of due
/// dates within a configurable window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fleet bounded RRULE subset</b> — the subset below is the contract (DES-0057 <c>recurrence-expansion-ck-1</c>),
/// checked in front of the engine. Expansion runs on Ical.Net behind this interface (ADR-0122, the owner's ruling of
/// 2026-11-07), so occurrences follow RFC 5545: an invalid date such as 31 February is skipped and not counted, a numeric
/// BYDAY under YEARLY without BYMONTH is an offset within the year (Errata 1913), and a timed series' UTC UNTIL bounds it
/// at that instant. The web tier's own expander is not bound to this one (ADR-0122 decision 4, T-1092).
/// </para>
/// <para>
/// <b>Supported subset:</b>
/// <list type="bullet">
///   <item><description><c>FREQ</c> — DAILY | WEEKLY | MONTHLY | YEARLY</description></item>
///   <item><description><c>INTERVAL=N</c> — positive integer (default 1)</description></item>
///   <item><description><c>COUNT=N</c> or <c>UNTIL</c> (<c>YYYYMMDD</c> or <c>YYYYMMDDTHHMMSSZ</c>); both in one rule refuses (RFC 5545 §3.3.10)</description></item>
///   <item><description><c>BYDAY</c> — weekday list e.g. <c>MO,WE,FR</c>; under MONTHLY or YEARLY with an ordinal e.g. <c>1MO</c>, <c>-1FR</c> (an ordinal under DAILY or WEEKLY refuses)</description></item>
///   <item><description><c>BYMONTHDAY</c> — calendar day 1–28 (capped at 28 to avoid month-end ambiguity across February and 30-day months)</description></item>
///   <item><description><c>BYMONTH</c> — month number 1–12</description></item>
///   <item><description>Hard occurrence cap: 1 000 occurrences per call to prevent runaway expansion</description></item>
/// </list>
/// </para>
/// <para>
/// Unsupported RRULE components (EXDATE, BYWEEKNO, BYYEARDAY, etc.)
/// are refused so an authored restriction cannot be silently discarded. Part names and values are case-insensitive
/// (RFC 5234 §2.3, RFC 5545 §3.1), and a part given twice refuses (RFC 5545 §3.3.10).
/// </para>
/// <para>
/// <b>Completeness</b> — occurrences are returned only when every occurrence relevant to the
/// requested range has been evaluated. A walk that reaches the 1 000-occurrence cap with range
/// still to cover refuses with <see cref="RruleExpansionCapExceededException"/>; it never
/// returns a partial list, because a caller cannot distinguish one from a complete result
/// (DES-0057 §10 ruling 3).
/// The producer also refuses with <see cref="RruleExpansionBudgetExceededException"/> past 50 000 work units,
/// counting each recurrence period (including empty periods) and each matching date before caller filtering.
/// </para>
/// </remarks>
public interface IRruleExpansionService
{
    /// <summary>
    /// Visits a timed series once from its anchor through end, including dates before requestedRangeStart.
    /// The caller owns filtering and the final result cap. Work counts every recurrence period, including empty ones,
    /// and every matching date before caller filtering. A budget refusal aborts the query; callbacks are provisional.
    /// Earlier implementations refuse rather than silently falling back to an unbounded or capped walk.
    /// </summary>
    /// <exception cref="RruleExpansionBudgetExceededException">The complete walk exceeds candidateLimit.</exception>
    void VisitZonedOccurrences(string rrule, DateOnly start, TimeOnly startTime, string timezone,
        DateOnly end, DateOnly requestedRangeStart, Action<DateOnly> visit, int candidateLimit = 50_000) =>
        throw new NotSupportedException($"{GetType().Name} does not support budgeted timed visits.");

    /// <summary>
    /// Return all occurrence dates that fall within the configured
    /// window per the parsed <paramref name="rrule"/>.
    /// </summary>
    /// <param name="rrule">
    /// RFC 5545 RRULE string (e.g. <c>FREQ=MONTHLY;BYMONTHDAY=1</c>).
    /// </param>
    /// <param name="start">Recurrence anchor / DTSTART date.</param>
    /// <param name="end">
    /// Optional hard upper bound (e.g. <c>RecurringSchedule.EndsOn</c>).
    /// When <see langword="null"/>, the horizon is
    /// <c>today + <paramref name="lookaheadDays"/></c>.
    /// </param>
    /// <param name="lookaheadDays">
    /// Soft upper bound (days from <paramref name="today"/>) used when
    /// <paramref name="end"/> is <see langword="null"/>.
    /// </param>
    /// <param name="leadDays">
    /// Skip occurrences that fall earlier than
    /// <c>today + leadDays</c>. Enables "generate work N days ahead"
    /// semantics without changing the recurrence anchor.
    /// </param>
    /// <param name="today">
    /// Wall-clock today. Callers inject this so unit tests are
    /// deterministic without mocking <c>DateTime.UtcNow</c>.
    /// </param>
    /// <param name="timezone">
    /// IANA timezone id for the schedule owner (e.g. <c>America/Los_Angeles</c>). This date-valued expansion treats the
    /// series as naive and does not apply it (DES-0057 ck-7); a timed series uses
    /// <see cref="ExpandZonedOccurrences"/>, which does.
    /// </param>
    /// <returns>
    /// Occurrences in ascending date order, filtered by
    /// <paramref name="leadDays"/> and bounded by <paramref name="end"/>
    /// (or <c>today + lookaheadDays</c>). Never exceeds 1 000 items, and never a partial
    /// result: see the completeness remark above.
    /// </returns>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="rrule"/> is missing a <c>FREQ=</c> component, gives a part twice, gives COUNT with
    /// UNTIL, or when an admitted part carries an out-of-bound or malformed value (named in the message).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="rrule"/> is null or whitespace.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="rrule"/> contains a component outside the supported subset.
    /// </exception>
    /// <exception cref="RruleExpansionCapExceededException">
    /// Thrown when the walk reaches the 1 000-occurrence cap before the requested range has been
    /// fully evaluated, so no complete answer exists to return.
    /// </exception>
    IReadOnlyList<DateOnly> ExpandOccurrences(
        string rrule,
        DateOnly start,
        DateOnly? end,
        int lookaheadDays,
        int leadDays,
        DateOnly today,
        string timezone);

    /// <summary>
    /// Expands a timed series anchored at <paramref name="start"/> and <paramref name="startTime"/> in
    /// <paramref name="timezone"/>, and returns each occurrence's local date (ADR-0122 decision 3). A DATE-TIME UNTIL is a
    /// UTC instant that bounds the series inclusively (RFC 5545 §3.3.10), so <c>UNTIL=20261105T040000Z</c> on a 20:00
    /// Los Angeles series ends on 4 November. A DATE UNTIL bounds it by local date. <paramref name="end"/>,
    /// <paramref name="lookaheadDays"/>, <paramref name="leadDays"/> and <paramref name="today"/> bound by local date
    /// exactly as in <see cref="ExpandOccurrences"/>, and the same subset, cap and exceptions apply.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timezone"/> is not an IANA time zone id.</exception>
    /// <exception cref="NotSupportedException">Thrown by implementations written before this member existed.</exception>
    IReadOnlyList<DateOnly> ExpandZonedOccurrences(
        string rrule,
        DateOnly start,
        TimeOnly startTime,
        string timezone,
        DateOnly? end,
        int lookaheadDays,
        int leadDays,
        DateOnly today) =>
        throw new NotSupportedException($"{GetType().Name} does not expand timed series.");
}
