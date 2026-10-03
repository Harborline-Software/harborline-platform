using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Harborline.Experiments.SchedulingDecisionModel.Data;

// T-1053 corpus tool.
//   pilot  <family> <size> <count> [threads]   hardness preview on pilot seeds (never in the corpus)
//   freeze <dir>                               write manifest.jsonl and manifest.sha256
//   verify <dir>                               regenerate every instance and compare hashes
//   label  <dir> [threads]                     write labels.jsonl, disagreements.jsonl, label-summary.json
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var command = args.ElementAtOrDefault(0);

switch (command)
{
    case "pilot":
        Pilot(args[1], Corpus.ParseSize(args[2]), int.Parse(args[3], CultureInfo.InvariantCulture), Threads(args.ElementAtOrDefault(4)));
        return 0;
    case "freeze":
        Freeze(args[1], args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1);
        return 0;
    case "verify":
        return Verify(args[1]) ? 0 : 1;
    case "resolve-unknown":
        ResolveUnknown(args[1], double.Parse(args[2], CultureInfo.InvariantCulture));
        return 0;
    case "label":
        LabelAll(args[1], Threads(args.ElementAtOrDefault(2)), args.SkipWhile(a => a != "--reuse").Skip(1).FirstOrDefault());
        return 0;
    default:
        Console.Error.WriteLine("usage: pilot|freeze|verify|label");
        return 2;
}

int Threads(string? value) => value is null ? 8 : int.Parse(value, CultureInfo.InvariantCulture);

void Pilot(string family, SizeClass size, int count, int threads)
{
    var labels = new ConcurrentBag<InstanceLabel>();
    Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
    {
        foreach (var variant in Corpus.Variants)
        {
            var (profile, entry) = Corpus.Instance(family, size, i, variant, Corpus.PilotSeed, "pilot");
            labels.Add(Labeler.Label(entry, profile));
        }
    });

    Summarise($"{family}/{size}", labels.ToArray());
}

void Summarise(string title, IReadOnlyList<InstanceLabel> labels)
{
    var work = labels.Where(l => l.IncumbentStatus == "Feasible").Select(l => (double)l.IncumbentWork).Order().ToArray();
    double Q(double q) => work.Length == 0 ? double.NaN : work[(int)Math.Min(work.Length - 1, Math.Floor(q * work.Length))];
    Console.WriteLine($"{title}: n={labels.Count}");
    Console.WriteLine($"  oracle: {string.Join(", ", labels.GroupBy(l => l.OracleOutcome).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}");
    Console.WriteLine($"  method: {string.Join(", ", labels.GroupBy(l => l.OracleMethod).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}");
    Console.WriteLine($"  incumbent: {string.Join(", ", labels.GroupBy(l => l.IncumbentStatus).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}");
    Console.WriteLine($"  incumbent work when feasible: p50={Q(0.5)} p90={Q(0.9)} p99={Q(0.99)} max={(work.Length > 0 ? work[^1] : double.NaN)}");
    Console.WriteLine($"  incumbent work >= 10k: {labels.Count(l => l.IncumbentWork >= 10_000)}; >= 100k: {labels.Count(l => l.IncumbentWork >= 100_000)}; capped: {labels.Count(l => l.IncumbentStatus == "Indeterminate")}");
    Console.WriteLine($"  greedy: {string.Join(", ", labels.GroupBy(l => l.GreedyStatus).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}");
    var heavy = labels.Where(l => l.IncumbentWork >= 100_000).ToArray();
    if (heavy.Length > 0)
    {
        Console.WriteLine($"  incumbent throughput (work >= 100k): {heavy.Sum(l => (double)l.IncumbentWork) / heavy.Sum(l => l.IncumbentWallMs):F0} work units/ms");
    }

    Console.WriteLine($"  disagreements: {labels.Count(l => l.Disagreement)}");
    foreach (var d in labels.Where(l => l.Disagreement).Take(5))
    {
        Console.WriteLine($"    {d.InstanceId}: {d.DisagreementDetail}");
    }
}

void Freeze(string dir, int version)
{
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "manifest.jsonl");
    if (File.Exists(path))
    {
        throw new InvalidOperationException($"{path} already exists; a frozen manifest is never overwritten.");
    }

    var lines = Corpus.Build(version).Select(e => JsonSerializer.Serialize(e, json)).ToArray();
    File.WriteAllText(path, string.Join('\n', lines) + "\n");
    File.WriteAllText(Path.Combine(dir, "manifest.sha256"), $"{Canonical.Sha256(File.ReadAllText(path))}  manifest.jsonl\n");
    Console.WriteLine($"froze {lines.Length} instances");
}

IReadOnlyList<ManifestEntry> ReadManifest(string dir)
{
    var path = Path.Combine(dir, "manifest.jsonl");
    var expected = File.ReadAllText(Path.Combine(dir, "manifest.sha256")).Split(' ')[0];
    if (Canonical.Sha256(File.ReadAllText(path)) != expected)
    {
        throw new InvalidOperationException("manifest.jsonl does not match manifest.sha256");
    }

    return File.ReadLines(path)
        .Where(l => l.Length > 0)
        .Select(l => JsonSerializer.Deserialize<ManifestEntry>(l, json)!)
        .ToArray();
}

