using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Experiments.SchedulingDecisionModel.Data;
using Harborline.Experiments.SchedulingDecisionModel.Engine;
using Harborline.Experiments.SchedulingDecisionModel.Evaluation;
using Harborline.Experiments.SchedulingDecisionModel.Oracle;
using static Harborline.Experiments.SchedulingDecisionModel.Evaluation.Stats;

// T-1053 evaluation tool.
//   calibrate   <corpus> [count]                   work units per 0.5 s for the incumbent, single thread, train split
//   baselines   <corpus> <split> <budget> <out> [threads] [--phase3]
//   report      <corpus> <results.jsonl> <budget>
// Test splits are refused unless --phase3 is given: the holdout is opened once, in Phase 3.
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var inv = CultureInfo.InvariantCulture;

switch (args.ElementAtOrDefault(0))
{
    case "t1054-evidence":
        if (args.Length != 4 || args[3] != "--phase3")
        {
            Console.Error.WriteLine("usage: t1054-evidence <corpus> <out.jsonl> --phase3");
            return 2;
        }

        try
        {
            return T1054Evidence(args[1], args[2], phase3: true) ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    case "equivalence": return Equivalence(args[1], args[2]) ? 0 : 1;
    case "calibrate": Calibrate(args[1], args.Length > 2 ? int.Parse(args[2], inv) : 25); return 0;
    case "baselines":
        Baselines(args[1], args[2], long.Parse(args[3], inv), args[4],
            args.Length > 5 && !args[5].StartsWith("--", StringComparison.Ordinal) ? int.Parse(args[5], inv) : 8,
            args.Contains("--phase3"));
        return 0;
    case "report": Report(args[1], args[2], long.Parse(args[3], inv)); return 0;
    case "train": Train(args[1], args[2], args.Length > 3 ? int.Parse(args[3], inv) : 300); return 0;
    case "calibrate-charges": CalibrateCharges(args[1], args[2], args.Length > 3 ? int.Parse(args[3], inv) : 40); return 0;
    case "select":
        Select(args[1], args[2], long.Parse(args[3], inv), long.Parse(args[4], inv), long.Parse(args[5], inv), double.Parse(args[6], inv), args[7]);
        return 0;
    case "decide": Decide(long.Parse(args[1], inv), args[2..]); return 0;
    case "pilot-hardness":
        PilotHardness(args[1], Corpus.ParseSize(args[2]), int.Parse(args[3], inv), long.Parse(args[4], inv));
        return 0;
    default:
        Console.Error.WriteLine("usage: equivalence|calibrate|baselines|report|t1054-evidence");
        return 2;
}

bool T1054Evidence(string dir, string outPath, bool phase3)
{
    GuardSplit("test-unseen-family", phase3);
    const string manifestHash = "d6916b48c35a40c3ead99445a36e25499aeec174431df553f4c082b41d4341c3";
    if (Canonical.Sha256(File.ReadAllText(Path.Combine(dir, "manifest.jsonl"))) != manifestHash)
    {
        throw new InvalidDataException("T1054 requires the frozen corpus-v2 manifest.");
    }

    var (manifest, oracle) = Load(dir);
    var labels = File.ReadLines(Path.Combine(dir, "labels.jsonl")).Where(line => line.Length > 0)
        .Select(line => JsonSerializer.Deserialize<InstanceLabel>(line, json)
            ?? throw new InvalidDataException("Null label."))
        .ToDictionary(label => label.InstanceId, StringComparer.Ordinal);
    if (manifest.Length != 6000 || manifest.Select(row => row.InstanceId).Distinct(StringComparer.Ordinal).Count() != 6000
        || labels.Count != 6000 || oracle.Count != 6000
        || manifest.GroupBy(row => (row.Family, row.SizeClass, row.Split)).Count() != 25)
    {
        throw new InvalidDataException("Expected 6000 unique paired corpus identities in 25 cells.");
    }

    foreach (var entry in manifest)
    {
        if (!labels.TryGetValue(entry.InstanceId, out var label) || label.InputSha256 != entry.InputSha256
            || label.Disagreement || label.OracleOutcome is not ("Feasible" or "Infeasible")
            || oracle[entry.InstanceId] != label.OracleOutcome)
        {
            throw new InvalidDataException($"{entry.InstanceId}: missing, mismatched or indefinite label.");
        }
    }

    foreach (var group in manifest.GroupBy(entry => entry.GroupId))
    {
        if (!group.Select(entry => entry.Variant).Order(StringComparer.Ordinal).SequenceEqual(new[] { "base", "tightened" })
            || group.Select(entry => (entry.Family, entry.SizeClass, entry.Split)).Distinct().Count() != 1)
        {
            throw new InvalidDataException($"{group.Key}: incomplete or inconsistent variant pair.");
        }
    }

    var profiles = manifest.Select(Profile).ToArray();
    var results = new List<RunResult>(12000);
    for (var i = 0; i < manifest.Length; i++)
    {
        var entry = manifest[i];
        var profile = profiles[i];
        var compiled = new FiniteCandidateCompiler().Compile(profile);
        var watch = Stopwatch.StartNew();
        var production = new DeterministicExhaustiveProofSolver().Solve(compiled, 1_826_301);
        watch.Stop();
        results.Add(JudgeT1054(entry, oracle[entry.InstanceId], profile, "production", production.Status,
            production.Assignments, production.WorkUnits, watch.Elapsed.TotalMilliseconds));
        watch.Restart();
        var incumbent = new SearchEngine().Run(compiled, new IncumbentPolicy(), 1_826_301, dedupe: false);
        watch.Stop();
        results.Add(JudgeT1054(entry, oracle[entry.InstanceId], profile, "incumbent", incumbent.Status,
            incumbent.Assignments, incumbent.Work, watch.Elapsed.TotalMilliseconds) with
        {
            Fallbacks = incumbent.Fallbacks,
            FallbackReason = incumbent.FirstFallbackReason,
        });
    }

    File.WriteAllText(outPath, string.Join('\n', results.OrderBy(row => row.InstanceId, StringComparer.Ordinal)
        .ThenBy(row => row.Method, StringComparer.Ordinal).Select(row => JsonSerializer.Serialize(row, json))) + "\n");
    return ReportT1054(results);
}

RunResult JudgeT1054(ManifestEntry entry, string label, SchedulingProfile profile, string method,
    SolveStatus status, IReadOnlyList<AssignmentCandidate> plan, long work, double wallMs)
{
    var valid = status == SolveStatus.Feasible && new PlanChecker(profile).Check(plan).IsFeasible;
    var solved = work is >= 0 and <= 1_826_301 &&
        ((status == SolveStatus.Feasible && valid && label == "Feasible")
        || (status == SolveStatus.ProvenInfeasible && label == "Infeasible"));
    return new(method, entry.InstanceId, entry.GroupId, entry.Family, entry.SizeClass, entry.Split, label,
        status.ToString(), work, wallMs, valid, solved,
        status == SolveStatus.Feasible && !valid,
        status == SolveStatus.ProvenInfeasible && label == "Feasible", 0, null);
}

bool ReportT1054(IReadOnlyList<RunResult> rows)
{
    const long budget = 1_826_301;
    var pairs = rows.GroupBy(row => row.InstanceId).ToArray();
    if (rows.Count != 12000 || pairs.Length != 6000 || pairs.Any(pair => pair.Count() != 2
        || !pair.Select(row => row.Method).Order(StringComparer.Ordinal).SequenceEqual(new[] { "incumbent", "production" })
        || pair.Select(row => (row.GroupId, row.Family, row.SizeClass, row.Split, row.Oracle)).Distinct().Count() != 1))
    {
        Console.Error.WriteLine("Insufficient evidence. Expected 12000 unique paired results.");
        return false;
    }

    var production = rows.Where(row => row.Method == "production").ToDictionary(row => row.InstanceId, StringComparer.Ordinal);
    var incumbent = rows.Where(row => row.Method == "incumbent").ToDictionary(row => row.InstanceId, StringComparer.Ordinal);
    var all = production.Values.OrderBy(row => row.InstanceId, StringComparer.Ordinal).ToArray();
    var cells = new List<(string Dimension, string Name, RunResult[] Rows)> { ("overall", "all", all) };
    cells.AddRange(all.GroupBy(row => row.Family).Select(group => ("family", group.Key, group.ToArray())));
    cells.AddRange(all.GroupBy(row => row.SizeClass).Select(group => ("size", group.Key, group.ToArray())));
    cells.AddRange(all.GroupBy(row => row.Split).Select(group => ("split", group.Key, group.ToArray())));
    cells.AddRange(all.GroupBy(row => $"{row.Family}/{row.SizeClass}/{row.Split}")
        .Select(group => ("family/size/split", group.Key, group.ToArray())));
    if (cells.Count(cell => cell.Dimension == "family/size/split") != 25
        || !all.Select(row => row.Split).Distinct().Order(StringComparer.Ordinal).SequenceEqual(new[]
        {
            "test-seen", "test-unseen-family", "test-unseen-size", "train", "validation",
        }))
    {
        Console.Error.WriteLine("Insufficient evidence. Frozen cells or splits are missing.");
        return false;
    }

    var correctnessFaults = rows.Count(row => row.InvalidAccepted || row.FalseInfeasible || Contradiction(row)
        || row.Work is < 0 or > budget || row.Fallbacks != 0
        || row.Status is not ("Feasible" or "ProvenInfeasible" or "Indeterminate"));
    var reports = new List<object>();
    var regressions = new List<string>();
    var primaryPass = false;
    foreach (var cell in cells.OrderBy(cell => cell.Dimension, StringComparer.Ordinal).ThenBy(cell => cell.Name, StringComparer.Ordinal))
    {
        if (cell.Rows.Length == 0)
        {
            return false;
        }

        var a = cell.Rows;
        var b = a.Select(row => incumbent[row.InstanceId]).ToArray();
        var solved = Stats.SolvedDifference(a.Select(row => (row.GroupId, row.Solved, incumbent[row.InstanceId].Solved)).ToArray(), 1054);
        var work = Stats.WorkRatio(a.Select(row => (row.GroupId,
            (double)(row.Solved ? row.Work : budget),
            (double)(incumbent[row.InstanceId].Solved ? incumbent[row.InstanceId].Work : budget))).ToArray(), 1054);
        if (solved.High < 0)
        {
            regressions.Add($"{cell.Dimension}/{cell.Name}");
        }

        if (cell.Dimension == "split" && cell.Name == "test-unseen-family")
        {
            primaryPass = a.Count(row => row.Solved) >= b.Count(row => row.Solved) && solved.Low > 0;
        }

        reports.Add(new
        {
            cell.Dimension, cell.Name, Count = a.Length, Groups = a.Select(row => row.GroupId).Distinct().Count(),
            Production = Describe(a), Incumbent = Describe(b),
            BothSolved = a.Count(row => row.Solved && incumbent[row.InstanceId].Solved),
            ProductionOnly = a.Count(row => row.Solved && !incumbent[row.InstanceId].Solved),
            IncumbentOnly = a.Count(row => !row.Solved && incumbent[row.InstanceId].Solved),
            NeitherSolved = a.Count(row => !row.Solved && !incumbent[row.InstanceId].Solved),
            SolvedDifference = solved, AllPairCappedWorkRatio = work,
        });
    }

    var pass = correctnessFaults == 0 && primaryPass && regressions.Count == 0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Task = "T1054", Budget = budget, ResultRows = rows.Count, Instances = pairs.Length,
        BootstrapReplicates = 2000, Confidence = 0.95, BootstrapSeed = 1054,
        WorkEstimand = "Production/incumbent geometric mean of capped work over every definite-label pair, including both unsolved; unsolved sides cost the budget.",
        WallTime = "Descriptive only; no wall-time acceptance threshold.",
        CorrectnessFaults = correctnessFaults, PrimaryPass = primaryPass, Regressions = regressions,
        Accepted = pass, Cells = reports,
    }, json));
    return pass;

    static bool Contradiction(RunResult row) =>
        (row.Status == "Feasible" && row.Oracle != "Feasible")
        || (row.Status == "ProvenInfeasible" && row.Oracle != "Infeasible");

    static object Describe(RunResult[] arm)
    {
        var work = arm.Select(row => row.Work).Order().ToArray();
        return new
        {
            Count = arm.Length, Solved = arm.Count(row => row.Solved),
            SolvedProportion = (double)arm.Count(row => row.Solved) / arm.Length,
            CheckedPlans = arm.Count(row => row.Status == "Feasible"),
            ValidPlans = arm.Count(row => row.PlanValid),
            InvalidAccepted = arm.Count(row => row.InvalidAccepted),
            FalseInfeasible = arm.Count(row => row.FalseInfeasible),
            LabelContradictions = arm.Count(Contradiction),
            StatusCounts = arm.GroupBy(row => row.Status).OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            Work = new { Min = work[0], P50 = work[work.Length / 2], P90 = work[work.Length * 9 / 10], Max = work[^1] },
        };
    }
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
            var engine = new SearchEngine().Run(compiled, new DynamicDomainPolicy(), budget, dedupe: false);
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
    LoadFrozen(dir);
    var methods = Methods.All.Concat(Methods.Learned.Keys).ToArray();
    var results = new ConcurrentBag<RunResult>();
    var wallBudgetMs = 500.0 * budget / BudgetFor05s(budget);
    var done = 0;
    WarmCpSat();
    Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = threads }, e =>
    {
        var profile = Profile(e);
        foreach (var method in methods)
        {
            results.Add(Methods.Judge(method, e, oracle[e.InstanceId], profile, budget, wallBudgetMs));
        }

        if (Interlocked.Increment(ref done) % 100 == 0)
        {
            Console.WriteLine($"ran {done}/{rows.Length}");
        }
    });

    File.WriteAllText(outPath, string.Join('\n', results
        .OrderBy(r => r.InstanceId, StringComparer.Ordinal).ThenBy(r => Array.IndexOf(methods, r.Method))
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
    var methods = Methods.All.Concat(byMethod.Keys.Where(k => !Methods.All.Contains(k)).Order(StringComparer.Ordinal)).Where(byMethod.ContainsKey).ToArray();
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
    var reference = byMethod.ContainsKey("incumbent") ? "incumbent" : "dom-dynamic";
    Console.WriteLine($"Paired against {reference} (group bootstrap, 95% CI):");
    Console.WriteLine("| method | solved diff pts [CI] | work ratio, geomean [CI] (engine methods) |");
    Console.WriteLine("| --- | --- | --- |");
    foreach (var m in methods.Where(m => m != reference))
    {
        var solved = ids.Select(i => (byMethod[m][i].GroupId, byMethod[m][i].Solved, byMethod[reference][i].Solved)).ToArray();
        var sd = Stats.SolvedDifference(solved, 1053);
        var work = m == "cpsat" ? "n/a (wall-limited)" : Ratio(m, reference);
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

string ModelsDir(string corpus) => Path.Combine(corpus, "..", "..", "models", "trained");

// Loads models/trained/frozen.json, if present, into Methods.Learned as method "learned".
void LoadFrozen(string corpus)
{
    var path = Path.Combine(ModelsDir(corpus), "frozen.json");
    if (!File.Exists(path))
    {
        return;
    }

    var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    Harborline.Experiments.SchedulingDecisionModel.Models.LinearModel? LoadModel(string key, string kind, string[] names)
    {
        var file = root.GetProperty(key).GetString();
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        var model = Harborline.Experiments.SchedulingDecisionModel.Models.LinearModel.Load(Path.Combine(ModelsDir(corpus), file), kind, names);
        var expected = root.GetProperty(key + "Sha256").GetString();
        return model.Sha256() == expected ? model : throw new InvalidDataException($"{file}: hash differs from frozen.json");
    }

    Methods.Learned["learned"] = new LearnedConfig(
        "learned",
        LoadModel("activityModel", "activity", Harborline.Experiments.SchedulingDecisionModel.Models.FeatureContext.ActivityNames),
        LoadModel("valueModel", "value", Harborline.Experiments.SchedulingDecisionModel.Models.FeatureContext.CandidateNames),
        root.GetProperty("shortlist").GetInt32(),
        root.GetProperty("chargePerActivity").GetInt64(),
        root.GetProperty("chargePerCandidate").GetInt64(),
        root.GetProperty("contextChargePerOperation").GetDouble());
    Console.WriteLine($"loaded frozen learned policy from {path}");
}


void Train(string corpus, string outDir, int maxSnapshots)
{
    var (manifest, _) = Load(corpus);
    var train = manifest.Where(e => e.Split == "train").ToArray();
    var snapshots = new ConcurrentBag<Harborline.Experiments.SchedulingDecisionModel.Models.ActivitySnapshot>();
    var values = new ConcurrentBag<Harborline.Experiments.SchedulingDecisionModel.Models.ValueRow>();
    var watch = Stopwatch.StartNew();
    Parallel.ForEach(train, new ParallelOptions { MaxDegreeOfParallelism = 8 }, e =>
    {
        var compiled = new FiniteCandidateCompiler().Compile(Profile(e));
        var trace = Harborline.Experiments.SchedulingDecisionModel.Models.TraceCollector.Collect(e.InstanceId, compiled, 1_826_301, maxSnapshots);
        foreach (var s in trace.Snapshots)
        {
            snapshots.Add(s);
        }

        foreach (var v in trace.Values)
        {
            values.Add(v);
        }
    });
    var collectSeconds = watch.Elapsed.TotalSeconds;

    // Deterministic order regardless of thread scheduling.
    var snaps = snapshots.OrderBy(s => s.InstanceId, StringComparer.Ordinal).ThenBy(s => string.Join(',', s.TrueCounts)).ThenBy(s => s.Features.Length).ToArray();
    var vals = values.OrderBy(v => v.InstanceId, StringComparer.Ordinal).ThenBy(v => string.Join(',', v.Features.Select(f => f.ToString("R", inv)))).ThenBy(v => v.Label).ToArray();
    Directory.CreateDirectory(outDir);
    var summary = new List<string>
    {
        string.Create(inv, $"train instances: {train.Length}; activity snapshots: {snaps.Length}; value rows: {vals.Length} (positive {vals.Count(v => v.Label == 1)}); trace collection {collectSeconds:F0} s"),
    };
    foreach (var l2 in Grid.L2)
    {
        watch.Restart();
        var activity = Harborline.Experiments.SchedulingDecisionModel.Models.ModelTrainer.TrainActivity(snaps, l2, 4, 1053, "corpus-v2/train");
        var value = Harborline.Experiments.SchedulingDecisionModel.Models.ModelTrainer.TrainValue(vals, l2, "corpus-v2/train");
        var tag = l2.ToString("0e0", inv);
        File.WriteAllText(Path.Combine(outDir, $"activity-l2-{tag}.json"), activity.ToJson());
        File.WriteAllText(Path.Combine(outDir, $"value-l2-{tag}.json"), value.ToJson());
        summary.Add(string.Create(inv, $"l2={tag}: activity sha256 {activity.Sha256()}, value sha256 {value.Sha256()}; fit {watch.Elapsed.TotalSeconds:F0} s"));
        summary.Add($"  activity weights: {string.Join(", ", activity.FeatureNames.Zip(activity.Weights, (n, w) => string.Create(inv, $"{n}={w:F3}")))}");
        summary.Add($"  value weights: {string.Join(", ", value.FeatureNames.Zip(value.Weights, (n, w) => string.Create(inv, $"{n}={w:F3}")))}; bias={value.Bias.ToString("F3", inv)}");
    }

    File.WriteAllLines(Path.Combine(outDir, "training-summary.txt"), summary);
    summary.ForEach(Console.WriteLine);
}

void CalibrateCharges(string corpus, string modelsDir, int count)
{
    var (manifest, _) = Load(corpus);
    var activity = Harborline.Experiments.SchedulingDecisionModel.Models.LinearModel.Load(Path.Combine(modelsDir, "activity-l2-1e-4.json"), "activity", Harborline.Experiments.SchedulingDecisionModel.Models.FeatureContext.ActivityNames);
    var value = Harborline.Experiments.SchedulingDecisionModel.Models.LinearModel.Load(Path.Combine(modelsDir, "value-l2-1e-4.json"), "value", Harborline.Experiments.SchedulingDecisionModel.Models.FeatureContext.CandidateNames);
    long aItems = 0, aTicks = 0, vItems = 0, vTicks = 0, checks = 0, cTicks = 0, cOps = 0;
    double checkMs = 0;
    foreach (var e in manifest.Where(e => e.Split == "train").Where((_, i) => i % 7 == 0).Take(count))
    {
        var compiled = new FiniteCandidateCompiler().Compile(Profile(e));
        var policy = new Harborline.Experiments.SchedulingDecisionModel.Models.LearnedPolicy(activity, value, 1, 0, 0);
        new SearchEngine().Run(compiled, policy, 200_000, dedupe: true);
        aItems += policy.ActivitiesScored; aTicks += policy.ActivityTicks;
        vItems += policy.CandidatesScored; vTicks += policy.CandidateTicks;
        cTicks += policy.ContextTicks; cOps += policy.ContextOperations;

        // Same instance, incumbent order: cost per consistency check including search overhead.
        var w = Stopwatch.StartNew();
        var r = new SearchEngine().Run(compiled, new IncumbentPolicy(), 200_000, dedupe: true);
        checkMs += w.Elapsed.TotalMilliseconds;
        checks += r.Work;
    }

    var nsPerCheck = checkMs * 1e6 / checks;
    var nsPerActivity = aTicks * 1e9 / Stopwatch.Frequency / Math.Max(1, aItems);
    var nsPerCandidate = vTicks * 1e9 / Stopwatch.Frequency / Math.Max(1, vItems);
    Console.WriteLine(string.Create(inv, $"ns/check {nsPerCheck:F0} ({checks} checks); ns/activity scored {nsPerActivity:F0} ({aItems}); ns/candidate scored {nsPerCandidate:F0} ({vItems})"));
    var nsPerContextOp = cTicks * 1e9 / Stopwatch.Frequency / Math.Max(1, cOps);
    Console.WriteLine(string.Create(inv, $"charges (ceil): activity {Math.Ceiling(nsPerActivity / nsPerCheck)}, candidate {Math.Ceiling(nsPerCandidate / nsPerCheck)}"));
    Console.WriteLine(string.Create(inv, $"context build: {nsPerContextOp:F1} ns/operation ({cOps} ops) = {nsPerContextOp / nsPerCheck:F3} work units per operation"));
}

// Validation-only model selection over (activity model, value model, shortlist k, L2).
void Select(string corpus, string modelsDir, long budget, long chargeA, long chargeV, double chargeContext, string outPath)
{
    var (manifest, oracle) = Load(corpus);
    var rows = manifest.Where(e => e.Split == "validation").ToArray();
    var A = Harborline.Experiments.SchedulingDecisionModel.Models.FeatureContext.ActivityNames;
    var V = Harborline.Experiments.SchedulingDecisionModel.Models.FeatureContext.CandidateNames;
    foreach (var l2 in Grid.L2)
    {
        var tag = l2.ToString("0e0", inv);
        var am = Harborline.Experiments.SchedulingDecisionModel.Models.LinearModel.Load(Path.Combine(modelsDir, $"activity-l2-{tag}.json"), "activity", A);
        var vm = Harborline.Experiments.SchedulingDecisionModel.Models.LinearModel.Load(Path.Combine(modelsDir, $"value-l2-{tag}.json"), "value", V);
        foreach (var k in new[] { 1, 2, 3, 5 })
        {
            Methods.Learned[$"a-l2={tag}-k={k}"] = new LearnedConfig($"a-l2={tag}-k={k}", am, null, k, chargeA, chargeV, chargeContext);
            Methods.Learned[$"av-l2={tag}-k={k}"] = new LearnedConfig($"av-l2={tag}-k={k}", am, vm, k, chargeA, chargeV, chargeContext);
        }

        Methods.Learned[$"v-l2={tag}"] = new LearnedConfig($"v-l2={tag}", null, vm, 1, chargeA, chargeV, chargeContext);
        Methods.Learned[$"domv-l2={tag}"] = new LearnedConfig($"domv-l2={tag}", null, vm, -1, chargeA, chargeV, chargeContext);
    }

    var results = new ConcurrentBag<RunResult>();
    var names = Methods.Learned.Keys.Order(StringComparer.Ordinal).ToArray();
    Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = 8 }, e =>
    {
        var profile = Profile(e);
        foreach (var name in names.Append("dom-dynamic"))
        {
            results.Add(Methods.Judge(name, e, oracle[e.InstanceId], profile, budget, 500));
        }
    });

    File.WriteAllText(outPath, string.Join('\n', results.OrderBy(r => r.InstanceId, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal)
        .Select(r => JsonSerializer.Serialize(r, json))) + "\n");
    Report(corpus, outPath, budget);
}

// Applies the pre-registered T-1053 sdm-03 criteria to Phase 3 result files (any order; split read from rows).
void Decide(long budget, string[] files)
{
    const string Model = "learned";
    const string Base = "dom-dynamic";
    var rows = files.SelectMany(f => File.ReadLines(f).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<RunResult>(l, json)!)).ToArray();
    var by = rows.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.InstanceId, StringComparer.Ordinal));
    var model = by[Model];
    var baseline = by[Base];

    Interval Solved(IEnumerable<string> ids) => Stats.SolvedDifference(ids.Select(i => (model[i].GroupId, model[i].Solved, baseline[i].Solved)).ToArray(), 1053);
    Interval Work(IEnumerable<string> ids) => Stats.WorkRatio(ids
        .Where(i => model[i].Oracle == "Feasible" && (model[i].Solved || baseline[i].Solved))
        .Select(i => (model[i].GroupId, (double)(model[i].Solved ? model[i].Work : budget), (double)(baseline[i].Solved ? baseline[i].Work : budget)))
        .ToArray(), 1053);
    string Fmt(Interval x, string unit) => string.Create(inv, $"{x.Estimate:F3}{unit} [{x.Low:F3}, {x.High:F3}]");

    var all = model.Keys.ToArray();
    var invalid = all.Count(i => model[i].InvalidAccepted);
    var falseInf = all.Count(i => model[i].FalseInfeasible);
    var fallbacks = all.Sum(i => model[i].Fallbacks);
    var baseUnsafe = by.Where(kv => kv.Key != Model).Sum(kv => kv.Value.Values.Count(r => r.InvalidAccepted || r.FalseInfeasible));
    Console.WriteLine($"## Phase 3 decision ({all.Length} holdout instances, budget {budget})");
    Console.WriteLine();
    Console.WriteLine($"3. Hard gates (model): invalid accepted {invalid}; false infeasible {falseInf}; fallbacks {fallbacks}. Baseline unsafe outcomes: {baseUnsafe}.");
    var gatesPass = invalid == 0 && falseInf == 0;

    var unseen = all.Where(i => model[i].Split == "test-unseen-family").ToArray();
    var a = Solved(unseen);
    var b = Work(unseen);
    var passA = a.Low >= 10;
    var passB = b.High <= 0.5;
    Console.WriteLine($"4. Primary, test-unseen-family (n={unseen.Length}): (a) solved diff {Fmt(a, " pts")} -> {(passA ? "PASS" : "fail")} (needs low >= +10); (b) work ratio {Fmt(b, "x")} -> {(passB ? "PASS" : "fail")} (needs high <= 0.5).");

    var regressions = new List<string>();
    var cells = unseen.GroupBy(i => model[i].Family).Select(g => ($"unseen family {g.Key}", g.ToArray()))
        .Concat(new[] { ("unseen size (all)", all.Where(i => model[i].Split == "test-unseen-size").ToArray()) })
        .Concat(all.Where(i => model[i].Split == "test-unseen-size").GroupBy(i => model[i].Family).Select(g => ($"unseen size {g.Key}", g.ToArray())));
    foreach (var (name, ids) in cells.Where(c => c.Item2.Length > 0))
    {
        var d = Solved(ids);
        var w = Work(ids);
        var regress = d.High < 0;
        if (regress)
        {
            regressions.Add(name);
        }

        Console.WriteLine($"5. {name} (n={ids.Length}): solved diff {Fmt(d, " pts")}{(regress ? " REGRESSION" : "")}; work ratio {Fmt(w, "x")}");
    }

    var hard = unseen.Count(i => !baseline[i].Solved || baseline[i].Work > budget / 100);
    var floor = hard >= 300;
    Console.WriteLine($"6. Hard cases for {Base} in test-unseen-family: {hard} (needs >= 300) -> {(floor ? "met" : "NOT MET")}");

    var seen = all.Where(i => model[i].Split == "test-seen").ToArray();
    if (seen.Length > 0)
    {
        Console.WriteLine($"   secondary, test-seen (n={seen.Length}): solved diff {Fmt(Solved(seen), " pts")}; work ratio {Fmt(Work(seen), "x")}");
    }

    var hardIds = unseen.Where(i => !baseline[i].Solved || baseline[i].Work > budget / 100).ToArray();
    Console.WriteLine($"   secondary, hard subset of test-unseen-family (n={hardIds.Length}): solved diff {Fmt(Solved(hardIds), " pts")}; work ratio {Fmt(Work(hardIds), "x")}");
    if (by.TryGetValue("cpsat", out var cp))
    {
        var c = Stats.SolvedDifference(unseen.Select(i => (model[i].GroupId, cp[i].Solved, baseline[i].Solved)).ToArray(), 1053);
        Console.WriteLine($"   CP-SAT vs {Base}, test-unseen-family: solved diff {Fmt(c, " pts")}; CP-SAT solved {100.0 * unseen.Count(i => cp[i].Solved) / unseen.Length:F1}% vs {Base} {100.0 * unseen.Count(i => baseline[i].Solved) / unseen.Length:F1}% vs model {100.0 * unseen.Count(i => model[i].Solved) / unseen.Length:F1}%");
    }

    var decision = !gatesPass ? "NO GO (hard gate)"
        : !floor ? "INCONCLUSIVE (hard-case floor not met) -> NO GO for adoption"
        : regressions.Count > 0 ? $"NO GO (regression: {string.Join(", ", regressions)})"
        : passA || passB ? "GO"
        : "NO GO (gain below both pre-registered thresholds)";
    Console.WriteLine();
    Console.WriteLine($"7. Decision: {decision}");
}

internal static class Grid
{
    public static readonly double[] L2 = [1e-4, 1e-2];
}
