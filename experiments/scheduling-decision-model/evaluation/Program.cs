using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Data;
using Harborline.Experiments.SchedulingDecisionModel.Evaluation;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;

// T-1053 evaluation tool.
//   equivalence <corpus> <split>                   engine "incumbent" vs production solver, exact
//   calibrate   <corpus> [count]                   work units per 0.5 s for the incumbent, single thread, train split
//   baselines   <corpus> <split> <budget> <out> [threads] [--phase3]
//   report      <corpus> <results.jsonl> <budget>
// Test splits are refused unless --phase3 is given: the holdout is opened once, in Phase 3.
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var inv = CultureInfo.InvariantCulture;

switch (args.ElementAtOrDefault(0))
{
    case "equivalence": return Equivalence(args[1], args[2]) ? 0 : 1;
    case "calibrate": Calibrate(args[1], args.Length > 2 ? int.Parse(args[2], inv) : 25); return 0;
    case "baselines":
        Baselines(args[1], args[2], long.Parse(args[3], inv), args[4],
            args.Length > 5 && !args[5].StartsWith("--", StringComparison.Ordinal) ? int.Parse(args[5], inv) : 8,
            args.Contains("--phase3"));
        return 0;
    case "report": Report(args[1], args[2], long.Parse(args[3], inv)); return 0;
    case "pilot-hardness":
        PilotHardness(args[1], Corpus.ParseSize(args[2]), int.Parse(args[3], inv), long.Parse(args[4], inv));
        return 0;
    default:
        Console.Error.WriteLine("usage: equivalence|calibrate|baselines|report");
        return 2;
}

(ManifestEntry[] Manifest, Dictionary<string, string> Oracle) Load(string dir)
{
    var manifest = File.ReadLines(Path.Combine(dir, "manifest.jsonl")).Where(l => l.Length > 0)
        .Select(l => JsonSerializer.Deserialize<ManifestEntry>(l, json)!).ToArray();
    var expected = File.ReadAllText(Path.Combine(dir, "manifest.sha256")).Split(' ')[0];
    if (Canonical.Sha256(File.ReadAllText(Path.Combine(dir, "manifest.jsonl"))) != expected)
    {
        throw new InvalidOperationException("manifest hash mismatch");
    }

    var oracle = File.ReadLines(Path.Combine(dir, "labels.jsonl")).Where(l => l.Length > 0)
        .Select(l => JsonDocument.Parse(l).RootElement)
        .ToDictionary(e => e.GetProperty("instanceId").GetString()!, e => e.GetProperty("oracleOutcome").GetString()!);
    return (manifest, oracle);
}

SchedulingProfile Profile(ManifestEntry e)
{
    var p = Corpus.Regenerate(e);
    return Canonical.Sha256(Canonical.Json(p)) == e.InputSha256 ? p : throw new InvalidOperationException($"{e.InstanceId} hash mismatch");
}

void GuardSplit(string split, bool phase3)
{
    if (split.StartsWith("test", StringComparison.Ordinal) && !phase3)
    {
        throw new InvalidOperationException($"{split} is a frozen holdout; it opens once, in Phase 3 (--phase3).");
    }
}

bool Equivalence(string dir, string split)
{
    GuardSplit(split, false);
    var (manifest, _) = Load(dir);
    var bad = new ConcurrentBag<string>();
    var rows = manifest.Where(e => e.Split == split).ToArray();
    Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = 8 }, e =>
    {
        var compiled = new FiniteCandidateCompiler().Compile(Profile(e));
        foreach (var budget in new[] { 1_000L, 50_000L, Labeler.IncumbentWorkCap })
        {
            var production = new DeterministicExhaustiveProofSolver().Solve(compiled, (int)budget);
            var engine = new SearchEngine().Run(compiled, new IncumbentPolicy(), budget);
            var same = production.Status == engine.Status &&
                production.WorkUnits == engine.Work &&
                production.ReasonCode == engine.ReasonCode &&
                production.Assignments.Select(Key).SequenceEqual(engine.Assignments.Select(Key));
            if (!same)
            {
                bad.Add($"{e.InstanceId}@{budget}: production {production.Status}/{production.WorkUnits} engine {engine.Status}/{engine.Work}");
            }
        }
    });

    Console.WriteLine(bad.IsEmpty
        ? $"equivalent on {rows.Length} instances x 3 budgets"
        : $"{bad.Count} differences, e.g.\n  {string.Join("\n  ", bad.Take(5))}");
    return bad.IsEmpty;

    static string Key(AssignmentCandidate c) => $"{c.ActivityId}@{c.StartSlot}-{c.EndSlotExclusive}:{string.Join('|', c.ResourceIds)}";
}