bool Verify(string dir)
{
    var bad = ReadManifest(dir)
        .AsParallel()
        .Where(e => Canonical.Sha256(Canonical.Json(Corpus.Regenerate(e))) != e.InputSha256)
        .Select(e => e.InstanceId)
        .ToArray();
    Console.WriteLine(bad.Length == 0 ? "all instance hashes match" : $"{bad.Length} mismatches, e.g. {string.Join(", ", bad.Take(5))}");
    return bad.Length == 0;
}

void LabelAll(string dir, int threads, string? reuseDir)
{
    var manifest = ReadManifest(dir);
    var labels = new ConcurrentBag<InstanceLabel>();

    // Reuse labels from an earlier corpus only where the instance id AND input hash match.
    var reused = reuseDir is null
        ? new Dictionary<string, InstanceLabel>(StringComparer.Ordinal)
        : File.ReadLines(Path.Combine(reuseDir, "labels.jsonl")).Where(l => l.Length > 0)
            .Select(l => JsonSerializer.Deserialize<InstanceLabel>(l, json)!)
            .ToDictionary(l => l.InstanceId, StringComparer.Ordinal);
    var todo = new List<ManifestEntry>();
    foreach (var entry in manifest)
    {
        if (reused.TryGetValue(entry.InstanceId, out var old) && old.InputSha256 == entry.InputSha256)
        {
            labels.Add(old);
        }
        else
        {
            todo.Add(entry);
        }
    }

    Console.WriteLine($"reused {labels.Count} labels; labelling {todo.Count}");
    var done = 0;
    Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = threads }, entry =>
    {
        var profile = Corpus.Regenerate(entry);
        if (Canonical.Sha256(Canonical.Json(profile)) != entry.InputSha256)
        {
            throw new InvalidOperationException($"{entry.InstanceId}: regenerated hash differs from manifest");
        }

        labels.Add(Labeler.Label(entry, profile));
        var n = Interlocked.Increment(ref done);
        if (n % 250 == 0)
        {
            Console.WriteLine($"labelled {n}/{manifest.Count}");
        }
    });

    var ordered = labels.OrderBy(l => l.InstanceId, StringComparer.Ordinal).ToArray();
    File.WriteAllText(Path.Combine(dir, "labels.jsonl"), string.Join('\n', ordered.Select(l => JsonSerializer.Serialize(l, json))) + "\n");
    File.WriteAllText(Path.Combine(dir, "disagreements.jsonl"), string.Join('\n', ordered.Where(l => l.Disagreement).Select(l => JsonSerializer.Serialize(l, json))) + "\n");

    var bySplit = manifest.ToDictionary(e => e.InstanceId, e => e.Split);
    foreach (var group in ordered.GroupBy(l => bySplit[l.InstanceId]).OrderBy(g => g.Key))
    {
        Summarise(group.Key, group.ToArray());
    }
}

// Re-runs CP-SAT with a longer limit for Unknown labels only; provenance becomes "cpsat-<seconds>s".
void ResolveUnknown(string dir, double seconds)
{
    var manifest = ReadManifest(dir).ToDictionary(e => e.InstanceId, StringComparer.Ordinal);
    var path = Path.Combine(dir, "labels.jsonl");
    var labels = File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<InstanceLabel>(l, json)!).ToArray();
    for (var i = 0; i < labels.Length; i++)
    {
        if (labels[i].OracleOutcome != "Unknown")
        {
            continue;
        }

        var entry = manifest[labels[i].InstanceId];
        var profile = Corpus.Regenerate(entry);
        var result = new Harborline.Experiments.SchedulingDecisionModel.Baselines.CpSatFeasibility().Solve(profile, seconds, seed: 0);
        var checkedOk = result.Outcome != Harborline.Experiments.SchedulingDecisionModel.Baselines.CpSatOutcome.Feasible ||
            new Harborline.Experiments.SchedulingDecisionModel.Oracle.PlanChecker(profile).Check(result.Plan).IsFeasible;
        var outcome = result.Outcome switch
        {
            Harborline.Experiments.SchedulingDecisionModel.Baselines.CpSatOutcome.Feasible when checkedOk => "Feasible",
            Harborline.Experiments.SchedulingDecisionModel.Baselines.CpSatOutcome.Infeasible => "Infeasible",
            _ => "Unknown",
        };
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{entry.InstanceId}: cpsat {result.Outcome} in {result.WallSeconds:F1} s, plan checked {checkedOk} -> {outcome}"));
        labels[i] = labels[i] with
        {
            OracleMethod = outcome == "Unknown" ? "none" : $"cpsat-{seconds:F0}s",
            OracleOutcome = outcome,
            CpSatOutcome = result.Outcome.ToString(),
            CpSatWallMs = Math.Round(result.WallSeconds * 1000, 3),
            CpSatPlanChecked = checkedOk,
        };
    }

    File.WriteAllText(path, string.Join('\n', labels.Select(l => JsonSerializer.Serialize(l, json))) + "\n");
}
