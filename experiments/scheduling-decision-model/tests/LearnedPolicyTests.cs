using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Engine;
using Harborline.Experiments.SchedulingDecisionModel.Models;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;

namespace Harborline.Experiments.SchedulingDecisionModel.Tests;

public sealed class LearnedPolicyTests
{
    private static readonly string Trained = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "models", "trained");

    private static LinearModel Activity(double bias = 0) => new(
        "activity", FeatureContext.Version, FeatureContext.ActivityNames,
        new double[FeatureContext.ActivityNames.Length],
        Enumerable.Repeat(1.0, FeatureContext.ActivityNames.Length).ToArray(),
        Enumerable.Repeat(1.0, FeatureContext.ActivityNames.Length).ToArray(),
        bias, "test", 0);

    [Theory]
    [InlineData(5, SolveStatus.Feasible)]
    [InlineData(4, SolveStatus.ProvenInfeasible)]
    public void NonFiniteScores_FallBack_AndKeepTheHandProvedOutcome(int job2Inspect, SolveStatus handProved)
    {
        var profile = Profiles.TwoJobs(job2Inspect);
        var problem = new FiniteCandidateCompiler().Compile(profile);
        var policy = new LearnedPolicy(Activity(double.NaN), null, 1, 2, 2, 1.25);

        var result = new SearchEngine().Run(problem, policy, 1_000_000, dedupe: true);

        Assert.Equal(handProved, result.Status);
        Assert.True(result.Fallbacks > 0);
        Assert.StartsWith("SELECT_THREW", result.FirstFallbackReason, StringComparison.Ordinal);
        if (result.Status == SolveStatus.Feasible)
        {
            Assert.Empty(new PlanChecker(profile).Check(result.Assignments).Violations);
        }
    }

    [Fact]
    public void LoadingRefusesANonFiniteOrMismatchedModel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdm-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, Activity().ToJson().Replace("\"bias\": 0", "\"bias\": \"NaN\"", StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(() => LinearModel.Load(path, "activity", FeatureContext.ActivityNames));
        File.WriteAllText(path, Activity().ToJson());
        Assert.Throws<InvalidDataException>(() => LinearModel.Load(path, "value", FeatureContext.CandidateNames));
        File.Delete(path);
    }

    [Fact]
    public void FrozenModel_MatchesItsRecordedHash()
    {
        // Oracle: the hash written into frozen.json (and control T-1053) at freeze time.
        var model = LinearModel.Load(Path.Combine(Trained, "activity-l2-1e-2.json"), "activity", FeatureContext.ActivityNames);
        Assert.Equal("1bdd9d809ff6ee84e15e53ec49f9302975884750f714f981c19628287681ce91", model.Sha256());
    }

    [Theory]
    [InlineData(5, SolveStatus.Feasible)]
    [InlineData(4, SolveStatus.ProvenInfeasible)]
    public void FrozenPolicy_ReachesTheHandProvedOutcome(int job2Inspect, SolveStatus handProved)
    {
        var profile = Profiles.TwoJobs(job2Inspect);
        var model = LinearModel.Load(Path.Combine(Trained, "activity-l2-1e-2.json"), "activity", FeatureContext.ActivityNames);
        var result = new SearchEngine().Run(new FiniteCandidateCompiler().Compile(profile), new LearnedPolicy(model, null, 1, 2, 2, 1.25), 1_000_000, dedupe: true);

        Assert.Equal(handProved, result.Status);
        Assert.Equal(0, result.Fallbacks);
        if (result.Status == SolveStatus.Feasible)
        {
            Assert.Empty(new PlanChecker(profile).Check(result.Assignments).Violations);
        }
    }
}
