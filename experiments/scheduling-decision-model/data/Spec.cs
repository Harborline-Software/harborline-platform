using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Data;

/// <summary>Mutable working form of a generated instance, built into an immutable profile.</summary>
internal sealed class Spec
{
    public List<(string Id, int Duration, (string Capability, int Quantity)[] Needs)> Activities { get; } = [];
    public List<(string Id, string[] Capabilities, HashSet<int>? Slots)> Resources { get; } = [];
    public List<(string Before, string After, int Gap)> Precedence { get; } = [];
    public Dictionary<string, int> Release { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (int Earliest, int Latest)> Windows { get; } = new(StringComparer.Ordinal);
    public int Horizon { get; set; }

    public void AddResource(string id, string[] capabilities) => Resources.Add((id, capabilities, null));

    public void AddActivity(string id, int duration, (string, int)[] needs) => Activities.Add((id, duration, needs));

    public void AddPrecedence(string before, string after, int gap) => Precedence.Add((before, after, gap));

    /// <summary>
    /// Start windows from the resource-free schedule: earliest = longest path from release,
    /// latest = deadline − longest tail, with deadline = ceil(critical path × tightness).
    /// </summary>
    public void DeriveWindows(double tightness)
    {
        var duration = Activities.ToDictionary(a => a.Id, a => a.Duration, StringComparer.Ordinal);
        var order = TopologicalOrder();
        var earliest = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            earliest[id] = Precedence
                .Where(p => p.After == id)
                .Select(p => earliest[p.Before] + duration[p.Before] + p.Gap)
                .DefaultIfEmpty(0)
                .Max();
            earliest[id] = Math.Max(earliest[id], Release.GetValueOrDefault(id));
        }

        var tail = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in order.AsEnumerable().Reverse())
        {
            tail[id] = duration[id] + Precedence
                .Where(p => p.Before == id)
                .Select(p => p.Gap + tail[p.After])
                .DefaultIfEmpty(0)
                .Max();
        }

        var critical = order.Max(id => earliest[id] + tail[id]);
        var deadline = (int)Math.Ceiling(critical * tightness);
        foreach (var id in order)
        {
            Windows[id] = (earliest[id], Math.Max(earliest[id], deadline - tail[id]));
        }

        Horizon = deadline;
    }

    /// <summary>Gives every resource a repeating on/off shift with its own length and phase.</summary>
    public void ApplyShifts(Rng rng)
    {
        var end = Horizon + 8;
        for (var i = 0; i < Resources.Count; i++)
        {
            // On-blocks are at least one slot longer than the longest job-shop operation (4),
            // so shifts constrain placement without making an operation unplaceable outright.
            var on = rng.Int(5, 8);
            var off = rng.Int(1, 3);
            var phase = rng.Int(0, on + off - 1);
            var slots = Enumerable.Range(0, end).Where(s => (s + phase) % (on + off) < on).ToHashSet();
            Resources[i] = (Resources[i].Id, Resources[i].Capabilities, slots);
        }
    }

    /// <summary>Adds the reverse of an existing precedence edge, creating a two-cycle.</summary>
    public void AddCycle(Rng rng)
    {
        var (before, after, _) = rng.Pick(Precedence);
        Precedence.Add((after, before, 0));
    }

    /// <summary>Disjoint union of two instances: ids and capabilities are prefixed so nothing is shared.</summary>
    public static Spec Compose(Spec left, Spec right)
    {
        var result = new Spec();
        foreach (var (prefix, part) in new[] { ("L.", left), ("R.", right) })
        {
            foreach (var r in part.Resources)
            {
                result.Resources.Add((prefix + r.Id, r.Capabilities.Select(c => prefix + c).ToArray(), r.Slots));
            }

            foreach (var a in part.Activities)
            {
                result.Activities.Add((prefix + a.Id, a.Duration, a.Needs.Select(n => (prefix + n.Capability, n.Quantity)).ToArray()));
                result.Windows[prefix + a.Id] = part.Windows[a.Id];
            }

            foreach (var p in part.Precedence)
            {
                result.Precedence.Add((prefix + p.Before, prefix + p.After, p.Gap));
            }
        }

        result.Horizon = Math.Max(left.Horizon, right.Horizon);
        return result;
    }

    public SchedulingProfile Build(string instanceId)
    {
        var maxDuration = Activities.Max(a => a.Duration);
        var full = Enumerable.Range(0, Horizon + maxDuration + 1).ToHashSet();
        return new SchedulingProfile(
            instanceId,
            Families.GeneratorVersion,
            Activities.Select(a => new Activity(a.Id, a.Duration)).ToArray(),
            Activities.Select(a => new TimeWindow(a.Id, Windows[a.Id].Earliest, Windows[a.Id].Latest)).ToArray(),
            Resources.Select(r => new PlanningResource(
                r.Id,
                r.Capabilities.ToHashSet(StringComparer.Ordinal),
                r.Slots ?? full)).ToArray(),
            Activities.SelectMany(a => a.Needs.Select((n, i) =>
                new ResourceRequirement($"{a.Id}.q{i}", a.Id, n.Capability, n.Quantity))).ToArray(),
            Precedence.Select((p, i) => new PrecedenceConstraint($"p{i}", p.Before, p.After, p.Gap)).ToArray(),
            PlanningFactSets.Required.Select(name => new PlanningFactSetPin(name, Families.GeneratorVersion, true)).ToArray());
    }

    private List<string> TopologicalOrder()
    {
        var indegree = Activities.ToDictionary(a => a.Id, _ => 0, StringComparer.Ordinal);
        foreach (var p in Precedence)
        {
            indegree[p.After]++;
        }

        var ready = new Queue<string>(Activities.Select(a => a.Id).Where(id => indegree[id] == 0));
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            order.Add(id);
            foreach (var p in Precedence.Where(p => p.Before == id))
            {
                if (--indegree[p.After] == 0)
                {
                    ready.Enqueue(p.After);
                }
            }
        }

        return order.Count == Activities.Count
            ? order
            : throw new InvalidOperationException("DeriveWindows requires an acyclic precedence graph.");
    }
}
