using System.Text.RegularExpressions;
using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// T-523/L879: freezes member-call folds outside the compute packages. Method groups, using static
/// calls, AggregateBy, SumAsync and hand loops are outside this text scan's reach.
/// </summary>
public sealed class ComputeSiteArchitectureTests
{
    // Scalar static receivers are not LINQ folds over rows.
    private const string ScalarReceivers = @"(?<!\.)\b(?:System\.(?:Numerics\.|Threading\.)?)?(?:Math|MathF|BigInteger|TimeSpan|decimal|DateTimeOffset|DateTime|Interlocked|Vector(?:[0-9]+|<[^>]+>)?)\s*$";
    private static readonly Regex Fold = new(@"\.(?:Sum|Average|Aggregate|MinBy|MaxBy|Min|Max)\s*(?:<(?:(?<depth><)|(?<-depth>>)|[\w\s.,?\[\]:()*])*(?(depth)(?!))>\s*)?\(", RegexOptions.CultureInvariant);
    private static readonly Regex Scalar = new(ScalarReceivers, RegexOptions.CultureInvariant);

    // Frozen findings, not permission to add folds. Delete a row when its last fold moves.
    private static readonly Dictionary<string, int> Listed = new(StringComparer.Ordinal)
    {
        ["blocks/hlp.blocks.calendar/Booking/BookingDefinitions.cs"] = 2, // structure: required resource hold limits
        ["blocks/hlp.blocks.calendar/Services/IntervalMath.cs"] = 1, // structure: interval overlap depth
        ["blocks/hlp.blocks.entity-views/ViewDefinitionStore.cs"] = 1, // structure: definition revision
        ["blocks/hlp.blocks.scheduling/Occurrences/Chains/InMemoryRelativeChainExpansionService.cs"] = 1, // structure: occurrence ordinal
        ["blocks/hlp.blocks.workflow/Durable/FileJournalWorkflowStore.cs"] = 2, // structure: store counts and event sequence
        ["foundation/hlp.foundation.forms-engine/Capabilities/MacaroonFormCapabilityVerifier.cs"] = 1, // structure: caveat byte lengths
        ["foundation/hlp.foundation.forms/InMemoryFormDefinitionStore.cs"] = 1, // structure: definition version
        ["foundation/hlp.foundation.rule-runtime/Compilation/CoreTypeDerivation.cs"] = 1, // structure: child type sets
        ["foundation/hlp.foundation.rule-runtime/Compilation/CoreWorkDerivation.cs"] = 9, // structure: expression child costs
        ["foundation/hlp.foundation.rule-runtime/Graph/FormRuleGraph.cs"] = 4, // debt L737: table row folds in field runtime
    };

    [Fact]
    public void Set_folds_outside_the_compute_packages_match_the_commented_allow_list_exactly()
    {
        AssertMatches(Scan(DotnetRoot(), false), Listed);
    }

