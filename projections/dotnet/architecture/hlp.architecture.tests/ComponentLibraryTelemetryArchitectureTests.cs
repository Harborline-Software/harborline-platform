using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class ComponentLibraryTelemetryArchitectureTests
{
    private static readonly string[] Roots = ["react", "blazor", "shared"];
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs", ".cs", ".razor" };
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "tests", "test", "__tests__", "stories" };
    // Raw source text, no comment or string stripping: a mention in a comment or string is a finding too,
    // and only an exact allow-list row admits it. Bare console/Console/sendBeacon tokens keep a comment
    // between the receiver and the member (console /* x */ .log) from slipping past.
    private static readonly Regex Emit = new(@"\b(?:console\b|Console\b|ILogger(?:Factory)?\b|LoggerMessage\b|ActivitySource\b|System\s*\.\s*Diagnostics\s*\.\s*Metrics\b|new\s+Meter\s*\(|Debug\s*\.\s*(?:Write\w*|Print)\b|Trace\s*\.\s*(?:Write\w*|Trace\w*)\b|EventSource\b|sendBeacon\b|using\s+static\s+System\s*\.\s*Diagnostics\s*\.\s*(?:Debug|Trace)\b)|@opentelemetry/", RegexOptions.Compiled);
    private static readonly Regex LiteralCall = new("""^[A-Za-z_][\w.]*\s*\(\s*(?:'(?:[^'\\\r\n]|\\.)*'|"(?:[^"\\\r\n]|\\.)*")(?:\s*,\s*(?:'(?:[^'\\\r\n]|\\.)*'|"(?:[^"\\\r\n]|\\.)*"))*\s*\)\s*;?$""", RegexOptions.Compiled);

    private static readonly string[] ExpectedFindings = [
        "projections/blazor/emit.ts:1",
        "projections/blazor/member/ContentRemoved.test.cs:1",
        "projections/blazor/member/CrossType.test.cs:1",
        "projections/blazor/member/ValueLeak.test.cs:1",
        "projections/blazor/pkg/Default.test.cs:1",
        "projections/react/activity.cs:1",
        "projections/react/after-hole.ts:1",
        "projections/react/anchors.ts:1",
        "projections/react/anchors.ts:2",
        "projections/react/apostrophe.tsx:2",
        "projections/react/block.ts:1",
        "projections/react/brace-text.ts:1",
        "projections/react/bypasses.ts:1",
        "projections/react/bypasses.ts:2",
        "projections/react/bypasses.ts:3",
        "projections/react/bypasses.ts:4",
        "projections/react/bypasses.ts:6",
        "projections/react/bypasses.ts:8",
        "projections/react/comment-gap.ts:1",
        "projections/react/comment-gap.ts:2",
        "projections/react/comment-gap.ts:3",
        "projections/react/comment-lines.ts:4",
        "projections/react/console.cs:1",
        "projections/react/console.ts:1",
        "projections/react/console.ts:2",
        "projections/react/debug.razor:1",
        "projections/react/debug.razor:2",
        "projections/react/division-comment.ts:1",
        "projections/react/division-regex.ts:1",
        "projections/react/division-regex.ts:2",
        "projections/react/division.ts:1",
        "projections/react/dot-split.ts:1",
        "projections/react/dup.ts:1",
        "projections/react/dup.ts:2",
        "projections/react/error.cjs:1",
        "projections/react/error.cjs:2",
        "projections/react/event.ts:1",
        "projections/react/factory.tsx:1",
        "projections/react/html.razor:1",
        "projections/react/inert.ts:1",
        "projections/react/interpolated.cs:1",
        "projections/react/interpolation-hole.cs:1",
        "projections/react/logger.cs:1",
        "projections/react/message.js:1",
        "projections/react/meter.mjs:1",
        "projections/react/metrics.jsx:1",
        "projections/react/module.cts:2",
        "projections/react/module.mts:2",
        "projections/react/nested-follow.ts:1",
        "projections/react/nested-interpolation.cs:1",
        "projections/react/nested-template.ts:1",
        "projections/react/otel-dynamic.mjs:1",
        "projections/react/otel-from.ts:1",
        "projections/react/otel-require.cjs:1",
        "projections/react/otel.js:1",
        "projections/react/regex.ts:1",
        "projections/react/source.cs:1",
        "projections/react/static.cs:1",
        "projections/react/static.cs:2",
        "projections/react/static.cs:3",
        "projections/react/strings.ts:1",
        "projections/react/strings.ts:2",
        "projections/react/strings.ts:3",
        "projections/react/template-hole.ts:1",
        "projections/react/trace.cs:1",
        "projections/react/trace.cs:2",
        "projections/react/variable.ts:1",
        "projections/react/variable.ts:2",
        "projections/react/variable.ts:3",
        "projections/react/variable.ts:4",
        "projections/react/verbatim-interpolation.cs:1",
        "projections/react/verbatim.cs:1",
        "projections/shared/emit.ts:1",
    ];

    private static readonly string[] ExpectedAllowListErrors = [
        "projections/react/console.ts: stale allow-list row, no line reads: console.log('comment')",
        "projections/react/console.ts:1: // console.log('comment')",
        "projections/react/dup.ts:2: console.log('same')",
        "projections/react/interpolated.cs: allow-list row passes more than plain string literals: Console.WriteLine($\"{value}\");",
        "projections/react/interpolated.cs:1: Console.WriteLine($\"{value}\");",
        "projections/react/missing.ts: stale allow-list row, no line reads: console.log('gone')",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log('a' + value)",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log(`value ${value}`)",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log(value)",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log(value.name)",
        "projections/react/variable.ts:1: console.log(value)",
        "projections/react/variable.ts:2: console.log(value.name)",
        "projections/react/variable.ts:3: console.log(`value ${value}`)",
        "projections/react/variable.ts:4: console.log('a' + value)",
    ];

    // Each row admits exactly one line: the file, its exact trimmed text, and why it carries no rendered value.
    private static readonly Dictionary<(string Path, string Line), string> AllowList = new()
    {
        [("projections/react/ui/hlp.ui.text-area/src/TextArea.tsx", "console.warn('TextArea: the `invalid` prop is deprecated. Use `error` instead.')")] = "fixed deprecation sentence, no prop value",
    };

    [Fact]
    public void Component_library_emit_calls_match_the_commented_allow_list_exactly()
    {
        var root = RepositoryRoot();
        foreach (var lane in Roots) Assert.NotEmpty(Files(root, lane));
        Assert.All(AllowList.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        var errors = Violations(Scan(root), AllowList);
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
            File.WriteAllText(Path.Combine(dir, "module.cts"), "import value = require('./value');\nconsole.log(value);\n");
            File.WriteAllText(Path.Combine(dir, "module.mts"), "import { value } from './value.js';\nconsole.log(value);\n");
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
            File.WriteAllText(Path.Combine(dir, "regex.ts"), "const pattern = /https?:\\/\\//; console.log(value);\n");
            File.WriteAllText(Path.Combine(dir, "inert.ts"), "const help = \"console.log(value)\";\n");
            File.WriteAllText(Path.Combine(dir, "division.ts"), "const ratio = a / b; console.log(value); // comment\n");
            File.WriteAllText(Path.Combine(dir, "template-hole.ts"), "const text = `value ${console.log(value)}`;\n");
            File.WriteAllText(Path.Combine(dir, "interpolation-hole.cs"), "var text = $\"value {Console.WriteLine(value)}\";\n");
            File.WriteAllText(Path.Combine(dir, "nested-template.ts"), "const text = `${a ? `x${console.log(v)}` : b}`;\n");
            File.WriteAllText(Path.Combine(dir, "nested-follow.ts"), "const text = `${f(`inner ${x}`, console.log(v))}`;\n");
            File.WriteAllText(Path.Combine(dir, "nested-interpolation.cs"), "var text = $\"{(x ? $\"{Console.WriteLine(v)}\" : y)}\";\n");
            File.WriteAllText(Path.Combine(dir, "verbatim-interpolation.cs"), "var text = $@\"value {Console.WriteLine(v)}\";\n");
            File.WriteAllText(Path.Combine(dir, "division-regex.ts"), "const ratio = a / b; console.log(v) // x\nconst re = /['\"]/; console.log(v)\n");
            File.WriteAllText(Path.Combine(dir, "division-comment.ts"), "const ratio = a / b; // console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "brace-text.ts"), "const help = `Example { console.log(value) }`;\n");
            File.WriteAllText(Path.Combine(dir, "after-hole.ts"), "const help = `${value} console.log(value)`;\n");
            File.WriteAllText(Path.Combine(dir, "comment-gap.ts"), "console /* x */ .log(value)\nConsole /* x */ .WriteLine(value);\nnavigator /* x */ .sendBeacon(url);\n");
            File.WriteAllText(Path.Combine(dir, "dup.ts"), "console.log('same')\nconsole.log('same')\n");
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
            // A packable project compiles whatever its globs match, test-like name or not; a non-packable one ships nothing.
            var blazor = Path.Combine(root, "projections", "blazor");
            foreach (var name in new[] { "pkg", "member", "pkg.tests", "member.tests" }) Directory.CreateDirectory(Path.Combine(blazor, name));
            File.WriteAllText(Path.Combine(blazor, "pkg", "Pkg.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><ItemGroup><Compile Include=\"../member/**/*.cs\" Exclude=\"../member/Excluded.test.cs\" /><Compile Include=\"../member/Removed.test.cs\" /><Compile Remove=\"../member/Removed.test.cs\" /><Content Include=\"../member/CrossType.test.cs\" /><Compile Remove=\"../member/CrossType.test.cs\" /><Content Remove=\"../member/ContentRemoved.test.cs\" /></ItemGroup></Project>\n");
            File.WriteAllText(Path.Combine(blazor, "pkg", "Default.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "ValueLeak.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "Excluded.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "Removed.test.cs"), "Console.WriteLine(value);\n");
            // MSBuild keeps one list per item type: a Remove on one type leaves the same path in another.
            File.WriteAllText(Path.Combine(blazor, "member", "CrossType.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "ContentRemoved.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "pkg.tests", "Pkg.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><Compile Include=\"../member.tests/**/*.cs\" /></ItemGroup></Project>\n");
            File.WriteAllText(Path.Combine(blazor, "member.tests", "Probe.test.cs"), "Console.WriteLine(value);\n");

            var findings = Scan(root);
            Assert.Equal(ExpectedFindings, findings.Select(row => $"{row.Path}:{row.Line}"));

            string[] rowsUnderTest = ["projections/react/console.ts", "projections/react/dup.ts", "projections/react/variable.ts", "projections/react/interpolated.cs"];
            var allowed = new Dictionary<(string Path, string Line), string>
            {
                [("projections/react/console.ts", "console.log('fixed')")] = "fixture",
                [("projections/react/console.ts", "console.log('comment')")] = "fixture",
                [("projections/react/dup.ts", "console.log('same')")] = "fixture",
                [("projections/react/missing.ts", "console.log('gone')")] = "fixture",
                [("projections/react/variable.ts", "console.log(value)")] = "fixture",
                [("projections/react/variable.ts", "console.log(value.name)")] = "fixture",
                [("projections/react/variable.ts", "console.log(`value ${value}`)")] = "fixture",
                [("projections/react/variable.ts", "console.log('a' + value)")] = "fixture",
                [("projections/react/interpolated.cs", "Console.WriteLine($\"{value}\");")] = "fixture",
            };
            var errors = Violations(findings.Where(row => rowsUnderTest.Contains(row.Path)).ToArray(), allowed);
            Assert.Equal(ExpectedAllowListErrors, errors.Order(StringComparer.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed record Finding(string Path, int Line, string Text);

    private static List<string> Violations(Finding[] findings, Dictionary<(string Path, string Line), string> allowList)
    {
        var errors = allowList.Keys.Where(key => !LiteralCall.IsMatch(key.Line))
            .Select(key => $"{key.Path}: allow-list row passes more than plain string literals: {key.Line}").ToList();
        var unused = allowList.Keys.Where(key => LiteralCall.IsMatch(key.Line)).ToHashSet();
        errors.AddRange(findings.Where(row => !unused.Remove((row.Path, row.Text))).Select(row => $"{row.Path}:{row.Line}: {row.Text}"));
        errors.AddRange(unused.Select(key => $"{key.Path}: stale allow-list row, no line reads: {key.Line}"));
        return errors;
    }

    // The walk skips test-like names, so every file a packable project compiles is added back from its own items.
    private static Finding[] Scan(string root) => Roots.SelectMany(lane => Files(root, lane).Concat(PackageInputs(Path.Combine(root, "projections", lane))))
        .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
        .SelectMany(file => ScanFile(root, file))
        .OrderBy(row => row.Path, StringComparer.Ordinal).ThenBy(row => row.Line).ToArray();

    private static IEnumerable<Finding> ScanFile(string root, string file)
    {
        var source = File.ReadAllText(file);
        var lines = source.Split('\n');
        var path = Path.GetRelativePath(root, file).Replace('\\', '/');
        return Emit.Matches(source).Select(match => source.AsSpan(0, match.Index).Count('\n') + 1).Distinct()
            .Select(line => new Finding(path, line, lines[line - 1].Trim()));
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

    // Compile, RazorComponent and Content items of every packable project (IsPackable is not false), read with
    // MSBuild's semantics: Include and Exclude are ;-separated globs relative to the project, Remove applies in
    // document order to its own item type, and the SDK defaults (Compile **/*.cs; Content **/*.razor and wwwroot/**;
    // minus bin and obj) come first.
    // ponytail: no property or import evaluation; an Include that needs one fails the test instead of being skipped.
    private static IEnumerable<string> PackageInputs(string scanRoot)
    {
        if (!Directory.Exists(scanRoot)) return [];
        var projects = Directory.EnumerateFiles(scanRoot, "*.csproj", new EnumerationOptions { RecurseSubdirectories = true })
            .Where(path => !path.Replace('\\', '/').Split('/').Any(part => part is "node_modules" or "bin" or "obj"));
        return projects.SelectMany(ProjectInputs).Where(file => Extensions.Contains(Path.GetExtension(file)));
    }

    private static IEnumerable<string> ProjectInputs(string project)
    {
        var dir = Path.GetDirectoryName(project)!;
        var items = XDocument.Load(project).Descendants().ToArray();
        if (items.Any(e => e.Name.LocalName == "IsPackable" && e.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))) return [];
        // MSBuild keeps one list per item type, so a Remove drops paths only from its own type's list.
        HashSet<string> Defaults(string globs) => Globs(dir, globs).Except(Globs(dir, "bin/**;obj/**"), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inputs = new Dictionary<string, HashSet<string>>
        {
            ["Compile"] = Defaults("**/*.cs"),
            ["Content"] = Defaults("**/*.razor;wwwroot/**"),
            ["RazorComponent"] = new(StringComparer.OrdinalIgnoreCase),
        };
        foreach (var item in items.Where(e => inputs.ContainsKey(e.Name.LocalName)))
        {
            var list = inputs[item.Name.LocalName];
            var include = (string?)item.Attribute("Include");
            var remove = (string?)item.Attribute("Remove");
            if (include is not null)
            {
                Assert.False(Regex.IsMatch(include, @"[$@%]\("), $"{project}: evaluate this Include before scanning: {include}");
                var excluded = Globs(dir, (string?)item.Attribute("Exclude")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                list.UnionWith(Globs(dir, include).Where(file => !excluded.Contains(file)));
            }
            if (remove is not null) list.ExceptWith(Globs(dir, remove));
        }
        return inputs.Values.SelectMany(list => list);
    }

    private static IEnumerable<string> Globs(string dir, string? list) =>
        (list ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SelectMany(glob => Glob(dir, glob));

    private static IEnumerable<string> Glob(string dir, string glob)
    {
        var parts = glob.Replace('\\', '/').Split('/');
        var wild = Array.FindIndex(parts, part => part.IndexOfAny(['*', '?']) >= 0);
        if (wild < 0)
        {
            var file = Path.GetFullPath(Path.Combine(dir, glob));
            return File.Exists(file) ? [file] : [];
        }
        var baseDir = Path.GetFullPath(Path.Combine([dir, .. parts[..wild]]));
        if (!Directory.Exists(baseDir)) return [];
        var pattern = new Regex("^" + Regex.Escape(string.Join('/', parts[wild..]))
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal).Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase);
        return Directory.EnumerateFiles(baseDir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Where(file => pattern.IsMatch(Path.GetRelativePath(baseDir, file).Replace('\\', '/')))
            .Select(Path.GetFullPath);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
