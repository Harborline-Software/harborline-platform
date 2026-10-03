namespace Harborline.Experiments.SchedulingDecisionModel.Data;

/// <summary>
/// SplitMix64. Owned here so generated instances never depend on the runtime's System.Random algorithm.
/// </summary>
public sealed class Rng(ulong seed)
{
    private ulong _state = seed;

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int Int(int min, int max)
    {
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max));
        }

        var span = (ulong)(max - min) + 1;
        return min + (int)(NextUInt64() % span);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double Unit() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public double Range(double min, double max) => min + (Unit() * (max - min));

    public bool Chance(double p) => Unit() < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    /// <summary>Derives an independent child seed, so adding a draw in one place does not shift another.</summary>
    public static ulong Derive(ulong seed, string label)
    {
        var h = seed;
        foreach (var c in label)
        {
            h = (h ^ c) * 0x100000001B3UL;
        }

        return new Rng(h).NextUInt64();
    }
}
