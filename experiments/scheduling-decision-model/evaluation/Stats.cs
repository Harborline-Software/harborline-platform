using System.Runtime.CompilerServices;
using Harborline.Experiments.SchedulingDecisionModel.Data;

namespace Harborline.Experiments.SchedulingDecisionModel.Evaluation;

/// <summary>Paired comparisons with group-level bootstrap (base and tightened variants resample together).</summary>
public static class Stats
{
    public const int Replicates = 2000;

    public sealed record Interval(double Estimate, double Low, double High);

    /// <summary>Solved-rate difference (a − b) in percentage points.</summary>
    public static Interval SolvedDifference(IReadOnlyList<(string Group, bool A, bool B)> rows, ulong seed) =>
        Bootstrap(rows, seed, sample => 100.0 * sample.Average(r => (r.A ? 1.0 : 0.0) - (r.B ? 1.0 : 0.0)));

    /// <summary>
    /// Geometric-mean ratio of work to first feasible plan (a ÷ b), over oracle-feasible instances.
    /// Unsolved counts at the budget; instances both sides fail are excluded (T-1053 sdm-03).
    /// </summary>
    public static Interval WorkRatio(IReadOnlyList<(string Group, double A, double B)> rows, ulong seed) =>
        rows.Count == 0
            ? new(double.NaN, double.NaN, double.NaN)
            : Bootstrap(rows, seed, sample => Math.Exp(sample.Average(r => Math.Log(Math.Max(1, r.A)) - Math.Log(Math.Max(1, r.B)))));

    private static Interval Bootstrap<T>(IReadOnlyList<T> rows, ulong seed, Func<IReadOnlyList<T>, double> statistic)
        where T : struct, ITuple
    {
        var groups = rows.GroupBy(r => (string)r[0]!).Select(g => g.ToArray()).ToArray();
        var estimate = statistic(rows);
        var rng = new Rng(seed);
        var values = new double[Replicates];
        for (var b = 0; b < Replicates; b++)
        {
            var sample = new List<T>(rows.Count);
            for (var g = 0; g < groups.Length; g++)
            {
                sample.AddRange(groups[rng.Int(0, groups.Length - 1)]);
            }

            values[b] = statistic(sample);
        }

        Array.Sort(values);
        return new(estimate, values[(int)(0.025 * Replicates)], values[(int)(0.975 * Replicates) - 1]);
    }
}
