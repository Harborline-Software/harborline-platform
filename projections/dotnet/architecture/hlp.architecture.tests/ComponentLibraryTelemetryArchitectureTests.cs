using System.Text.RegularExpressions;
using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class ComponentLibraryTelemetryArchitectureTests
{
    private static readonly string[] Roots = ["react", "blazor", "shared"];
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".cs", ".razor" };
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "tests", "test", "__tests__", "stories" };
    private static readonly Regex Emit = new(@"\b(?:console\s*\.\s*\w+\s*\(|ILogger(?:Factory)?\b|LoggerMessage\b|ActivitySource\b|System\s*\.\s*Diagnostics\s*\.\s*Metrics\b|new\s+Meter\s*\(|Console\s*\.\s*(?:Write\w*|Error|Out)\b|Debug\s*\.\s*(?:Write\w*|Print)\b|Trace\s*\.\s*(?:Write\w*|Trace\w*)\b|EventSource\b|navigator\s*\.\s*sendBeacon\b|(?:import|from|require)\s*(?:\(\s*)?['""]@opentelemetry/)", RegexOptions.Compiled);
    private static readonly Regex LiteralCall = new("""^\s*console\s*\.\s*\w+\s*\(\s*(?:'(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")\s*\)\s*;?\s*$""", RegexOptions.Compiled);

    // The existing deprecation warning is fixed text and carries no rendered value.
    private static readonly Dictionary<string, (int Count, string Reason)> AllowList = new(StringComparer.Ordinal)
    {
        ["projections/react/ui/hlp.ui.text-area/src/TextArea.tsx"] = (1, "fixed deprecation sentence, no prop value"),
    };

    [Fact]
    public void Component_library_emit_calls_match_the_commented_allow_list_exactly()
    {
        var root = RepositoryRoot();
        foreach (var lane in Roots) Assert.NotEmpty(Files(root, lane));
        Assert.All(AllowList.Values, row => Assert.False(string.IsNullOrWhiteSpace(row.Reason)));
        var findings = Scan(root);
        var errors = Violations(findings, AllowList);
        Assert.True(errors.Count == 0, "The component library emits no telemetry (telemetry-cc-16); a member's own emit path belongs in the member's engine.\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Component_library_fence_reports_every_planted_emit_call()
    {
        var root = Path.Combine(Path.GetTempPath(), "t502-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "projections", "react");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "console.ts"), "// console.log('comment')\nconsole.log('fixed')\n");
            File.WriteAllText(Path.Combine(dir, "logger.cs"), "ILogger logger;\n");
            File.WriteAllText(Path.Combine(dir, "activity.cs"), "ActivitySource source;\n");
            File.WriteAllText(Path.Combine(dir, "console.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(dir, "event.ts"), "navigator.sendBeacon(url);\n");
            File.WriteAllText(Path.Combine(dir, "variable.ts"), "console.log(value)\nconsole.log(value.name)\nconsole.log(`value ${value}`)\nconsole.log('a' + value)\n");
            File.WriteAllText(Path.Combine(dir, "interpolated.cs"), "Console.WriteLine($\"{value}\");\n");
            File.WriteAllText(Path.Combine(dir, "ignored.test.ts"), "console.log(value)\n");
            var findings = Scan(root);
            Assert.Equal(new[] {
                "projections/react/activity.cs:1", "projections/react/console.cs:1", "projections/react/console.ts:2",
                "projections/react/event.ts:1", "projections/react/interpolated.cs:1", "projections/react/logger.cs:1",
                "projections/react/variable.ts:1", "projections/react/variable.ts:2", "projections/react/variable.ts:3", "projections/react/variable.ts:4"
            }, findings.Select(row => $"{row.Path}:{row.Line}"));
            Assert.True(findings.Single(row => row.Path == "projections/react/console.ts").Literal);
            var allowed = new Dictionary<string, (int Count, string Reason)> { ["projections/react/variable.ts"] = (4, "fixture") };
            var errors = Violations(findings, allowed);
            Assert.Contains(errors, error => error.StartsWith("projections/react/variable.ts:1:", StringComparison.Ordinal));
            Assert.All(findings.Where(row => row.Path == "projections/react/variable.ts" || row.Path == "projections/react/interpolated.cs"), row => Assert.False(row.Literal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed record Finding(string Path, int Line, string Text, bool Literal);

    private static List<string> Violations(Finding[] findings, Dictionary<string, (int Count, string Reason)> allowList)
    {
        var actual = findings.GroupBy(row => row.Path).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var errors = findings.Where(row => !allowList.ContainsKey(row.Path) || !row.Literal).Select(row => $"{row.Path}:{row.Line}: {row.Text}").ToList();
        foreach (var row in allowList)
            if (!actual.TryGetValue(row.Key, out var count) || count != row.Value.Count)
                errors.Add($"{row.Key}: expected {row.Value.Count} emit call(s), found {count}");
        return errors;
    }

    private static Finding[] Scan(string root) => Roots.SelectMany(lane => Files(root, lane))
        .SelectMany(file => File.ReadLines(file).Select((line, index) => (line, index))
            .Where(row => !IsComment(row.line) && Emit.IsMatch(row.line))
            .Select(row => new Finding(Path.GetRelativePath(root, file).Replace('\\', '/'), row.index + 1, row.line.Trim(), LiteralCall.IsMatch(row.line))))
        .OrderBy(row => row.Path, StringComparer.Ordinal).ThenBy(row => row.Line).ToArray();

    private static bool IsComment(string line)
    {
        var text = line.TrimStart();
        return text.StartsWith("//") || text.StartsWith("/*") || text.StartsWith("*") || text.StartsWith("<!--");
    }

    private static IEnumerable<string> Files(string root, string lane)
    {
        var scanRoot = Path.Combine(root, "projections", lane);
        if (!Directory.Exists(scanRoot)) yield break;
        var pending = new Stack<string>();
        pending.Push(scanRoot);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(child);
                if (!Skip.Contains(name) && !name.EndsWith(".tests", StringComparison.OrdinalIgnoreCase)) pending.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(dir))
                if (Extensions.Contains(Path.GetExtension(file)) && !Regex.IsMatch(Path.GetFileName(file), @"\.(test|spec|stories)\.", RegexOptions.IgnoreCase)) yield return file;
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
