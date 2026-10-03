using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;

namespace Harborline.Experiments.SchedulingDecisionModel.Tests;

/// <summary>
/// Each mutation breaks one rule of docs/profile-spec.md in the hand-checked plan
/// <see cref="Profiles.TwoJobsPlan"/>. The expected codes are literals worked out by hand.
/// </summary>
public sealed class PlanCheckerMutationTests
{
    private static readonly PlanChecker Checker = new(Profiles.TwoJobs(5));

    [Fact]
    public void HandCheckedPlan_IsFeasible()
    {
        Assert.Empty(Checker.Check(Profiles.TwoJobsPlan()).Violations);
    }

    public static TheoryData<string, string[]> Mutations => new()
    {
        // M2 moved onto mach-a at [2,4): it overlaps M1 [1,3) on mach-a.
        { "overlap", ["R7-exclusive"] },
        // P2 at [3,4) ends after M2 starts at 3.
        { "precedence", ["R8-precedence"] },
        // I2 at 6 is outside its start window [5,5].
        { "window", ["R3-window"] },
        // I1 removed.
        { "missing", ["R1-coverage"] },
        // M1 ends at 2, but its duration is 2 from start 1.
        { "duration", ["R2-duration"] },
        // M2 on mach-b at [2,4): mach-b is unavailable at slot 2.
        { "unavailable", ["R6-availability"] },
        // M1 uses the inspector, which lacks the machine capability.
        { "capability", ["R5-requirements"] },
        // M2 holds both machines; it requires one.
        { "extra-resource", ["R5-requirements"] },
        // M2 lists mach-b twice. R5 is judged on the distinct set, so only R4 fires.
        { "duplicate-resource", ["R4-resource-known"] },
        // M2 names a resource that does not exist.
        { "unknown-resource", ["R4-resource-known", "R5-requirements"] },
        // A placement for an activity the profile does not have.
        { "unknown-activity", ["R1-coverage"] },
        // I1 placed twice, both at [3,4) on the inspector, so the copies also clash with each other.
        { "double-placement", ["R1-coverage", "R7-exclusive"] },
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void EachMutation_IsRejectedWithExactlyItsRule(string mutation, string[] expectedCodes)
    {
        var plan = Profiles.TwoJobsPlan().ToList();
        Replace(plan, mutation);

        var codes = Checker.Check(plan).Violations.Select(v => v.Code).Distinct().Order().ToArray();

        Assert.Equal(expectedCodes.Order().ToArray(), codes);
    }

    private static void Replace(List<AssignmentCandidate> plan, string mutation)
    {
        void Set(string id, AssignmentCandidate value) => plan[plan.FindIndex(p => p.ActivityId == id)] = value;

        switch (mutation)
        {
            case "overlap": Set("M2", new("M2", 2, 4, ["mach-a"])); Set("P2", new("P2", 1, 2, ["bench"])); break;
            case "precedence": Set("P2", new("P2", 3, 4, ["bench"])); break;
            case "window": Set("I2", new("I2", 6, 7, ["insp"])); break;
            case "missing": plan.RemoveAll(p => p.ActivityId == "I1"); break;
            case "duration": Set("M1", new("M1", 1, 2, ["mach-a"])); break;
            case "unavailable": Set("M2", new("M2", 2, 4, ["mach-b"])); break;
            case "capability": Set("M1", new("M1", 1, 3, ["insp"])); break;
            case "extra-resource": Set("M2", new("M2", 3, 5, ["mach-a", "mach-b"])); break;
            case "duplicate-resource": Set("M2", new("M2", 3, 5, ["mach-b", "mach-b"])); break;
            case "unknown-resource": Set("M2", new("M2", 3, 5, ["mach-z"])); break;
            case "unknown-activity": plan.Add(new("ghost", 0, 1, ["bench"])); break;
            case "double-placement": plan.Add(new("I1", 3, 4, ["insp"])); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }
}
