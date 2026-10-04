using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Tests;

/// <summary>Small hand-authored profiles. Expected outcomes are worked by hand in each test.</summary>
internal sealed class ProfileBuilder
{
    private readonly List<Activity> _activities = [];
    private readonly List<TimeWindow> _windows = [];
    private readonly List<PlanningResource> _resources = [];
    private readonly List<ResourceRequirement> _requirements = [];
    private readonly List<PrecedenceConstraint> _precedence = [];

    public ProfileBuilder Resource(string id, string capability, int firstSlot, int lastSlot) =>
        Resource(id, [capability], firstSlot, lastSlot);

    public ProfileBuilder Resource(string id, string[] capabilities, int firstSlot, int lastSlot)
    {
        _resources.Add(new PlanningResource(
            id,
            capabilities.ToHashSet(StringComparer.Ordinal),
            Enumerable.Range(firstSlot, lastSlot - firstSlot + 1).ToHashSet()));
        return this;
    }

    public ProfileBuilder Activity(string id, int duration, int earliestStart, int latestStart, params (string Capability, int Quantity)[] needs)
    {
        _activities.Add(new Activity(id, duration));
        _windows.Add(new TimeWindow(id, earliestStart, latestStart));
        var n = 0;
        foreach (var (capability, quantity) in needs)
        {
            _requirements.Add(new ResourceRequirement($"{id}.q{n++}", id, capability, quantity));
        }

        return this;
    }

    public ProfileBuilder Before(string before, string after, int gap = 0)
    {
        _precedence.Add(new PrecedenceConstraint($"{before}->{after}", before, after, gap));
        return this;
    }

    public SchedulingProfile Build(bool complete = true) => new(
        "test",
        "v1",
        _activities,
        _windows,
        _resources,
        _requirements,
        _precedence,
        PlanningFactSets.Required.Select(name => new PlanningFactSetPin(name, "v1", complete)).ToArray());
}

internal static class Profiles
{
    /// <summary>
    /// Two jobs (prep, machine, inspect) share one bench, one inspector and machine "mach-a";
    /// the alternative machine "mach-b" only becomes available at slot 3. Job 1 must inspect
    /// at slot 3. Job 2 inspects at <paramref name="job2InspectStart"/>.
    ///
    /// Hand calculation. Job 1: inspect at 3 forces M1 to end at 3 or earlier. mach-b is
    /// unavailable before 3, so M1 = [1,3) on mach-a and P1 = [0,1) on the bench. Job 2: the
    /// bench is busy at 0, so P2 starts at 1 or later and M2 starts at 2 or later. M2 = [2,4)
    /// needs a machine at slot 2, but mach-a is busy and mach-b is not yet available. So
    /// M2 starts at 3 or later, ends at 5 or later, and I2 starts at 5 or later.
    ///   job2InspectStart = 5: feasible.
    ///   job2InspectStart = 4: infeasible (the minimal change).
    /// </summary>
    public static SchedulingProfile TwoJobs(int job2InspectStart) => new ProfileBuilder()
        .Resource("bench", "prep", 0, 7)
        .Resource("mach-a", "machine", 0, 7)
        .Resource("mach-b", "machine", 3, 7)
        .Resource("insp", "inspect", 0, 7)
        .Activity("P1", 1, 0, 5, ("prep", 1))
        .Activity("M1", 2, 0, 5, ("machine", 1))
        .Activity("I1", 1, 3, 3, ("inspect", 1))
        .Activity("P2", 1, 0, 5, ("prep", 1))
        .Activity("M2", 2, 0, 5, ("machine", 1))
        .Activity("I2", 1, job2InspectStart, job2InspectStart, ("inspect", 1))
        .Before("P1", "M1").Before("M1", "I1")
        .Before("P2", "M2").Before("M2", "I2")
        .Build();

    /// <summary>A hand-checked feasible plan for <c>TwoJobs(5)</c>.</summary>
    public static AssignmentCandidate[] TwoJobsPlan() =>
    [
        new("I1", 3, 4, ["insp"]),
        new("I2", 5, 6, ["insp"]),
        new("M1", 1, 3, ["mach-a"]),
        new("M2", 3, 5, ["mach-b"]),
        new("P1", 0, 1, ["bench"]),
        new("P2", 1, 2, ["bench"]),
    ];

    /// <summary>One resource and two unit activities X and Y, each with start window [0,2].</summary>
    public static ProfileBuilder OneResourceTwoUnits() => new ProfileBuilder()
        .Resource("R", "c", 0, 2)
        .Activity("X", 1, 0, 2, ("c", 1))
        .Activity("Y", 1, 0, 2, ("c", 1));
}
