using System.Text.RegularExpressions;
using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class ComponentLibraryTelemetryArchitectureTests
{
    private static readonly string[] Roots = ["react", "blazor", "shared"];
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".cs", ".razor" };
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "tests", "test", "__tests__", "stories" };
    private static readonly Regex Emit = new(@"\b(?:console\s*(?:(?:\?\.|\.)\s*\w+|\[\s*['""]\w+['""]\s*\])\s*(?:\?\.)?\s*\(|ILogger(?:Factory)?\b|LoggerMessage\b|ActivitySource\b|System\s*\.\s*Diagnostics\s*\.\s*Metrics\b|new\s+Meter\s*\(|Console\s*\.\s*(?:Write\w*|Error|Out)\b|Debug\s*\.\s*(?:Write\w*|Print)\b|Trace\s*\.\s*(?:Write\w*|Trace\w*)\b|EventSource\b|navigator\s*\.\s*sendBeacon\b|(?:import|from|require)\s*(?:\(\s*)?['""]@opentelemetry/|using\s+static\s+System\s*\.\s*(?:Diagnostics\s*\.\s*)?(?:Console|Debug|Trace)\b)", RegexOptions.Compiled);
    private static readonly Regex CommentsAndStrings = new("""(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/|<!--[\s\S]*?-->)|@"(?:""|[^"])*"|"(?:\\[^\r\n]|[^"\\\r\n])*"|'(?:\\[^\r\n]|[^'\\\r\n])*'|`(?:\\.|[^`\\])*`""", RegexOptions.Compiled);
    private static readonly Regex LiteralCall = new("""^\s*console\s*\.\s*\w+\s*\(\s*(?:'(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")\s*\)\s*;?\s*$""", RegexOptions.Compiled);

    private static readonly string[] ExpectedFindings = [
        "projections/blazor/emit.ts:1",
        "projections/react/activity.cs:1",
        "projections/react/anchors.ts:1",
        "projections/react/anchors.ts:1",
        "projections/react/anchors.ts:2",
        "projections/react/anchors.ts:2",
        "projections/react/apostrophe.tsx:2",
        "projections/react/bypasses.ts:1",
        "projections/react/bypasses.ts:2",
        "projections/react/bypasses.ts:3",
        "projections/react/bypasses.ts:4",
        "projections/react/bypasses.ts:6",
        "projections/react/bypasses.ts:8",
        "projections/react/comment-lines.ts:4",
        "projections/react/console.cs:1",
        "projections/react/console.ts:2",
        "projections/react/debug.razor:1",
        "projections/react/debug.razor:2",
        "projections/react/dot-split.ts:1",
        "projections/react/error.cjs:1",
        "projections/react/error.cjs:2",
        "projections/react/event.ts:1",
        "projections/react/factory.tsx:1",
        "projections/react/interpolated.cs:1",
        "projections/react/logger.cs:1",
        "projections/react/message.js:1",
        "projections/react/meter.mjs:1",
        "projections/react/metrics.jsx:1",
        "projections/react/otel-dynamic.mjs:1",
        "projections/react/otel-from.ts:1",
        "projections/react/otel-require.cjs:1",
        "projections/react/otel.js:1",
        "projections/react/source.cs:1",
        "projections/react/static.cs:1",
        "projections/react/static.cs:2",
        "projections/react/static.cs:3",
        "projections/react/strings.ts:1",
        "projections/react/strings.ts:2",
        "projections/react/strings.ts:3",
        "projections/react/trace.cs:1",
        "projections/react/trace.cs:2",
        "projections/react/variable.ts:1",
        "projections/react/variable.ts:2",
        "projections/react/variable.ts:3",
        "projections/react/variable.ts:4",
        "projections/react/verbatim.cs:1",
        "projections/shared/emit.ts:1",
    ];

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
            File.WriteAllText(Path.Combine(dir, "bypasses.ts"), "console?.log(value)\nconsole.log?.(value)\nconsole['log'](value)\nconsole\n .log(value)\nconsole.log\n(value)\n/* comment */ console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "static.cs"), "using static System.Console;\nusing static System.Diagnostics.Debug;\nusing static System.Diagnostics.Trace;\nWriteLine(value);\nWrite(value);\n");
            File.WriteAllText(Path.Combine(dir, "factory.tsx"), "ILoggerFactory factory;\n");
            File.WriteAllText(Path.Combine(dir, "message.js"), "LoggerMessage message;\n");
            File.WriteAllText(Path.Combine(dir, "metrics.jsx"), "System.Diagnostics.Metrics metrics;\n");
            File.WriteAllText(Path.Combine(dir, "meter.mjs"), "new Meter(name);\n");
            File.WriteAllText(Path.Combine(dir, "error.cjs"), "Console.Error.Write(value);\nConsole.Out.Write(value);\n");
            File.WriteAllText(Path.Combine(dir, "debug.razor"), "Debug.Print(value);\nDebug.Write(value);\n");
            File.WriteAllText(Path.Combine(dir, "trace.cs"), "Trace.TraceError(value);\nTrace.Write(value);\n");
            File.WriteAllText(Path.Combine(dir, "source.cs"), "EventSource source;\n");
            File.WriteAllText(Path.Combine(dir, "otel.js"), "import '@opentelemetry/api';\n");
            File.WriteAllText(Path.Combine(dir, "apostrophe.tsx"), "<p>Don't</p>\nconst u = 'http://x'; console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "otel-from.ts"), "import { trace } from '@opentelemetry/api';\n");
            File.WriteAllText(Path.Combine(dir, "otel-require.cjs"), "const api = require('@opentelemetry/api');\n");
            File.WriteAllText(Path.Combine(dir, "otel-dynamic.mjs"), "await import('@opentelemetry/api');\n");
            File.WriteAllText(Path.Combine(dir, "dot-split.ts"), "console.\nlog(value)\n");
            File.WriteAllText(Path.Combine(dir, "block.ts"), "/* console.log(value) */\n");
            File.WriteAllText(Path.Combine(dir, "html.razor"), "<!-- @{ Console.WriteLine(Value); } -->\n");
            File.WriteAllText(Path.Combine(dir, "comment-lines.ts"), "/*\n\n*/\nconsole.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "strings.ts"), "const u = 'http://x'; console.log(value)\nconst u = \"http://x\"; console.log(value)\nconst u = `http://x`; console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "verbatim.cs"), "var p = @\"a\\\"; var u = \"//\"; Console.WriteLine(p);\n");
            File.WriteAllText(Path.Combine(dir, "anchors.ts"), "console.warn('fixed'); console.log(value)\nconsole.log(value); console.warn('fixed')\n");
            foreach (var name in new[] { "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "tests", "test", "__tests__", "stories", "example.tests" })
            {
                var skipped = Path.Combine(dir, name);
                Directory.CreateDirectory(skipped);
                File.WriteAllText(Path.Combine(skipped, "ignored.ts"), "console.log(value);\n");
            }
            File.WriteAllText(Path.Combine(dir, "ignored.spec.ts"), "console.log(value);\n");
            File.WriteAllText(Path.Combine(dir, "ignored.stories.ts"), "console.log(value);\n");
            foreach (var lane in new[] { "blazor", "shared" })
            {
                var laneDir = Path.Combine(root, "projections", lane);
                Directory.CreateDirectory(laneDir);
                File.WriteAllText(Path.Combine(laneDir, "emit.ts"), "console.log(value);\n");
            }
            var findings = Scan(root);
            Assert.Contains(findings, row => row.Path == "projections/react/strings.ts" && row.Line == 1);
            Assert.Contains(findings, row => row.Path == "projections/react/strings.ts" && row.Line == 2);
            Assert.Contains(findings, row => row.Path == "projections/react/strings.ts" && row.Line == 3);
            Assert.Contains(findings, row => row.Path == "projections/react/verbatim.cs" && row.Line == 1);
            Assert.Contains(findings, row => row.Path == "projections/react/apostrophe.tsx" && row.Line == 2);
            Assert.Equal(ExpectedFindings, findings.Select(row => $"{row.Path}:{row.Line}"));
            Assert.True(findings.Single(row => row.Path == "projections/react/console.ts").Literal);
            var allowed = new Dictionary<string, (int Count, string Reason)> { ["projections/react/variable.ts"] = (4, "fixture"), ["projections/react/activity.cs"] = (0, "fixture"), ["projections/react/missing.ts"] = (1, "fixture"), ["projections/react/logger.cs"] = (2, "fixture"), ["projections/react/anchors.ts"] = (4, "fixture") };
            var errors = Violations(findings, allowed);
            Assert.Contains(errors, error => error.StartsWith("projections/react/variable.ts:1:", StringComparison.Ordinal));
            Assert.Contains("projections/react/console.ts:2: console.log('fixed')", errors);
            Assert.Contains("projections/react/activity.cs: expected 0 emit call(s), found 1", errors);
            Assert.Contains("projections/react/missing.ts: expected 1 emit call(s), found 0", errors);
            Assert.Contains("projections/react/logger.cs: expected 2 emit call(s), found 1", errors);
            Assert.All(findings.Where(row => row.Path == "projections/react/anchors.ts"), row => Assert.False(row.Literal));
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
        .SelectMany(file => ScanFile(root, file))
        .OrderBy(row => row.Path, StringComparer.Ordinal).ThenBy(row => row.Line).ToArray();

    private static IEnumerable<Finding> ScanFile(string root, string file)
    {
        var source = File.ReadAllText(file);
        var clean = CommentsAndStrings.Replace(source, match => match.Groups["comment"].Success
            ? new string(match.Value.Select(c => c is '\r' or '\n' ? c : ' ').ToArray()) : match.Value);
        var lines = clean.Split('\n');
        foreach (Match match in Emit.Matches(clean))
        {
            var line = clean.AsSpan(0, match.Index).Count('\n') + 1;
            var text = lines[line - 1].Trim();
            yield return new Finding(Path.GetRelativePath(root, file).Replace('\\', '/'), line, text, LiteralCall.IsMatch(text));
        }
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