    [Fact]
    public void Allow_list_comparison_rejects_stale_and_mismatched_counts()
    {
        var found = new Dictionary<string, int> { ["a.cs"] = 1 };
        var stale = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertMatches(found, new() { ["a.cs"] = 1, ["b.cs"] = 1 }));
        Assert.Contains("b.cs: found 0, listed 1", stale.Message);
        var tooFew = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertMatches(found, new() { ["a.cs"] = 2 }));
        Assert.Contains("a.cs: found 1, listed 2", tooFew.Message);
        var tooMany = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertMatches(found, new() { ["a.cs"] = 0 }));
        Assert.Contains("a.cs: found 1, listed 0", tooMany.Message);
    }

    [Theory]
    [InlineData("var balance = opening + lines.Sum(l => l.Amount);")]
    [InlineData("var mean = rows.Where(r => r.Open).Average(r => r.Days);")]
    [InlineData("var total = entries.Aggregate(0m, (sum, e) => sum + e.Amount);")]
    [InlineData("var last = lines.Max(l => l.CreatedAtUtc);")]
    [InlineData("    .Sum(l => l.Amount);")]
    [InlineData("rows.Min(r => r.Amount)")]
    [InlineData("rows.MinBy(r => r.Amount)")]
    [InlineData("rows.MaxBy(r => r.Amount)")]
    [InlineData("rows.Aggregate(0, (sum, row) => sum + row.Value) + Math.Max(0, count)")]
    [InlineData("rows.Sum<Row>(r => r.Amount)")]
    [InlineData("rows.Aggregate<Row, decimal>(0m, (sum, r) => sum + r.Amount)")]
    [InlineData("rows.Sum<KeyValuePair<string, int>>(r => r.Value)")]
    [InlineData("rows.Sum<List<List<Row>>>(r => r.Count)")]
    [InlineData("shape.Vector.Sum(v => v.X)")]
    [InlineData("slotsByDateTime.Max(s => s.At)")]
    public void Canary_rejects_a_planted_fold_over_rows(string source) => Assert.Equal(1, CountFolds(source));

    [Fact]
    public void Scalar_math_and_catalogue_calls_remain_permitted()
    {
        Assert.Equal(0, CountFolds("Math.Max(a, b); BigInteger.Max(a, b); TimeSpan.FromTicks(Math.Min(a, b)); await catalogue.EvaluateAsync(reference, request, cancellationToken);"));
        Assert.Equal(0, CountFolds("<c>Cardinality.Max</c> (and the cap)"));
    }

    [Theory]
    [InlineData("Math.Max(a, b)")]
    [InlineData("MathF.Min(a, b)")]
    [InlineData("BigInteger.Max(a, b)")]
    [InlineData("TimeSpan.Min(a, b)")]
    [InlineData("decimal.Max(a, b)")]
    [InlineData("DateTimeOffset.Max(a, b)")]
    [InlineData("DateTime.Min(a, b)")]
    [InlineData("Interlocked.Max(a, b)")]
    [InlineData("Vector<int>.Max(a, b)")]
    [InlineData("Vector128.Max(a, b)")]
    [InlineData("System.Math.Max(a, b)")]
    [InlineData("System.Numerics.BigInteger.Max(a, b)")]
    public void Scalar_receivers_are_not_folds(string source) => Assert.Equal(0, CountFolds(source));

    [Fact]
    public void The_scan_sees_the_folds_inside_the_compute_packages()
    {
        var found = Scan(DotnetRoot(), true);
        Assert.Contains(found.Keys, path => path.StartsWith("blocks/hlp.blocks.aggregates/", StringComparison.Ordinal));
        Assert.Contains(found.Keys, path => path.StartsWith("blocks/hlp.blocks.reports/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_measure_catalogue_package_holds_no_fold() =>
        Assert.DoesNotContain(Scan(DotnetRoot(), false).Keys, path => path.StartsWith("blocks/hlp.blocks.measure-catalogue/", StringComparison.Ordinal));

    [Fact]
    public void Planted_tree_checks_scope_and_package_exclusions()
    {
        var root = Path.Combine(Path.GetTempPath(), "compute-site-" + Guid.NewGuid().ToString("N"));
        try
        {
            string[] paths = ["blocks/consumer/Rows.cs", "foundation/example/Rows.cs", "kernel/example/Rows.cs", "blocks/hlp.blocks.measure-catalogue/Rows.cs",
                "blocks/hlp.blocks.aggregates/Rows.cs", "blocks/hlp.blocks.reports/Rows.cs", "blocks/example.tests/Rows.cs", "blocks/mixed.Tests/Rows.cs",
                "blocks/example/bin/Rows.cs", "blocks/example/obj/Rows.cs", "architecture/Rows.cs", "blocks/consumer/Rows.txt"];
            foreach (var path in paths)
            {
                var file = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "var total = rows.Sum(row => row.Amount);");
            }
            var ordinary = Scan(root, false);
            Assert.Equal(new[] { "blocks/consumer/Rows.cs", "blocks/hlp.blocks.measure-catalogue/Rows.cs", "foundation/example/Rows.cs", "kernel/example/Rows.cs" }, ordinary.Keys.OrderBy(p => p, StringComparer.Ordinal));
            Assert.All(ordinary.Values, count => Assert.Equal(1, count));
            var includingCompute = Scan(root, true);
            Assert.Equal(new[] { "blocks/consumer/Rows.cs", "blocks/hlp.blocks.aggregates/Rows.cs", "blocks/hlp.blocks.measure-catalogue/Rows.cs", "blocks/hlp.blocks.reports/Rows.cs", "foundation/example/Rows.cs", "kernel/example/Rows.cs" }, includingCompute.Keys.OrderBy(p => p, StringComparer.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    private static int CountFolds(string source) => Fold.Matches(source).Count(match => !Scalar.IsMatch(source[..match.Index]));

    private static void AssertMatches(Dictionary<string, int> found, Dictionary<string, int> listed)
    {
        var differences = found.Keys.Union(listed.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
            .Where(path => found.GetValueOrDefault(path) != listed.GetValueOrDefault(path))
            .Select(path => $"{path}: found {found.GetValueOrDefault(path)}, listed {listed.GetValueOrDefault(path)}");
        Assert.True(!differences.Any(), "Fold counts differ:\n" + string.Join("\n", differences));
    }

    private static Dictionary<string, int> Scan(string root, bool includeCompute)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(root, file).Replace('\\', '/');
            var parts = path.Split('/');
            if (parts.SkipLast(1).Any(part => part is "bin" or "obj" or "architecture" || part.EndsWith(".tests", StringComparison.OrdinalIgnoreCase))) continue;
            if (!includeCompute && (path.StartsWith("blocks/hlp.blocks.aggregates/", StringComparison.Ordinal) || path.StartsWith("blocks/hlp.blocks.reports/", StringComparison.Ordinal))) continue;
            var count = CountFolds(File.ReadAllText(file));
            if (count > 0) result.Add(path, count);
        }
        return result;
    }

    private static string DotnetRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "projections", "dotnet");
    }
}
