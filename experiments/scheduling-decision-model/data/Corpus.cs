using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Data;

/// <summary>One frozen manifest row. Membership and identity only; no labels.</summary>
public sealed record ManifestEntry(
    string InstanceId,
    string GroupId,
    string Family,
    string SizeClass,
    string Variant,
    string Seed,
    double Tightness,
    string GeneratorVersion,
    string ProfileVersion,
    string InputSha256,
    string Split,
    int Activities);

/// <summary>
/// The corpus plan for T-1053 sdm-02. Changing anything here after the manifest is frozen
/// changes every hash, which <c>verify</c> will report.
/// </summary>
public static class Corpus
{
    public const ulong MasterSeed = 20261003;
    public const ulong PilotSeed = 777;
    public const double TightenedFactor = 0.85;

    public const string Train = "train";
    public const string Validation = "validation";
    public const string TestSeen = "test-seen";
    public const string TestUnseenFamily = "test-unseen-family";
    public const string TestUnseenSize = "test-unseen-size";

    /// <summary>
    /// (family, size, group count, split policy). v2 (T-1053 owner ruling, before the holdout was
    /// opened) adds medium groups 200-399 to each held-out family, because pilot seeds projected
    /// too few holdout instances hard for the selected baseline. Every v1 row is unchanged.
    /// </summary>
    public static IEnumerable<(string Family, SizeClass Size, int Groups, string? FixedSplit)> Plan(int version = 1)
    {
        foreach (var family in Families.Seen)
        {
            yield return (family, SizeClass.Small, 250, null);
            yield return (family, SizeClass.Medium, 250, null);
            yield return (family, SizeClass.Large, 100, TestUnseenSize);
        }

        foreach (var family in Families.Unseen)
        {
            yield return (family, SizeClass.Small, 200, TestUnseenFamily);
            yield return (family, SizeClass.Medium, version >= 2 ? 400 : 200, TestUnseenFamily);
        }
    }

    public static (SchedulingProfile Profile, ManifestEntry Entry) Instance(
        string family, SizeClass size, int index, string variant, ulong master, string split)
    {
        var groupId = $"{family}.{size.ToString().ToLowerInvariant()}.{index:D4}";
        var seed = Rng.Derive(master, groupId);
        var baseTightness = Families.DrawTightness(family, new Rng(Rng.Derive(seed, "tightness")));
        var tightness = variant == "base" ? baseTightness : Math.Max(1.0, baseTightness * TightenedFactor);
        tightness = Math.Round(tightness, 4);
        var instanceId = $"{groupId}.{variant}";
        var profile = Families.Generate(family, size, seed, tightness, instanceId);
        var entry = new ManifestEntry(
            instanceId,
            groupId,
            family,
            size.ToString().ToLowerInvariant(),
            variant,
            seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tightness,
            Families.GeneratorVersion,
            "sdm.unit-resource.v1",
            Canonical.Sha256(Canonical.Json(profile)),
            split,
            profile.Activities.Count);
        return (profile, entry);
    }

    public static readonly string[] Variants = ["base", "tightened"];

    /// <summary>Builds the full manifest. Seen-family small/medium groups are shuffled and cut 60/20/20 by group.</summary>
    public static IReadOnlyList<ManifestEntry> Build(int version = 1)
    {
        var entries = new List<ManifestEntry>();
        foreach (var (family, size, groups, fixedSplit) in Plan(version))
        {
            var splits = fixedSplit is not null
                ? Enumerable.Repeat(fixedSplit, groups).ToArray()
                : GroupSplits(family, size, groups);
            for (var i = 0; i < groups; i++)
            {
                foreach (var variant in Variants)
                {
                    entries.Add(Instance(family, size, i, variant, MasterSeed, splits[i]).Entry);
                }
            }
        }

        return entries.OrderBy(e => e.InstanceId, StringComparer.Ordinal).ToArray();
    }

    private static string[] GroupSplits(string family, SizeClass size, int groups)
    {
        var order = Enumerable.Range(0, groups).ToArray();
        var rng = new Rng(Rng.Derive(MasterSeed, $"split/{family}/{size}"));
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = rng.Int(0, i);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var result = new string[groups];
        for (var rank = 0; rank < groups; rank++)
        {
            result[order[rank]] = rank < groups * 0.6 ? Train : rank < groups * 0.8 ? Validation : TestSeen;
        }

        return result;
    }

    public static SizeClass ParseSize(string size) => Enum.Parse<SizeClass>(size, ignoreCase: true);

    public static int IndexOf(ManifestEntry e) => int.Parse(e.GroupId[(e.GroupId.LastIndexOf('.') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    public static SchedulingProfile Regenerate(ManifestEntry e) =>
        Instance(e.Family, ParseSize(e.SizeClass), IndexOf(e), e.Variant, MasterSeed, e.Split).Profile;
}
