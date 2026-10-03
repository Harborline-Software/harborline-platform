using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Data;

public enum SizeClass
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// Seeded generators for profile <c>sdm.unit-resource.v1</c>. A family fixes the structure;
/// <paramref name="tightness"/> scales deadlines relative to the resource-free critical path
/// (values near 1 are tight, larger values are loose).
/// </summary>
public static class Families
{
    public const string GeneratorVersion = "sdm-gen.v1";

    public static readonly string[] Seen = ["jobshop-chains", "project-dag", "parallel-contention"];
    public static readonly string[] Unseen = ["shift-gaps", "multi-skill"];

    public static (int Min, int Max) ActivityCount(SizeClass size) => size switch
    {
        SizeClass.Small => (6, 10),
        SizeClass.Medium => (11, 18),
        _ => (19, 30),
    };

    /// <summary>Base tightness draw per family, before the tightened variant scales it.</summary>
    public static double DrawTightness(string family, Rng rng) => family switch
    {
        "jobshop-chains" or "shift-gaps" => rng.Range(1.3, 3.0),
        "project-dag" or "multi-skill" => rng.Range(1.1, 2.4),
        "parallel-contention" => rng.Range(1.0, 1.8),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    public static SchedulingProfile Generate(string family, SizeClass size, ulong seed, double tightness, string instanceId)
    {
        var rng = new Rng(seed);
        var (min, max) = ActivityCount(size);
        var n = rng.Int(min, max);
        var spec = family switch
        {
            "jobshop-chains" => JobShop(rng, n, tightness, shifts: false),
            "shift-gaps" => JobShop(rng, n, tightness, shifts: true),
            "project-dag" => ProjectDag(rng, n, tightness, multiSkill: false),
            "multi-skill" => ProjectDag(rng, n, tightness, multiSkill: true),
            "parallel-contention" => Contention(rng, n, tightness),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

        // Seen families occasionally compose two independent halves (disconnected components),
        // and rarely carry a precedence cycle (a structural infeasibility).
        if (Seen.Contains(family) && n >= 8 && rng.Chance(0.2))
        {
            var left = family == "parallel-contention" ? Contention(rng, n / 2, tightness)
                : family == "project-dag" ? ProjectDag(rng, n / 2, tightness, false)
                : JobShop(rng, n / 2, tightness, false);
            var right = family == "parallel-contention" ? Contention(rng, n - (n / 2), tightness)
                : family == "project-dag" ? ProjectDag(rng, n - (n / 2), tightness, false)
                : JobShop(rng, n - (n / 2), tightness, false);
            spec = Spec.Compose(left, right);
        }

        if (Seen.Contains(family) && spec.Precedence.Count > 1 && rng.Chance(0.03))
        {
            spec.AddCycle(rng);
        }

        return spec.Build(instanceId);
    }

    private static Spec JobShop(Rng rng, int n, double t, bool shifts)
    {
        var spec = new Spec();
        var types = rng.Int(2, 4);
        for (var k = 0; k < types; k++)
        {
            var count = rng.Chance(0.35) ? 2 : 1;
            for (var m = 0; m < count; m++)
            {
                spec.AddResource($"m{k}.{m}", [$"type{k}"]);
            }
        }

        var remaining = n;
        var job = 0;
        while (remaining > 0)
        {
            var ops = Math.Min(remaining, rng.Int(2, 4));
            remaining -= ops;
            var release = rng.Int(0, 3);
            string? previous = null;
            for (var o = 0; o < ops; o++)
            {
                var id = $"j{job}.o{o}";
                spec.AddActivity(id, rng.Int(1, 4), [($"type{rng.Int(0, types - 1)}", 1)]);
                spec.Release[id] = release;
                if (previous is not null)
                {
                    spec.AddPrecedence(previous, id, rng.Chance(0.8) ? 0 : 1);
                }

                previous = id;
            }

            job++;
        }

        // Shifts remove machine time, so their deadlines are stretched to match (calibrated on pilot seeds).
        spec.DeriveWindows(shifts ? t * 1.5 : t);
        if (shifts)
        {
            spec.ApplyShifts(rng);
        }

        return spec;
    }

    private static Spec ProjectDag(Rng rng, int n, double t, bool multiSkill)
    {
        var spec = new Spec();
        var crews = rng.Int(2, 3);
        var pools = new int[crews];
        for (var c = 0; c < crews; c++)
        {
            pools[c] = rng.Int(1, 3);
            for (var m = 0; m < pools[c]; m++)
            {
                var caps = new List<string> { $"crew{c}" };
                if (multiSkill && rng.Chance(0.3))
                {
                    caps.Add($"crew{(c + 1) % crews}");
                }

                spec.AddResource($"c{c}.{m}", caps.ToArray());
            }
        }

        if (multiSkill)
        {
            spec.AddResource("insp.0", ["inspect"]);
        }

        var layers = rng.Int(3, 5);
        var byLayer = Enumerable.Range(0, layers).Select(_ => new List<string>()).ToArray();
        for (var a = 0; a < n; a++)
        {
            var layer = a < layers ? a : rng.Int(0, layers - 1);
            var id = $"a{a}";
            var crew = rng.Int(0, crews - 1);
            var needs = new List<(string, int)> { ($"crew{crew}", pools[crew] >= 2 && rng.Chance(0.25) ? 2 : 1) };
            if (multiSkill && rng.Chance(0.6))
            {
                var other = rng.Chance(0.5) ? "inspect" : $"crew{(crew + 1) % crews}";
                if (needs.All(x => x.Item1 != other))
                {
                    needs.Add((other, 1));
                }
            }

            spec.AddActivity(id, rng.Int(1, 5), needs.ToArray());
            spec.Release[id] = 0;
            byLayer[layer].Add(id);
        }

        for (var l = 1; l < layers; l++)
        {
            var earlier = byLayer.Take(l).SelectMany(x => x).ToArray();
            foreach (var id in byLayer[l])
            {
                var linked = false;
                foreach (var p in earlier)
                {
                    if (rng.Chance(0.25))
                    {
                        spec.AddPrecedence(p, id, 0);
                        linked = true;
                    }
                }

                if (!linked && earlier.Length > 0)
                {
                    spec.AddPrecedence(rng.Pick(earlier), id, 0);
                }
            }
        }

        spec.DeriveWindows(t);
        return spec;
    }

    private static Spec Contention(Rng rng, int n, double t)
    {
        var spec = new Spec();
        var types = rng.Int(1, 2);
        var capacity = 0;
        for (var k = 0; k < types; k++)
        {
            var pool = rng.Int(2, 4);
            capacity += pool;
            for (var m = 0; m < pool; m++)
            {
                spec.AddResource($"r{k}.{m}", [$"type{k}"]);
            }
        }

        var durations = Enumerable.Range(0, n).Select(_ => rng.Int(1, 4)).ToArray();
        var horizon = Math.Max(durations.Max() + 1, (int)Math.Ceiling(durations.Sum() / (double)capacity * t) + 1);
        for (var a = 0; a < n; a++)
        {
            var id = $"a{a}";
            spec.AddActivity(id, durations[a], [($"type{rng.Int(0, types - 1)}", 1)]);
            var earliest = rng.Int(0, horizon - durations[a]);
            var width = rng.Int(0, Math.Max(0, (int)Math.Ceiling(horizon * 0.4)));
            spec.Windows[id] = (earliest, Math.Min(horizon - durations[a], earliest + width));
        }

        spec.Horizon = horizon;
        return spec;
    }
}
