using System.Text.RegularExpressions;

using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// Ticket 080: React and Blazor are separate implementation lanes. An import is a JavaScript/TypeScript
/// import, export-from, dynamic import, or require specifier; a C# or Razor using; a ProjectReference; or any
/// quoted relative path that resolves across the lane boundary. Violations report the source file and line.
/// </summary>
public sealed partial class LanePurityArchitectureTests
{
    private static readonly IReadOnlySet<string> ImportCapableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".cjs", ".js", ".json", ".jsx", ".mjs", ".props", ".razor", ".targets", ".ts", ".tsx",
    };

    private static readonly IReadOnlySet<string> SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "artifacts", "bin", "coverage", "dist", "node_modules", "obj",
    };

    [Fact]
    public void React_projection_imports_no_Blazor_paths()
    {
        AssertLaneIsPure("react", "blazor");
    }

    [Fact]
    public void Blazor_projection_imports_no_React_paths()
    {
        AssertLaneIsPure("blazor", "react");
    }

    private static void AssertLaneIsPure(string sourceLane, string forbiddenLane)
    {
        var repositoryRoot = RepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "projections", sourceLane);
        var forbiddenRoot = Path.Combine(repositoryRoot, "projections", forbiddenLane);
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(sourceRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (IsCommentOnly(line))
                {
                    continue;
                }

                foreach (var specifier in ImportSpecifiers(line))
                {
                    if (!CrossesLane(file, specifier, forbiddenRoot, forbiddenLane))
                    {
                        continue;
                    }

                    var relativeFile = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                    violations.Add($"{relativeFile}:{lineNumber}: {specifier}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"LANE PURITY VIOLATED: {sourceLane} projection files must not import from the {forbiddenLane} lane. " +
            "Offending file, line, and specifier:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory)
                         .Where(path => !SkippedDirectories.Contains(Path.GetFileName(path))))
            {
                pending.Push(child);
            }

            foreach (var file in Directory.EnumerateFiles(directory)
                         .Where(path => ImportCapableExtensions.Contains(Path.GetExtension(path))))
            {
                yield return file;
            }
        }
    }

    private static bool IsCommentOnly(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("<!--", StringComparison.Ordinal);
    }

    private static IEnumerable<string> ImportSpecifiers(string line)
    {
        var specifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var regex in new[] { ModuleSpecifierRegex(), UsingRegex(), ProjectReferenceRegex(), RelativePathRegex() })
        {
            foreach (Match match in regex.Matches(line))
            {
                var specifier = match.Groups["specifier"].Value.Trim();
                if (specifier.Length > 0 && specifiers.Add(specifier))
                {
                    yield return specifier;
                }
            }
        }
    }

    private static bool CrossesLane(string sourceFile, string specifier, string forbiddenRoot, string forbiddenLane)
    {
        var normalized = specifier.Replace('\\', '/');
        if (LaneNameRegex(forbiddenLane).IsMatch(normalized))
        {
            return true;
        }

        if (!normalized.StartsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        var pathOnly = normalized.Split('?', '#')[0];
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, pathOnly));
        return resolved.Equals(forbiddenRoot, StringComparison.OrdinalIgnoreCase)
            || resolved.StartsWith(forbiddenRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Regex LaneNameRegex(string lane)
    {
        return new Regex(
            $@"(^|[./@_\-]){Regex.Escape(lane)}([./@_\-]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "repository.yaml")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "could not locate the Harborline Platform repository root");
        return directory!.FullName;
    }

    [GeneratedRegex("""\b(?:from|import|require)\s*(?:\(\s*)?['"](?<specifier>[^'"]+)['"]""", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleSpecifierRegex();

    [GeneratedRegex(@"^\s*(?:global\s+)?@?using\s+(?<specifier>[^;]+?)\s*;?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UsingRegex();

    [GeneratedRegex("""(?:<ProjectReference\b[^>]*\b)?Include\s*=\s*['"](?<specifier>[^'"]+)['"]""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectReferenceRegex();

    [GeneratedRegex("""['"](?<specifier>\.\.[^'"]+)['"]""", RegexOptions.CultureInvariant)]
    private static partial Regex RelativePathRegex();
}