void Calibrate(string dir, int count)
{
    var (manifest, _) = Load(dir);
    var labels = File.ReadLines(Path.Combine(dir, "labels.jsonl")).Where(l => l.Length > 0)
        .Select(l => JsonDocument.Parse(l).RootElement)
        .Where(e => e.GetProperty("incumbentStatus").GetString() == "Indeterminate")
        .Select(e => e.GetProperty("instanceId").GetString()!)
        .ToHashSet(StringComparer.Ordinal);
    var sample = manifest.Where(e => e.Split == "train" && labels.Contains(e.InstanceId)).Take(count).ToArray();
    const long probe = 2_000_000;
    var rates = new List<double>();
    foreach (var e in sample)
    {
        var compiled = new FiniteCandidateCompiler().Compile(Profile(e));
        new SearchEngine().Run(compiled, new IncumbentPolicy(), 10_000); // warm-up
        var watch = Stopwatch.StartNew();
        var r = new SearchEngine().Run(compiled, new IncumbentPolicy(), probe);
        watch.Stop();
        rates.Add(r.Work / watch.Elapsed.TotalMilliseconds);
    }

    rates.Sort();
    var median = rates[rates.Count / 2];
    Console.WriteLine($"incumbent engine, single thread, {rates.Count} capped train instances: work units/ms p10={rates[rates.Count / 10]:F0} median={median:F0} p90={rates[rates.Count * 9 / 10]:F0}");
    Console.WriteLine($"0.5 s budget = {(long)(median * 500)} work units; 5 s = {(long)(median * 5000)}");
}

void Baselines(string dir, string split, long budget, string outPath, int threads, bool phase3)
{
    GuardSplit(split, phase3);
    var (manifest, oracle) = Load(dir);
    var rows = manifest.Where(e => e.Split == split).ToArray();
    var results = new ConcurrentBag<RunResult>();
    var wallBudgetMs = 500.0 * budget / BudgetFor05s(budget);
    var done = 0;
    WarmCpSat();
    Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = threads }, e =>
    {
        var profile = Profile(e);
        foreach (var method in Methods.All)
        {
            results.Add(Methods.Judge(method, e, oracle[e.InstanceId], profile, budget, wallBudgetMs));
        }

        if (Interlocked.Increment(ref done) % 100 == 0)
        {
            Console.WriteLine($"ran {done}/{rows.Length}");
        }
    });

    File.WriteAllText(outPath, string.Join('\n', results
        .OrderBy(r => r.InstanceId, StringComparer.Ordinal).ThenBy(r => Array.IndexOf(Methods.All, r.Method))
        .Select(r => JsonSerializer.Serialize(r, json))) + "\n");
    Console.WriteLine($"wrote {results.Count} results to {outPath}");
}

// CP-SAT's first call loads the native library (~28 s here). Charge that once, outside the budget, and report it.
void WarmCpSat()
{
    var (profile, _) = Corpus.Instance("parallel-contention", SizeClass.Small, 0, "base", Corpus.PilotSeed, "warm-up");
    var watch = Stopwatch.StartNew();
    new Harborline.Experiments.SchedulingDecisionModel.Baselines.CpSatFeasibility().Solve(profile, 10);
    Console.WriteLine(string.Create(inv, $"cpsat cold start (excluded from per-instance wall, reported here): {watch.Elapsed.TotalMilliseconds:F0} ms"));
}

// Hardness of pilot-seed instances (never in the corpus) for each engine method: needs > 1% of budget or fails.
void PilotHardness(string family, SizeClass size, int count, long budget)
{
    var methods = new[] { "incumbent", "dom-dynamic", "least-slack", "dom-dynamic+lcv", "greedy-then-search" };
    var hard = methods.ToDictionary(m => m, _ => 0);
    var hardAll = 0;
    var n = 0;
    var gate = new object();
    Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
    {
        foreach (var variant in Corpus.Variants)
        {
            var (profile, _) = Corpus.Instance(family, size, i, variant, Corpus.PilotSeed, "pilot");
            var flags = methods.Select(m =>
            {
                var r = Methods.Run(m, profile, budget, 500);
                return r.Status == SolveStatus.Indeterminate || r.Work > budget / 100;
            }).ToArray();
            lock (gate)
            {
                n++;
                for (var k = 0; k < methods.Length; k++)
                {
                    hard[methods[k]] += flags[k] ? 1 : 0;
                }

                hardAll += flags.All(f => f) ? 1 : 0;
            }
        }
    });

    Console.WriteLine($"{family}/{size} pilot n={n}: " + string.Join(", ", methods.Select(m => $"{m}={hard[m]} ({100.0 * hard[m] / n:F1}%)")) + $"; all-hard={hardAll}");
}

