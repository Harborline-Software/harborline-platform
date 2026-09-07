using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for <see cref="CalendarEventExpansionService"/> — the local EXDATE + RECURRENCE-ID
/// override layer over the shipped <see cref="IRruleExpansionService"/>, honoring
/// capability-and-workflow-architecture.md §7.1 at the date level (Slice S0).
/// </summary>
public sealed class CalendarEventExpansionServiceTests
{
    private static readonly TenantId Tenant = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();

    private static ICalendarEventExpansionService NewSut()
        => new CalendarEventExpansionService(new InMemoryRruleExpansionService());

    // ----------------------------------------------------------------
    // Recurring event expands to its occurrences
    // ----------------------------------------------------------------

    [Fact]
    public void RecurringEvent_ExpandsToOccurrences()
    {
        // Weekly on Monday, anchored Mon 2026-01-05.
        var anchor = new DateOnly(2026, 1, 5); // Monday
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        // Mondays in Jan 2026: 5, 12, 19, 26 → 4 occurrences.
        Assert.Equal(4, occ.Count);
        Assert.Equal(
            new[] { new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 19), new DateOnly(2026, 1, 26) },
            occ.Select(o => o.Start));
        Assert.All(occ, o => Assert.Equal("Standup", o.Title));
        Assert.All(occ, o => Assert.False(o.IsOverride));
        // RecurrenceId == Start for normal occurrences.
        Assert.All(occ, o => Assert.Equal(o.Start, o.RecurrenceId));
    }

    [Fact]
    public void SingleEvent_YieldsOneOccurrence_WhenInWindow()
    {
        var ev = CalendarEvent.Create(
            Tenant, "Closing", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10), Actor);

        var inWindow = NewSut().Expand(ev, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        Assert.Single(inWindow);
        Assert.Equal(new DateOnly(2026, 3, 10), inWindow[0].Start);

        var outOfWindow = NewSut().Expand(ev, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        Assert.Empty(outOfWindow);
    }

    [Fact]
    public void MultiDaySingleEvent_StraddlingWindowStart_IsKept()
    {
        // Event Jan 28 → Feb 2; window is February. The event's END is in-window even though its
        // START precedes it.
        var ev = CalendarEvent.Create(
            Tenant, "Conference", new DateOnly(2026, 1, 28), new DateOnly(2026, 2, 2), Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));
        Assert.Single(occ);
        Assert.Equal(new DateOnly(2026, 1, 28), occ[0].Start);
        Assert.Equal(new DateOnly(2026, 2, 2), occ[0].End);
    }

    // ----------------------------------------------------------------
    // EXDATE cancels one occurrence
    // ----------------------------------------------------------------

    [Fact]
    public void Exdate_CancelsOneOccurrence_ItDisappears()
    {
        var anchor = new DateOnly(2026, 1, 5); // Monday
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        // Cancel the Jan 12 occurrence.
        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        // Now only 3 Mondays: 5, 19, 26 (12 is gone).
        Assert.Equal(3, occ.Count);
        Assert.DoesNotContain(new DateOnly(2026, 1, 12), occ.Select(o => o.Start));
        Assert.Equal(
            new[] { new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 19), new DateOnly(2026, 1, 26) },
            occ.Select(o => o.Start));
    }

    [Fact]
    public void RestoreOccurrence_UndoesExdate()
    {
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor);
        ev.RestoreOccurrence(new DateOnly(2026, 1, 12), Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.Equal(4, occ.Count);
        Assert.Contains(new DateOnly(2026, 1, 12), occ.Select(o => o.Start));
    }

    // ----------------------------------------------------------------
    // RECURRENCE-ID overrides one occurrence (edit-this-occurrence)
    // ----------------------------------------------------------------

    [Fact]
    public void RecurrenceIdOverride_OverridesOneOccurrence_SeriesUnchanged()
    {
        var anchor = new DateOnly(2026, 1, 5); // Monday
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        // Move + retitle the Jan 19 occurrence to Jan 20 "Standup (rescheduled)".
        ev.OverrideOccurrence(
            new OccurrenceOverride
            {
                RecurrenceId = new DateOnly(2026, 1, 19),
                NewStart     = new DateOnly(2026, 1, 20),
                NewTitle     = "Standup (rescheduled)",
            },
            Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        // Still 4 occurrences, but the Jan 19 one is now Jan 20 / retitled; the others are untouched.
        Assert.Equal(4, occ.Count);

        var overridden = Assert.Single(occ, o => o.IsOverride);
        Assert.Equal(new DateOnly(2026, 1, 20), overridden.Start);
        Assert.Equal("Standup (rescheduled)", overridden.Title);
        Assert.Equal(new DateOnly(2026, 1, 19), overridden.RecurrenceId); // stable key preserved

        // The series master + other occurrences are unchanged.
        Assert.Equal("Standup", ev.Title);
        Assert.DoesNotContain(new DateOnly(2026, 1, 19), occ.Select(o => o.Start));
        foreach (var d in new[] { new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 26) })
            Assert.Contains(d, occ.Where(o => !o.IsOverride).Select(o => o.Start));
    }

    [Fact]
    public void Override_MovingOccurrenceIntoWindow_IsIncluded()
    {
        // Occurrence Feb 2 (outside the January window) is overridden to move to Jan 30 (inside).
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        ev.OverrideOccurrence(
            new OccurrenceOverride
            {
                RecurrenceId = new DateOnly(2026, 2, 2), // Monday, outside Jan window
                NewStart     = new DateOnly(2026, 1, 30), // moved into Jan window
            },
            Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        // The moved occurrence appears in January even though its RECURRENCE-ID is in February.
        var moved = Assert.Single(occ, o => o.IsOverride);
        Assert.Equal(new DateOnly(2026, 1, 30), moved.Start);
        Assert.Equal(new DateOnly(2026, 2, 2), moved.RecurrenceId);
    }

    [Fact]
    public void CancelledOverride_IsOmittedFromExpansion()
    {
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        // Edit then cancel the same occurrence (cancel-an-already-edited occurrence path).
        ev.OverrideOccurrence(
            new OccurrenceOverride
            {
                RecurrenceId = new DateOnly(2026, 1, 12),
                NewStart     = new DateOnly(2026, 1, 13),
                IsCancelled  = true,
            },
            Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.Equal(3, occ.Count);
        Assert.DoesNotContain(new DateOnly(2026, 1, 13), occ.Select(o => o.Start));
    }

    [Fact]
    public void OverrideOnExdatedOccurrence_Throws()
    {
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");
        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor);

        Assert.Throws<InvalidOperationException>(() =>
            ev.OverrideOccurrence(
                new OccurrenceOverride { RecurrenceId = new DateOnly(2026, 1, 12), NewStart = new DateOnly(2026, 1, 12) },
                Actor));
    }

    // ----------------------------------------------------------------
    // Edit-series: UNTIL-split (this-and-future) + master edit (all)
    // ----------------------------------------------------------------

    [Fact]
    public void EditThisAndFuture_UntilSplit_BoundsTheOldSeries()
    {
        // Original: weekly Monday from Jan 5. "Edit this-and-future" at Jan 19 →
        //   old series ends at Jan 12 (UNTIL=20260112), new series starts Jan 19.
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        ev.EndSeriesOn(new DateOnly(2026, 1, 12), Actor);

        Assert.Contains("UNTIL=20260112", ev.Rrule);
        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        // Old series now only Jan 5 + Jan 12 (UNTIL is inclusive of the 12th).
        Assert.Equal(
            new[] { new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 12) },
            occ.Select(o => o.Start));

        // The "new series for the future" is created by the caller as a separate event:
        var future = CalendarEvent.Create(
            Tenant, "Standup (new cadence)", new DateOnly(2026, 1, 19), new DateOnly(2026, 1, 19), Actor,
            rrule: "FREQ=WEEKLY;BYDAY=TU"); // e.g. moved to Tuesdays
        var futureOcc = NewSut().Expand(future, new DateOnly(2026, 1, 19), new DateOnly(2026, 1, 31));
        Assert.All(futureOcc, o => Assert.Equal(DayOfWeek.Tuesday, o.Start.DayOfWeek));
    }

    [Fact]
    public void ApplyUntil_StripsCount_AndReplacesPriorUntil()
    {
        // COUNT removed (mutually exclusive with UNTIL), prior UNTIL replaced.
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO;UNTIL=20260301",
            CalendarEvent.ApplyUntil("FREQ=WEEKLY;BYDAY=MO;COUNT=10", new DateOnly(2026, 3, 1)));
        Assert.Equal("FREQ=DAILY;UNTIL=20260301",
            CalendarEvent.ApplyUntil("FREQ=DAILY;UNTIL=20251231", new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void EditMaster_AppliesToEveryNonOverriddenOccurrence()
    {
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        // Override one occurrence first…
        ev.OverrideOccurrence(
            new OccurrenceOverride { RecurrenceId = new DateOnly(2026, 1, 19), NewStart = new DateOnly(2026, 1, 19), NewTitle = "Kept" },
            Actor);

        // …then edit-all (master edit): rename the series.
        ev.EditMaster("Daily Sync", anchor, anchor, Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        // Non-overridden occurrences take the new master title; the override keeps its own title
        // (§7.1: a detached occurrence is preserved, not silently clobbered).
        Assert.All(occ.Where(o => !o.IsOverride), o => Assert.Equal("Daily Sync", o.Title));
        Assert.Equal("Kept", Assert.Single(occ, o => o.IsOverride).Title);
    }

    [Fact]
    public void CancelledWholeSeries_YieldsNothing()
    {
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");
        ev.Cancel(Actor);

        Assert.Empty(NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    // ----------------------------------------------------------------
    // Guards
    // ----------------------------------------------------------------

    [Fact]
    public void CancelOccurrence_OnNonRecurringEvent_Throws()
    {
        var ev = CalendarEvent.Create(Tenant, "One-off", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor);
        Assert.Throws<InvalidOperationException>(() => ev.CancelOccurrence(new DateOnly(2026, 1, 5), Actor));
    }

    [Fact]
    public void Create_EndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CalendarEvent.Create(Tenant, "Bad", new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 5), Actor));
    }

    // ----------------------------------------------------------------
    // M1 (deep-review §7.1 "known edge"): edit-master / edit-series × existing override.
    // The anchor (DTSTART) move must NOT silently strand an EXDATE / RECURRENCE-ID override.
    // ----------------------------------------------------------------

    [Fact]
    public void EditMaster_AnchorMove_WithExistingOverride_IsRejected_OverrideSurvives()
    {
        // Repro from the S0 deep-review M1: FREQ=WEEKLY (no BYDAY) anchored Jan 5 (gens Jan 5/12/19/26);
        // override on Jan 19; re-anchor to Jan 6 → the rule would gen Jan 6/13/20/27 and the Jan 19
        // override would match no generated date and SILENTLY VANISH. §7.1 forbids that — EditMaster
        // must reject the anchor move while occurrence edits exist.
        var ev = CalendarEvent.Create(
            Tenant, "Series", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor, rrule: "FREQ=WEEKLY");
        ev.OverrideOccurrence(
            new OccurrenceOverride { RecurrenceId = new DateOnly(2026, 1, 19), NewStart = new DateOnly(2026, 1, 19), NewTitle = "Special" },
            Actor);

        // The anchor move is rejected.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ev.EditMaster("Series", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 6), Actor));
        Assert.Contains("anchor", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The override is intact and still expands — NOT silently lost.
        Assert.True(ev.Overrides.ContainsKey(new DateOnly(2026, 1, 19)));
        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.Equal("Special", Assert.Single(occ, o => o.IsOverride).Title);
        Assert.Equal(new DateOnly(2026, 1, 5), ev.Start); // anchor unchanged by the rejected move
    }

    [Fact]
    public void EditMaster_AnchorMove_WithExistingExdate_IsRejected()
    {
        var ev = CalendarEvent.Create(
            Tenant, "Series", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor, rrule: "FREQ=WEEKLY");
        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor);

        Assert.Throws<InvalidOperationException>(() =>
            ev.EditMaster("Series", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 6), Actor));
        Assert.Contains(new DateOnly(2026, 1, 12), ev.ExceptionDates); // EXDATE preserved
    }

    [Fact]
    public void EditMaster_SameAnchor_WithOverride_IsAllowed_AndPreservesOverride()
    {
        // A same-anchor "edit all" (rename / retime, no anchor move) stays allowed and preserves the
        // override per §7.1 ("a detached occurrence is preserved, not silently clobbered").
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");
        ev.OverrideOccurrence(
            new OccurrenceOverride { RecurrenceId = new DateOnly(2026, 1, 19), NewStart = new DateOnly(2026, 1, 19), NewTitle = "Kept" },
            Actor);

        // Same anchor, new title — allowed.
        ev.EditMaster("Daily Sync", anchor, anchor, Actor);

        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.All(occ.Where(o => !o.IsOverride), o => Assert.Equal("Daily Sync", o.Title));
        Assert.Equal("Kept", Assert.Single(occ, o => o.IsOverride).Title); // override preserved
    }

    [Fact]
    public void EditMaster_AnchorMove_AfterClearingOverrides_IsAllowed()
    {
        // The escape hatch §7.1 prescribes: clear the occurrence edits first, then move the anchor.
        var ev = CalendarEvent.Create(
            Tenant, "Series", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor, rrule: "FREQ=WEEKLY");
        ev.OverrideOccurrence(
            new OccurrenceOverride { RecurrenceId = new DateOnly(2026, 1, 19), NewStart = new DateOnly(2026, 1, 19) },
            Actor);

        ev.ClearOverride(new DateOnly(2026, 1, 19), Actor);
        ev.EditMaster("Series", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 6), Actor); // now allowed

        Assert.Equal(new DateOnly(2026, 1, 6), ev.Start);
    }

    [Fact]
    public void EndSeriesOn_PrunesZombieOverridePastTheUntilSplit()
    {
        // The "zombie" edge: an override whose RECURRENCE-ID is PAST the new UNTIL is logically beyond
        // the truncated series. Before the fix the expander's "moved-into-window" second pass could
        // still surface it (a phantom occurrence past the series end). EndSeriesOn must prune it and
        // report the prune.
        var anchor = new DateOnly(2026, 1, 5); // Monday
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        // Override the Jan 26 occurrence to move it to Jan 15 (which lands inside a Jan window).
        ev.OverrideOccurrence(
            new OccurrenceOverride
            {
                RecurrenceId = new DateOnly(2026, 1, 26),
                NewStart     = new DateOnly(2026, 1, 15),
                NewTitle     = "Zombie",
            },
            Actor);

        // Truncate the series at Jan 12 — the Jan 26 RECURRENCE-ID is now past the series end.
        var pruned = ev.EndSeriesOn(new DateOnly(2026, 1, 12), Actor);

        Assert.Equal(1, pruned); // the one zombie override was pruned + surfaced
        Assert.False(ev.Overrides.ContainsKey(new DateOnly(2026, 1, 26)));

        // The expansion is just the truncated series (Jan 5 + Jan 12); no phantom "Zombie" at Jan 15.
        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.Equal(new[] { new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 12) }, occ.Select(o => o.Start));
        Assert.DoesNotContain(occ, o => o.Title == "Zombie");
    }

    [Fact]
    public void EndSeriesOn_KeepsOccurrenceEditsWithinTheTruncatedRange()
    {
        // An EXDATE / override whose RECURRENCE-ID is on-or-before the new UNTIL is still valid and
        // must NOT be pruned.
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");
        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor); // within the kept range

        var pruned = ev.EndSeriesOn(new DateOnly(2026, 1, 19), Actor); // UNTIL after the EXDATE

        Assert.Equal(0, pruned);
        Assert.Contains(new DateOnly(2026, 1, 12), ev.ExceptionDates);
        var occ = NewSut().Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        // Jan 5 + Jan 19 (12 cancelled, 26 past UNTIL).
        Assert.Equal(new[] { new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 19) }, occ.Select(o => o.Start));
    }
}
