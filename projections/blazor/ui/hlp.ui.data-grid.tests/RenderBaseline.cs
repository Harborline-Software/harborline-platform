using System.Diagnostics;

namespace Harborline.UIAdapters.Blazor.Tests;

// Ticket 265. A performance budget written as a flat millisecond constant is a budget for one
// machine. The renders in DataGridPerformanceTests cost 55 ms on the Windows relocation host when
// the old 240 ms constant was chosen and 360 ms on the 2016 Mac in the gate, so the constant was
// either red on the slow host or blind on the fast one.
//
// Fix 1 answered that with a ratio to a reference render of one window's worth of rows, timed by
// interleaved medians in the same process. Review round 2 measured the ratio over ten consecutive
// runs per host and it held -- but only just: the worst observation was 1.86 against k 1.9. A
// budget with 2% of margin is one scheduling accident from red, which is the defect that got
// ticket 265 rejected twice, so fix 2 drops the ratio here as well. Both rows now assert a single
// absolute ceiling derived from measurement (see DataGridPerformanceTests for the numbers, the
// hosts they came from and the detection floor that follows).
//
// What survives is the sampling discipline that DID hold up, and it is the whole of this class:
// discard warm-up rounds, then take the MEDIAN of several timed rounds. Warm-up matters here more
// than anywhere -- with one or two rounds the FIRST test in the process still had tiering in
// flight and the same loop measured 43-128 ms run to run; at four rounds it is 39-59.
internal static class RenderBaseline
{
    private const int WarmUpRounds = 4;
    internal const int Rounds = 5;

    private static async Task<double> Milliseconds(Func<Task> loop)
    {
        var stopwatch = Stopwatch.StartNew();
        await loop();
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2d;
    }

    /// <summary>
    /// Runs <paramref name="measured"/> for <see cref="Rounds"/> timed rounds after
    /// <see cref="WarmUpRounds"/> discarded warm-up rounds and returns the median milliseconds.
    /// The median, not the mean and not the best sample: one GC pause or one co-tenant scheduling
    /// slice in five rounds must not move the number the ceiling is compared against.
    /// </summary>
    internal static async Task<double> MedianMilliseconds(Func<Task> measured)
    {
        for (var warm = 0; warm < WarmUpRounds; warm++) await measured();
        var measurements = new List<double>(Rounds);
        for (var round = 0; round < Rounds; round++) measurements.Add(await Milliseconds(measured));
        return Median(measurements);
    }
}