// The 0.5 s budget in work units, as recorded from `calibrate`; wall budgets for CP-SAT scale from it.
long BudgetFor05s(long requested) =>
    long.TryParse(Environment.GetEnvironmentVariable("SDM_BUDGET_05S"), NumberStyles.Integer, inv, out var b) ? b : requested;

void Report(string dir, string resultsPath, long budget)
{
    var results = File.ReadLines(resultsPath).Where(l => l.Length > 0)
        .Select(l => JsonSerializer.Deserialize<RunResult>(l, json)!).ToArray();
    var byMethod = results.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.InstanceId, StringComparer.Ordinal));
    var methods = Methods.All.Where(byMethod.ContainsKey).ToArray();
    var ids = byMethod[methods[0]].Keys.Order(StringComparer.Ordinal).ToArray();

    Console.WriteLine($"budget {budget} work units; {ids.Length} instances; split(s) {string.Join(",", results.Select(r => r.Split).Distinct())}");
    Console.WriteLine();
    Console.WriteLine("| method | solved % | feasible found % | infeasible proved % | invalid accepted | false infeasible | fallbacks | wall p50 ms | wall p90 ms |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
    foreach (var m in methods)
    {
        var rs = ids.Select(i => byMethod[m][i]).ToArray();
        var feas = rs.Where(r => r.Oracle == "Feasible").ToArray();
        var inf = rs.Where(r => r.Oracle == "Infeasible").ToArray();
        var walls = rs.Select(r => r.WallMs).Order().ToArray();
        Console.WriteLine(string.Create(inv,
            $"| {m} | {100.0 * rs.Count(r => r.Solved) / rs.Length:F1} | {100.0 * feas.Count(r => r.Solved) / Math.Max(1, feas.Length):F1} | {100.0 * inf.Count(r => r.Solved) / Math.Max(1, inf.Length):F1} | {rs.Count(r => r.InvalidAccepted)} | {rs.Count(r => r.FalseInfeasible)} | {rs.Sum(r => r.Fallbacks)} | {walls[walls.Length / 2]:F1} | {walls[walls.Length * 9 / 10]:F1} |"));
    }

    Console.WriteLine();
    Console.WriteLine("Paired against incumbent (group bootstrap, 95% CI):");
    Console.WriteLine("| method | solved diff pts [CI] | work ratio, geomean [CI] (engine methods) |");
    Console.WriteLine("| --- | --- | --- |");
    foreach (var m in methods.Where(m => m != "incumbent"))
    {
        var solved = ids.Select(i => (byMethod[m][i].GroupId, byMethod[m][i].Solved, byMethod["incumbent"][i].Solved)).ToArray();
        var sd = Stats.SolvedDifference(solved, 1053);
        var work = m == "cpsat" ? "n/a (wall-limited)" : Ratio(m, "incumbent");
        Console.WriteLine(string.Create(inv, $"| {m} | {sd.Estimate:+0.0;-0.0} [{sd.Low:+0.0;-0.0}, {sd.High:+0.0;-0.0}] | {work} |"));
    }

    Console.WriteLine();
    Console.WriteLine("Solved % by family and size:");
    var cells = results.GroupBy(r => $"{r.Family}/{r.SizeClass}").OrderBy(g => g.Key).ToArray();
    Console.WriteLine($"| cell | n | {string.Join(" | ", methods)} |");
    Console.WriteLine($"| --- | --- |{string.Concat(methods.Select(_ => " --- |"))}");
    foreach (var cell in cells)
    {
        var cellIds = cell.Select(r => r.InstanceId).Distinct().ToArray();
        Console.WriteLine(string.Create(inv,
            $"| {cell.Key} | {cellIds.Length} | {string.Join(" | ", methods.Select(m => $"{100.0 * cellIds.Count(i => byMethod[m][i].Solved) / cellIds.Length:F1}"))} |"));
    }

    var hard = ids.Count(i => byMethod.Where(kv => kv.Key != "cpsat").All(kv => !kv.Value[i].Solved || kv.Value[i].Work > budget / 100));
    Console.WriteLine();
    Console.WriteLine($"Instances where every engine method needs > 1% of budget or fails: {hard}");

    string Ratio(string a, string b)
    {
        var rows = ids.Where(i => byMethod[a][i].Oracle == "Feasible" && (byMethod[a][i].Solved || byMethod[b][i].Solved))
            .Select(i => (byMethod[a][i].GroupId,
                (double)(byMethod[a][i].Solved ? byMethod[a][i].Work : budget),
                (double)(byMethod[b][i].Solved ? byMethod[b][i].Work : budget)))
            .ToArray();
        var r = Stats.WorkRatio(rows, 1053);
        return string.Create(inv, $"{r.Estimate:F3} [{r.Low:F3}, {r.High:F3}] (n={rows.Length})");
    }
}
