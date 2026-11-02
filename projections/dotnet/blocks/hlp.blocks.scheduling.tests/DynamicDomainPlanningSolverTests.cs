using System.Text.Json;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Data;
using Harborline.Experiments.SchedulingDecisionModel.Engine;
using Harborline.Experiments.SchedulingDecisionModel.Evaluation;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;
using Harborline.Experiments.SchedulingDecisionModel.Tests;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class DynamicDomainPlanningSolverTests
{
    private const int CorpusBudget = 1_826_301;
    private const string ManifestHash = "d6916b48c35a40c3ead99445a36e25499aeec174431df553f4c082b41d4341c3";

    [Theory]
    [InlineData(0, SolveStatus.Indeterminate, 0)]
    [InlineData(1, SolveStatus.Feasible, 1)]
    [InlineData(int.MaxValue, SolveStatus.Feasible, 1)]
    public void CachedSelection_CanFinishAtTheBudget(int budget, SolveStatus status, int work)
    {
        var profile = new ProfileBuilder().Resource("R", "c", 0, 0)
            .Activity("X", 1, 0, 0, ("c", 1)).Build();
        var proposal = Solve(profile, budget);
        AssertOutcome(proposal, status, work);
        if (status == SolveStatus.Feasible)
        {
            Assert.Equal(new[] { "X@0-1:R" }, proposal.Assignments.Select(Key));
            Assert.True(new PlanChecker(profile).Check(proposal.Assignments).IsFeasible);
        }
    }

    [Fact]
    public void OrdinalTie_IgnoresInputOrder_AndSolvesRepeatedly()
    {
        var profile = new ProfileBuilder().Resource("R", "c", 0, 1)
            .Activity("Y", 1, 0, 1, ("c", 1)).Activity("X", 1, 0, 1, ("c", 1)).Build();
        var compiled = new FiniteCandidateCompiler().Compile(profile);
        Assert.Equal(new[] { "Y", "X" }, compiled.Activities.Select(activity => activity.Id));
        var solver = new DeterministicExhaustiveProofSolver();
        AssertOutcome(solver.Solve(compiled, 5), SolveStatus.Indeterminate, 5);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var proposal = solver.Solve(compiled, 6);
            AssertOutcome(proposal, SolveStatus.Feasible, 6);
            Assert.Equal(new[] { "X@0-1:R", "Y@1-2:R" }, proposal.Assignments.Select(Key));
            Assert.Equal("prototype.exhaustive-proof.v2", proposal.SolverId);
            Assert.Equal("prototype.exhaustive-proof.v2:test:v1:Feasible", proposal.ProposalId);
            Assert.Equal(SolverGuarantee.FeasibleProof, proposal.Guarantee);
            Assert.True(new PlanChecker(profile).Check(proposal.Assignments).IsFeasible);
        }
    }

    [Theory]
    [InlineData(16, SolveStatus.Indeterminate)]
    [InlineData(17, SolveStatus.ProvenInfeasible)]
    public void Backtracking_ProvesInfeasibilityAtSeventeenChecks(int budget, SolveStatus status)
    {
        var profile = Profiles.OneResourceTwoUnits().Before("X", "Y", 2).Build();
        AssertOutcome(Solve(profile, budget), status, budget);
    }

    [Theory]
    [InlineData(12, SolveStatus.Indeterminate)]
    [InlineData(13, SolveStatus.Feasible)]
    public void DynamicDomain_FindsTheConstrainedWitnessAtThirteenChecks(int budget, SolveStatus status)
    {
        var profile = new ProfileBuilder().Resource("R", "c", 0, 4)
            .Activity("A", 2, 0, 0, ("c", 1)).Activity("B", 1, 2, 4, ("c", 1))
            .Activity("C", 1, 0, 3, ("c", 1)).Build();
        var proposal = Solve(profile, budget);
        AssertOutcome(proposal, status, budget);
        if (status == SolveStatus.Feasible)
        {
            Assert.Equal(new[] { "A@0-2:R", "B@3-4:R", "C@2-3:R" }, proposal.Assignments.Select(Key));
            Assert.True(new PlanChecker(profile).Check(proposal.Assignments).IsFeasible);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicatePlacements_KeepSeparateReferenceCharges(bool equalRecords)
    {
        var profile = new ProfileBuilder().Resource("R1", "c", 0, 0).Resource("R2", "c", 0, 0)
            .Activity("X", 1, 0, 0, ("c", 1), ("c", 1)).Build();
        var problem = new FiniteCandidateCompiler().Compile(profile);
        Assert.Equal(2, problem.CandidatesByActivity["X"].Count);
        Assert.Single(ExhaustiveEnumerator.Placements(profile, new PlanChecker(profile), profile.Activities[0]));
        if (equalRecords)
        {
            var first = problem.CandidatesByActivity["X"][0];
            var second = first with { };
            Assert.Equal(first, second);
            Assert.NotSame(first, second);
            problem = problem with
            {
                CandidatesByActivity = new Dictionary<string, IReadOnlyList<AssignmentCandidate>>(StringComparer.Ordinal)
                {
                    ["X"] = [first, second],
                },
            };
        }

        var solver = new DeterministicExhaustiveProofSolver();
        AssertOutcome(solver.Solve(problem, 1), SolveStatus.Indeterminate, 1);
        var proposal = solver.Solve(problem, 2);
        AssertOutcome(proposal, SolveStatus.Feasible, 2);
        Assert.True(new PlanChecker(profile).Check(proposal.Assignments).IsFeasible);
    }

    [Fact]
    public void Guards_PreservePinPrecedenceAndEmptyProblemSemantics()
    {
        var empty = new ProfileBuilder().Build();
        AssertOutcome(Solve(empty, 1), SolveStatus.Feasible, 0);
        AssertOutcome(Solve(empty, 0), SolveStatus.Indeterminate, 0);
        var noResource = new ProfileBuilder().Activity("X", 1, 0, 0, ("c", 1)).Build();
        AssertOutcome(Solve(noResource, 1), SolveStatus.ProvenInfeasible, 0);

        foreach (var pins in new[]
        {
            empty.FactSetPins.Where(p => p.FactSet != "time-windows").ToArray(),
            empty.FactSetPins.Select(p => p with { IsComplete = p.FactSet != "time-windows" }).ToArray(),
        })
        {
            foreach (var budget in new[] { -1, 0, 1 })
            {
                var proposal = Solve(empty with { FactSetPins = pins }, budget);
                Assert.Equal(SolveStatus.Indeterminate, proposal.Status);
                Assert.Equal(0, proposal.WorkUnits);
                Assert.Empty(proposal.Assignments);
                Assert.Equal("PINNED_FACTS_INCOMPLETE", proposal.ReasonCode);
                Assert.Equal(new[] { "time-windows" }, proposal.IncompleteFactSets);
            }
        }
    }

    public static TheoryData<string, long> HandCounted => new()
    {
        { "two-units", 6 },
        { "two-units-precedence", 3 },
        { "two-units-gap-1", 1 },
        { "two-units-gap-2", 0 },
        { "two-resources-swap", 2 },
        { "quantity-2-clash", 0 },
        { "quantity-2-shift", 2 },
        { "no-capable-resource", 0 },
        { "two-jobs-infeasible", 0 },
    };

    [Theory]
    [MemberData(nameof(HandCounted))]
    public void IndependentEnumerator_AndProductionMatchHandCounts(string name, long count)
    {
        var profile = HandCase(name);
        var oracle = new ExhaustiveEnumerator().Solve(profile, solutionCap: long.MaxValue);
        Assert.Equal(count, oracle.SolutionCount);
        Assert.True(oracle.SolutionCountIsExact);
        Assert.Equal(count == 0 ? OracleOutcome.Infeasible : OracleOutcome.Feasible, oracle.Outcome);
        var proposal = Solve(profile, 1_000_000);
        Assert.Equal(count == 0 ? SolveStatus.ProvenInfeasible : SolveStatus.Feasible, proposal.Status);
        if (proposal.Status == SolveStatus.Feasible)
        {
            Assert.True(new PlanChecker(profile).Check(proposal.Assignments).IsFeasible);
        }
    }

    [Fact]
    public void IndependentEnumerator_DoesNotTurnAnUnfinishedProofIntoInfeasibility()
    {
        var result = new ExhaustiveEnumerator().Solve(HandCase("two-units-gap-2"), nodeCap: 2);
        Assert.Equal(OracleOutcome.Unknown, result.Outcome);
        Assert.False(result.SolutionCountIsExact);
        Assert.Equal(OracleOutcome.Unknown,
            new ExhaustiveEnumerator().Solve(Profiles.OneResourceTwoUnits().Build(complete: false)).Outcome);
    }

    [Fact]
    public void GroupBootstrap_PinsIdenticalAndUniformlyBetterPairs()
    {
        Assert.Equal(new Stats.Interval(0, 0, 0),
            Stats.SolvedDifference([("a", true, true), ("a", false, false), ("b", true, true)], 1054));
        Assert.Equal(new Stats.Interval(100, 100, 100),
            Stats.SolvedDifference([("a", true, false), ("a", true, false), ("b", true, false)], 1054));
        Assert.Equal(new Stats.Interval(1, 1, 1),
            Stats.WorkRatio([("a", 2.0, 2.0), ("a", 4.0, 4.0), ("b", 1.0, 1.0)], 1054));
    }

    [Fact]
    public void FrozenCorpus_MatchesDynamicReferenceAndIndependentTruth()
    {
        var (manifest, labels) = LoadFrozenCorpus(Path.Combine(AppContext.BaseDirectory, "corpus-v2"));
        var full = Environment.GetEnvironmentVariable("HLP_T1054_FULL_CORPUS") == "1";
        var rows = SelectRows(manifest, full);
        Assert.Equal(full ? 6000 : 200, rows.Length);
        foreach (var row in rows)
        {
            CheckCase(row, labels[row.InstanceId], CorpusBudget);
        }
    }

    private sealed record TruthRow(string InstanceId, string InputSha256, string OracleOutcome);

    private static (ManifestEntry[] Manifest, Dictionary<string, TruthRow> Labels) LoadFrozenCorpus(string directory)
    {
        var manifestText = File.ReadAllText(Path.Combine(directory, "manifest.jsonl"));
        Assert.Equal(ManifestHash, Canonical.Sha256(manifestText));
        Assert.Equal(ManifestHash, File.ReadAllText(Path.Combine(directory, "manifest.sha256")).Split(' ')[0].Trim());
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var manifest = manifestText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<ManifestEntry>(line, options)!).ToArray();
        var labels = new Dictionary<string, TruthRow>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(directory, "labels.jsonl")).Where(line => line.Length > 0))
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            Assert.False(row.GetProperty("disagreement").GetBoolean());
            var truth = new TruthRow(row.GetProperty("instanceId").GetString()!,
                row.GetProperty("inputSha256").GetString()!, row.GetProperty("oracleOutcome").GetString()!);
            Assert.Contains(truth.OracleOutcome, new[] { "Feasible", "Infeasible" });
            Assert.True(labels.TryAdd(truth.InstanceId, truth));
        }

        Assert.Equal(6000, manifest.Length);
        Assert.Equal(6000, manifest.Select(row => row.InstanceId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(6000, labels.Count);
        foreach (var row in manifest)
        {
            Assert.True(labels.TryGetValue(row.InstanceId, out var truth));
            Assert.Equal(row.InputSha256, truth!.InputSha256);
        }

        return (manifest, labels);
    }

    private static ManifestEntry[] SelectRows(ManifestEntry[] all, bool full)
    {
        var cells = all.GroupBy(row => (row.Family, row.SizeClass, row.Split)).ToArray();
        Assert.Equal(25, cells.Length);
        var selected = new List<ManifestEntry>();
        foreach (var cell in cells)
        {
            var groups = cell.GroupBy(row => row.GroupId).ToArray();
            Assert.True(groups.Length >= 4);
            foreach (var group in groups)
            {
                Assert.Equal(new[] { "base", "tightened" }, group.Select(row => row.Variant).Order(StringComparer.Ordinal));
            }

            selected.AddRange((full ? groups : groups.OrderBy(group => Rng.Derive(1054, group.Key))
                .ThenBy(group => group.Key, StringComparer.Ordinal).Take(4)).SelectMany(group => group));
        }

        Assert.Equal(25, selected.GroupBy(row => (row.Family, row.SizeClass, row.Split)).Count());
        return selected.OrderBy(row => row.InstanceId, StringComparer.Ordinal).ToArray();
    }

    private static void CheckCase(ManifestEntry entry, TruthRow truth, int budget)
    {
        var profile = Corpus.Regenerate(entry);
        var hash = Canonical.Sha256(Canonical.Json(profile));
        Assert.Equal(entry.InputSha256, hash);
        Assert.Equal(truth.InputSha256, hash);
        var compiled = new FiniteCandidateCompiler().Compile(profile);
        var proposal = new DeterministicExhaustiveProofSolver().Solve(compiled, budget);
        var reference = new SearchEngine().Run(compiled, new DynamicDomainPolicy(), budget, dedupe: false);
        Assert.Equal(reference.Status, proposal.Status);
        Assert.Equal(reference.Work, (long)proposal.WorkUnits);
        Assert.Equal(reference.ReasonCode, proposal.ReasonCode);
        Assert.Equal(reference.Assignments.Select(Key), proposal.Assignments.Select(Key));
        Assert.Equal(0, reference.Fallbacks);
        Assert.InRange(proposal.WorkUnits, 0, budget);
        Assert.Contains(proposal.Status, new[] { SolveStatus.Feasible, SolveStatus.ProvenInfeasible, SolveStatus.Indeterminate });
        if (proposal.Status == SolveStatus.Feasible)
        {
            Assert.Equal("Feasible", truth.OracleOutcome);
            Assert.True(new PlanChecker(profile).Check(proposal.Assignments).IsFeasible, entry.InstanceId);
        }
        else
        {
            Assert.Empty(proposal.Assignments);
            if (proposal.Status == SolveStatus.ProvenInfeasible)
            {
                Assert.Equal("Infeasible", truth.OracleOutcome);
            }
        }
    }

    private static SchedulingProposal Solve(SchedulingProfile profile, int budget) =>
        new DeterministicExhaustiveProofSolver().Solve(new FiniteCandidateCompiler().Compile(profile), budget);

    private static void AssertOutcome(SchedulingProposal proposal, SolveStatus status, int work)
    {
        Assert.Equal(status, proposal.Status);
        Assert.Equal(work, proposal.WorkUnits);
        Assert.Equal(status switch
        {
            SolveStatus.Feasible => "ALL_HARD_CONSTRAINTS_SATISFIED",
            SolveStatus.ProvenInfeasible => "SEARCH_SPACE_EXHAUSTED",
            _ => "WORK_BUDGET_EXHAUSTED",
        }, proposal.ReasonCode);
        Assert.Empty(proposal.IncompleteFactSets);
        if (status != SolveStatus.Feasible)
        {
            Assert.Empty(proposal.Assignments);
        }
    }

    private static string Key(AssignmentCandidate c) => $"{c.ActivityId}@{c.StartSlot}-{c.EndSlotExclusive}:{string.Join('|', c.ResourceIds)}";

    private static SchedulingProfile HandCase(string name) => name switch
    {
        "two-units" => Profiles.OneResourceTwoUnits().Build(),
        "two-units-precedence" => Profiles.OneResourceTwoUnits().Before("X", "Y").Build(),
        "two-units-gap-1" => Profiles.OneResourceTwoUnits().Before("X", "Y", 1).Build(),
        "two-units-gap-2" => Profiles.OneResourceTwoUnits().Before("X", "Y", 2).Build(),
        "two-resources-swap" => new ProfileBuilder().Resource("R1", "c", 0, 3).Resource("R2", "c", 0, 3)
            .Activity("X", 2, 0, 0, ("c", 1)).Activity("Y", 2, 0, 0, ("c", 1)).Build(),
        "quantity-2-clash" => new ProfileBuilder().Resource("R1", "c", 0, 3).Resource("R2", "c", 0, 3)
            .Activity("X", 1, 0, 0, ("c", 2)).Activity("Y", 1, 0, 0, ("c", 1)).Build(),
        "quantity-2-shift" => new ProfileBuilder().Resource("R1", "c", 0, 3).Resource("R2", "c", 0, 3)
            .Activity("X", 1, 0, 0, ("c", 2)).Activity("Y", 1, 0, 1, ("c", 1)).Build(),
        "no-capable-resource" => new ProfileBuilder().Resource("R1", "c", 0, 3).Activity("X", 1, 0, 0, ("d", 1)).Build(),
        "two-jobs-infeasible" => Profiles.TwoJobs(4),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
