using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Scheduling.Tests;

// Oracles: literal conformance cases sourced to DES-0022 section 4, DES-0057 ck-1/cc-1 and control #1358.
public sealed class SchedulingAuthoringRefusalTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Theory(DisplayName = "scheduling-auth-17: unsupported configuration refuses before storage")]
    [InlineData("unsupported-configuration")]
    public Task UnsupportedConfiguration(string id) => CheckWriteAsync(id);

    [Theory(DisplayName = "scheduling-auth-18: authoring reads the engine's admitted module set")]
    [InlineData("unadmitted-module")]
    public Task UnadmittedModule(string id) => CheckWriteAsync(id);

    [Theory(DisplayName = "scheduling-auth-21: recovery replanning refuses before storage")]
    [InlineData("recovery-replanning")]
    public Task RecoveryReplanning(string id) => CheckWriteAsync(id);

    [Theory(DisplayName = "scheduling-auth-23: a regulator-rule-set requirement refuses before storage")]
    [InlineData("regulator-rule-set")]
    public Task RegulatorRuleSet(string id) => CheckWriteAsync(id);

    [Theory(DisplayName = "scheduling-auth-24: another plan's output refuses before storage")]
    [InlineData("upstream-plan")]
    public Task UpstreamPlan(string id) => CheckWriteAsync(id);

    [Theory(DisplayName = "scheduling-auth-25: within-plan operation dependencies and ordinary replanning admit")]
    [InlineData("within-plan-precedence")]
    [InlineData("ordinary-replan-and-citation")]
    public Task WithinPlanDependencies(string id) => CheckWriteAsync(id);

    [Theory(DisplayName = "recurrence-expansion-cc-1: the producer's bounded parser refuses unsupported RRULEs at save")]
    [InlineData("rrule-hourly")]
    [InlineData("rrule-unknown-part")]
    [InlineData("rrule-unsupported-month-day")]
    [InlineData("rrule-week-start")]
    [InlineData("rrule-count-and-until")]
    public Task RecurrenceSubset(string id) => CheckWriteAsync(id);

    [Theory(DisplayName = "scheduling-auth-28: host-resolved K9 edges require dependency and compatible exposure")]
    [InlineData("dependency-undeclared")]
    [InlineData("target-unexposed")]
    [InlineData("version-mismatch")]
    [InlineData("digest-mismatch")]
    [InlineData("both-undeclared")]
    [InlineData("declared-and-exposed")]
    [InlineData("same-package")]
    public void CrossPackageEdges(string id)
    {
        var fixture = Case(id);
        var consumer = ScheduleDefinitionJson.Deserialize(fixture.GetProperty("definition").GetRawText());
        var report = SchedulingCrossPackageAuthoring.Check(consumer,
            fixture.GetProperty("references").Deserialize<CrossPackageReference[]>(Options)!,
            fixture.GetProperty("exposures").Deserialize<PackageExposure[]>(Options)!);
        Assert.Equal(DefinitionAdmissionPhase.Author, report.Stage);
        Assert.Equal(Expected(fixture), report.Refusals);
        Reasons(fixture);
    }

    [Theory(DisplayName = "scheduling-eng-22: validate refuses raw compiled writers at author, publish and install")]
    [InlineData(DefinitionAdmissionPhase.Author)]
    [InlineData(DefinitionAdmissionPhase.Publish)]
    [InlineData(DefinitionAdmissionPhase.Install)]
    public void ValidateIsStructural(DefinitionAdmissionPhase phase)
    {
        var body = Case("recovery-replanning").GetProperty("definition").GetRawText();
        var refusals = ScheduleDefinitionCatalogue.Admission(new(new("tenant-a", DefinitionKind.Schedules, "inspection"),
            "inspection@1.0.0", "1.0.0", body), phase);
        Assert.Contains(new DefinitionRefusal("scheduling.definition.recovery_replanning_unsupported", "/recovery_replanning"), refusals);
    }

    private static async Task CheckWriteAsync(string id)
    {
        var fixture = Case(id);
        var expected = Expected(fixture);
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission,
        });
        var key = new DefinitionKey("tenant-a", DefinitionKind.Schedules, "inspection");
        var body = fixture.GetProperty("definition").GetRawText();
        var document = new DefinitionDocument(key, "inspection@1.0.0", "1.0.0", body);
        if (expected.Length == 0)
        {
            await store.SaveDraftAsync(document, 0, "draft");
            Assert.Single(await store.ListHistoryAsync(key));
        }
        else
        {
            var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() => store.SaveDraftAsync(document, 0, "draft").AsTask());
            Assert.Equal(DefinitionAdmissionPhase.Author, refusal.Stage);
            Assert.Equal(expected, refusal.Refusals);
            Assert.Empty(await store.ListHistoryAsync(key));
            Assert.Null(await store.GetPublishedHeadAsync(key));
            Reasons(fixture);
            if (id is "recovery-replanning" or "regulator-rule-set" or "upstream-plan")
            {
                var typed = ScheduleDefinitionJson.Deserialize(body);
                var typedRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
                    new ScheduleDefinitionCatalogue(store).SaveDraftAsync("tenant-a", "inspection", "1.0.0", typed, 0, "typed").AsTask());
                Assert.Equal(expected, typedRefusal.Refusals);
                Assert.Empty(await store.ListHistoryAsync(key));
            }
        }
    }

    private static DefinitionRefusal[] Expected(JsonElement fixture)
        => fixture.GetProperty("refusals").Deserialize<DefinitionRefusal[]>(Options)!;

    private static void Reasons(JsonElement fixture)
    {
        if (Expected(fixture).Length == 0) return;
        Assert.False(string.IsNullOrWhiteSpace(fixture.GetProperty("missing").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(fixture.GetProperty("required").GetString()));
    }

    private static JsonElement Case(string id)
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "authoring-refusals.json")));
        return corpus.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id).Clone();
    }
}
