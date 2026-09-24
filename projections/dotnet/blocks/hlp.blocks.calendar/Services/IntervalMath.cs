using Harborline.Blocks.Calendar.Models;

namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// Interval algebra over half-open UTC <see cref="TimeInterval"/>s (Slice S3) — merge, subtract,
/// clip. The shared engine of free/busy: <c>free = clip(merge(availability) − merge(occupancy))</c>.
/// All operations treat intervals as half-open <c>[Start, End)</c> so touching endpoints never
/// spuriously overlap (a 9–12 and a 12–5 are adjacent, not overlapping).
/// </summary>
internal static class IntervalMath
{
    /// <summary>
    /// Merge a set of intervals into the minimal set of non-overlapping, non-adjacent intervals
    /// covering the same instants (the union), ordered by start. Adjacent intervals (one ends where
    /// the next begins) are coalesced into one.
    /// </summary>
    public static List<TimeInterval> Merge(IEnumerable<TimeInterval> intervals)
    {
        var sorted = intervals.OrderBy(i => i.StartUtc).ThenBy(i => i.EndUtc).ToList();
        var merged = new List<TimeInterval>();
        foreach (var iv in sorted)
        {
            if (merged.Count == 0)
            {
                merged.Add(iv);
                continue;
            }
            var last = merged[^1];
            // Coalesce when the next interval starts at or before the running end (overlap OR adjacency).
            if (iv.StartUtc <= last.EndUtc)
            {
                if (iv.EndUtc > last.EndUtc)
                    merged[^1] = new TimeInterval(last.StartUtc, iv.EndUtc);
                // else fully contained — drop.
            }
            else
            {
                merged.Add(iv);
            }
        }
        return merged;
    }

    /// <summary>
    /// Subtract the union of <paramref name="busy"/> from the union of <paramref name="available"/> —
    /// the free intervals (availability with the busy spans punched out), ordered by start. Both
    /// operands are merged first; the result contains only positive-length intervals.
    /// </summary>
    public static List<TimeInterval> Subtract(IEnumerable<TimeInterval> available, IEnumerable<TimeInterval> busy)
    {
        var avail = Merge(available);
        var blocked = Merge(busy);
        var free = new List<TimeInterval>();

        foreach (var slot in avail)
        {
            var cursor = slot.StartUtc;
            foreach (var b in blocked)
            {
                if (b.EndUtc <= cursor) continue;          // entirely before the cursor
                if (b.StartUtc >= slot.EndUtc) break;      // past this slot (blocked is sorted)

                if (b.StartUtc > cursor)
                    free.Add(new TimeInterval(cursor, b.StartUtc));   // gap before the block

                if (b.EndUtc > cursor)
                    cursor = b.EndUtc;                                // advance past the block
                if (cursor >= slot.EndUtc) break;
            }
            if (cursor < slot.EndUtc)
                free.Add(new TimeInterval(cursor, slot.EndUtc));      // tail of the slot
        }
        return free;
    }

    /// <summary>
    /// Clip an interval to [<paramref name="windowStart"/>, <paramref name="windowEnd"/>] — the
    /// overlap, or <see langword="null"/> when the interval falls entirely outside the window.
    /// </summary>
    public static TimeInterval? Clip(TimeInterval iv, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var start = iv.StartUtc > windowStart ? iv.StartUtc : windowStart;
        var end = iv.EndUtc < windowEnd ? iv.EndUtc : windowEnd;
        return end > start ? new TimeInterval(start, end) : null;
    }

    /// <summary>Clip every interval to the window, dropping those that fall entirely outside it.</summary>
    public static List<TimeInterval> ClipAll(IEnumerable<TimeInterval> intervals, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var result = new List<TimeInterval>();
        foreach (var iv in intervals)
        {
            if (Clip(iv, windowStart, windowEnd) is { } clipped)
                result.Add(clipped);
        }
        return result;
    }

    /// <summary>
    /// The merged spans where at least <paramref name="depth"/> of <paramref name="intervals"/> overlap
    /// (T-626 pool capacity). With <paramref name="depth"/> = 1 this is <see cref="Merge"/>: the
    /// exclusive kind is a pool of one in the algebra, and only there.
    /// </summary>
    public static List<TimeInterval> WhereDepthAtLeast(IEnumerable<TimeInterval> intervals, int depth)
        => Merge(Sweep(intervals).Where(s => s.Depth >= depth).Select(s => s.Segment));

    /// <summary>The deepest overlap among <paramref name="intervals"/> inside <paramref name="within"/>; 0 when none touches it.</summary>
    public static int MaxDepth(IEnumerable<TimeInterval> intervals, TimeInterval within)
        => Sweep(intervals.Select(iv => Clip(iv, within.StartUtc, within.EndUtc)).Where(c => c.HasValue).Select(c => c!.Value))
            .Select(s => s.Depth).DefaultIfEmpty(0).Max();

    /// <summary>Plane sweep: every positive-length segment between consecutive endpoints, with how many intervals cover it.</summary>
    private static IEnumerable<(TimeInterval Segment, int Depth)> Sweep(IEnumerable<TimeInterval> intervals)
    {
        var edges = new List<(DateTimeOffset At, int Delta)>();
        foreach (var iv in intervals)
        {
            edges.Add((iv.StartUtc, +1));
            edges.Add((iv.EndUtc, -1));
        }
        edges.Sort(static (a, b) => a.At.CompareTo(b.At));
        var depth = 0;
        for (var i = 0; i < edges.Count; i++)
        {
            depth += edges[i].Delta;
            if (i + 1 < edges.Count && depth > 0 && edges[i + 1].At > edges[i].At)
                yield return (new TimeInterval(edges[i].At, edges[i + 1].At), depth);
        }
    }
}
