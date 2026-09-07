using System.Text.Json;
using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class FixturePlanningTests
{
    private static readonly string[] ExpectedFixtureNames =
    [
        "construction-multifamily.feasible.json",
        "construction-multifamily.unsatisfied.json",
        "construction-remodeling.feasible.json",
        "construction-remodeling.unsatisfied.json",
        "construction-residential-new.feasible.json",
        "construction-residential-new.unsatisfied.json",
        "medical-iv-therapy.feasible.json",
        "medical-iv-therapy.unsatisfied.json",
        "medical-oncology.feasible.json",
        "medical-oncology.unsatisfied.json",
        "medical-pregnancy.feasible.json",
        "medical-pregnancy.unsatisfied.json",
    ];

    public static TheoryData<string> FixtureNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in ExpectedFixtureNames)
            {
                data.Add(name);
            }

            return data;
        }
    }

    public static TheoryData<string, string, string?> ExpectedOutcomes => new()
    {
        { "construction-multifamily.feasible.json", "feasible", null },
        { "construction-multifamily.unsatisfied.json", "unsatisfied", "scheduling.unassigned.resource-capacity" },
        { "construction-remodeling.feasible.json", "feasible", null },
        { "construction-remodeling.unsatisfied.json", "unsatisfied", "scheduling.unassigned.approval-unsatisfied" },
        { "construction-residential-new.feasible.json", "feasible", null },
        { "construction-residential-new.unsatisfied.json", "unsatisfied", "scheduling.unassigned.window-precedence" },
        { "medical-iv-therapy.feasible.json", "feasible", null },
        { "medical-iv-therapy.unsatisfied.json", "unsatisfied", "scheduling.unassigned.capacity-limit" },
        { "medical-oncology.feasible.json", "feasible", null },
        { "medical-oncology.unsatisfied.json", "unsatisfied", "scheduling.unassigned.capability-missing" },
        { "medical-pregnancy.feasible.json", "feasible", null },
        { "medical-pregnancy.unsatisfied.json", "unsatisfied", "scheduling.unassigned.capacity-envelope" },
    };

    [Theory(DisplayName = "%s parses, semantic-validates, and canonicalizes stably")]
    [MemberData(nameof(FixtureNames))]
    public void ParsesSemanticValidatesAndCanonicalizesStably(string name)
    {
        var validator = new FixtureSchedulingValidator();
        var fixture = validator.Validate(ReadFixture(name));
        var roundTrip = validator.Validate(fixture.CanonicalJson);

        Assert.Equal(fixture.CanonicalJson, roundTrip.CanonicalJson);
    }

    [Theory(DisplayName = "%s encodes the common scheduling primitives")]
    [MemberData(nameof(FixtureNames))]
    public void EncodesTheCommonSchedulingPrimitives(string name)
    {
        using var document = JsonDocument.Parse(ReadFixture(name));
        var root = document.RootElement;

        Assert.NotEmpty(root.GetProperty("anchors").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("windows").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("locations").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("phases").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("resources").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("capacityPools").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("constraints").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("demands").EnumerateArray());
        Assert.True(root.TryGetProperty("approvals", out _));
        Assert.True(root.TryGetProperty("commitment", out _));
    }

    [Theory(DisplayName = "%s declares the expected positive outcome or exact negative reason")]
    [MemberData(nameof(ExpectedOutcomes))]
    public void DeclaresTheExpectedPositiveOutcomeOrExactNegativeReason(
        string name,
        string expectedOutcome,
        string? expectedReason)
    {
        var fixture = new FixtureSchedulingValidator().Validate(ReadFixture(name));
        var result = new FixtureConstructivePlanner(TimeZoneInfo.Utc).Solve(fixture);

        Assert.Equal(expectedOutcome, fixture.ExpectedOutcome);
        if (expectedReason is null)
        {
            Assert.Equal(SolveStatus.Feasible, result.Status);
            Assert.NotEmpty(result.Plan);
            Assert.Empty(result.UnsatisfiedReasons);
            Assert.True(result.Score > 0);
        }
        else
        {
            Assert.Equal(SolveStatus.ProvenInfeasible, result.Status);
            Assert.Contains(expectedReason, result.UnsatisfiedReasons);
            Assert.True(result.Score < 0);
        }
    }

    [Fact(DisplayName = "matches across separate solver instances with contrasting ambient timezones")]
    public void CrossProcessTimezoneDeterminism()
    {
        var validator = new FixtureSchedulingValidator();
        foreach (var name in ExpectedFixtureNames)
        {
            var fixture = validator.Validate(ReadFixture(name));
            var utc = new FixtureConstructivePlanner(TimeZoneInfo.Utc).Solve(fixture).ToCanonicalJson();
            var contrasting = new FixtureConstructivePlanner(
                TimeZoneInfo.CreateCustomTimeZone("fixture-west", TimeSpan.FromHours(-8), "fixture-west", "fixture-west"))
                .Solve(fixture)
                .ToCanonicalJson();

            Assert.Equal(utc, contrasting);
        }
    }

    [Fact(DisplayName = "cites authored refs for every unsatisfied outcome")]
    public void ExplanationTraceRefsObligation()
    {
        var validator = new FixtureSchedulingValidator();
        foreach (var name in ExpectedFixtureNames.Where(name => name.EndsWith(".unsatisfied.json", StringComparison.Ordinal)))
        {
            var outcome = new FixtureConstructivePlanner(TimeZoneInfo.Utc).Solve(validator.Validate(ReadFixture(name)));
            Assert.NotEmpty(outcome.UnsatisfiedReasons);
            Assert.NotEmpty(outcome.ExplanationRefs);
        }
    }

    [Fact(DisplayName = "covers exactly the authored fixture set on disk")]
    public void StaticManifestGuard()
    {
        var actual = Directory.GetFiles(FixtureDirectory, "*.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedFixtureNames, actual);
    }

    // Ticket 288 slice 2 renamed the schema discriminator. The draft language did not change, so a
    // fixture authored under the pre-rename id is the same v0 draft and must still be accepted; both
    // spellings must produce the identical canonical fixture, and anything else must still be refused.
    [Fact(DisplayName = "accepts both the current and the pre-rename schema id, and refuses any other")]
    public void SchemaIdSuccessorIsAcceptedAlongsideItsPredecessor()
    {
        var validator = new FixtureSchedulingValidator();
        var current = ReadFixture(ExpectedFixtureNames[0]);
        Assert.Contains(FixtureSchedulingValidator.SchemaId, current, StringComparison.Ordinal);

        var legacy = current.Replace(
            $"\"{FixtureSchedulingValidator.SchemaId}\"",
            $"\"{FixtureSchedulingValidator.LegacySchemaId}\"",
            StringComparison.Ordinal);
        Assert.NotEqual(current, legacy);

        // Everything but the discriminator itself must canonicalize identically.
        Assert.Equal(
            validator.Validate(current).CanonicalJson,
            validator.Validate(legacy).CanonicalJson.Replace(
                FixtureSchedulingValidator.LegacySchemaId,
                FixtureSchedulingValidator.SchemaId,
                StringComparison.Ordinal));

        var unknown = current.Replace(
            $"\"{FixtureSchedulingValidator.SchemaId}\"", "\"someone.else/v0\"", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => validator.Validate(unknown));
    }

    private static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "Planning", "Fixtures");

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(FixtureDirectory, name));
}
