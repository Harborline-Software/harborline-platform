using System.Text.Json;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Data;

namespace Harborline.Experiments.SchedulingDecisionModel.Tests;

public sealed class CorpusTests
{
    private static readonly string CorpusDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "data", "corpus-v1");

    private static ManifestEntry[] Manifest() => File.ReadLines(Path.Combine(CorpusDir, "manifest.jsonl"))
        .Where(l => l.Length > 0)
        .Select(l => JsonSerializer.Deserialize<ManifestEntry>(l, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!)
        .ToArray();

    [Fact]
    public void FrozenManifest_MatchesItsRecordedHash()
    {
        // Oracle: the hash recorded at freeze time in manifest.sha256 and in control T-1053.
        const string Frozen = "ebaacd5b67ad36cc80e1f99b70af893ffb9f2a0dbf8e7d586bc553fde09e8c19";
        var text = File.ReadAllText(Path.Combine(CorpusDir, "manifest.jsonl"));

        Assert.Equal(Frozen, Canonical.Sha256(text));
    }

    [Fact]
    public void FrozenManifest_HasThePlannedSplitSizes_AndNoGroupSpansSplits()
    {
        var rows = Manifest();

        // Oracle: the corpus plan in T-1053 (3 seen families x 500 groups cut 60/20/20, 3 x 100
        // large groups, 2 unseen families x 400 groups; two variants per group).
        Assert.Equal(1800, rows.Count(r => r.Split == "train"));
        Assert.Equal(600, rows.Count(r => r.Split == "validation"));
        Assert.Equal(600, rows.Count(r => r.Split == "test-seen"));
        Assert.Equal(600, rows.Count(r => r.Split == "test-unseen-size"));
        Assert.Equal(1600, rows.Count(r => r.Split == "test-unseen-family"));
        Assert.All(rows.GroupBy(r => r.GroupId), g => Assert.Single(g.Select(r => r.Split).Distinct()));
        Assert.DoesNotContain(rows, r => r.Split is "train" or "validation" && r.Family is "shift-gaps" or "multi-skill");
        Assert.DoesNotContain(rows, r => r.Split is "train" or "validation" && r.SizeClass == "large");
    }

    [Fact]
    public void SampledInstances_RegenerateToTheirManifestHash()
    {
        foreach (var entry in Manifest().Where((_, i) => i % 97 == 0))
        {
            Assert.Equal(entry.InputSha256, Canonical.Sha256(Canonical.Json(Corpus.Regenerate(entry))));
        }
    }

    [Theory]
    [InlineData("jobshop-chains")]
    [InlineData("project-dag")]
    [InlineData("parallel-contention")]
    [InlineData("shift-gaps")]
    [InlineData("multi-skill")]
    public void EveryFamily_ProducesProfilesTheProductionCompilerAdmits(string family)
    {
        for (var i = 0; i < 25; i++)
        {
            var (profile, _) = Corpus.Instance(family, SizeClass.Medium, i, "tightened", Corpus.PilotSeed, "test");
            var compiled = new FiniteCandidateCompiler().Compile(profile);
            Assert.Equal(profile.Activities.Count, compiled.CandidatesByActivity.Count);
        }
    }

    [Fact]
    public void ShiftOnBlocks_AreLongerThanAnyJobShopOperation()
    {
        for (var i = 0; i < 25; i++)
        {
            var (profile, _) = Corpus.Instance("shift-gaps", SizeClass.Medium, i, "base", Corpus.PilotSeed, "test");
            foreach (var resource in profile.Resources)
            {
                var slots = resource.AvailableSlots.Order().ToArray();
                var run = 1;
                var shortest = int.MaxValue;
                for (var k = 1; k < slots.Length; k++)
                {
                    if (slots[k] == slots[k - 1] + 1)
                    {
                        run++;
                    }
                    else
                    {
                        // Interior blocks only; the first block may be cut short by the phase.
                        if (slots[k - run] != slots[0])
                        {
                            shortest = Math.Min(shortest, run);
                        }

                        run = 1;
                    }
                }

                // Oracle: spec-level requirement that an operation (max 4 slots) fits in a block.
                Assert.True(shortest == int.MaxValue || shortest >= 5, $"{resource.Id} has an interior block of {shortest}");
            }
        }
    }
}

public sealed class CorpusV2Tests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data");

    [Fact]
    public void V2Manifest_MatchesItsRecordedHash_AndKeepsEveryV1RowByteIdentical()
    {
        // Oracle: the hash recorded at the v2 freeze (control T-1053 log) and the v1 file itself.
        const string Frozen = "d6916b48c35a40c3ead99445a36e25499aeec174431df553f4c082b41d4341c3";
        var v2Text = File.ReadAllText(Path.Combine(Root, "corpus-v2", "manifest.jsonl"));
        Assert.Equal(Frozen, Canonical.Sha256(v2Text));

        var v2 = v2Text.Split('\n').Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
        var v1 = File.ReadLines(Path.Combine(Root, "corpus-v1", "manifest.jsonl")).Where(l => l.Length > 0).ToArray();
        Assert.All(v1, row => Assert.Contains(row, v2));
        Assert.Equal(800, v2.Count - v1.Length);
    }

    [Fact]
    public void V2Labels_AreDefiniteForEveryInstance()
    {
        var labels = File.ReadLines(Path.Combine(Root, "corpus-v2", "labels.jsonl")).Where(l => l.Length > 0).ToArray();
        Assert.Equal(6000, labels.Length);
        Assert.DoesNotContain(labels, l => l.Contains("\"oracleOutcome\":\"Unknown\"", StringComparison.Ordinal));
    }
}
