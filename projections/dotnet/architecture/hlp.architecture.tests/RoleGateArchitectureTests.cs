using System.Text.RegularExpressions;

using Xunit;

namespace Harborline.Architecture.Tests;

public sealed partial class RoleGateArchitectureTests
{
    [Fact]
    public void Production_role_gates_use_qualified_references_and_common_resolvers()
    {
        var root = RepositoryRoot();
        var files = ProductionFiles(root).ToList();
        var offenders = FindRawRoleGateOffenders(files);

        Assert.True(offenders.Count == 0,
            "ROLE GATE TYPE FENCE: gate properties must not use raw string roles or local string role catalogues:\n" +
            string.Join("\n", offenders));

        Assert.Contains("RoleGateResolver.Allows", File.ReadAllText(Path.Combine(root,
            "projections", "dotnet", "foundation", "hlp.foundation.forms-engine", "FormCandidateEvaluator.cs")));
        // Ticket 226: the Blazor shell's role visibility moved from the component into the one api#58 mapper
        // (PackNavigationMapper.Map); the component consumes the mapped view model. The common-resolver rule
        // is asserted where the gate now lives.
        Assert.Contains("RoleGateResolver.Allows", File.ReadAllText(Path.Combine(root,
            "projections", "blazor", "ui", "hlp.ui.app-shell", "PackNavigationMapper.cs")));
        Assert.Contains("roleGateAllows", File.ReadAllText(Path.Combine(root,
            "projections", "react", "ui", "hlp.ui.app-shell", "src", "pack-navigation-mapper.ts")));
        Assert.Contains("roleVocabulary.resolve", File.ReadAllText(Path.Combine(root,
            "projections", "typescript", "contracts", "hlp.contracts.forms", "src", "workflow-admission.ts")));
    }

    [Fact]
    public void Scanner_detects_a_planted_raw_string_gate_and_catalogue()
    {
        var planted = new[]
        {
            ("Planted.cs", "public IReadOnlyList<string> RequiredRoles { get; init; }"),
            ("Planted.ts", "const localRoleCatalog = new Set<string>() /* Role catalogue */;"),
        };
        Assert.Equal(2, FindRawRoleGateOffenders(planted).Count);
    }

    internal static List<string> FindRawRoleGateOffenders(IEnumerable<(string Path, string Text)> files) =>
        files.Where(file => RawGatePattern().IsMatch(file.Text)).Select(file => file.Path)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static IEnumerable<(string Path, string Text)> ProductionFiles(string root)
    {
        var roots = new[]
        {
            "projections/dotnet/contracts/hlp.contracts.identities/Workflow",
            "projections/dotnet/contracts/hlp.contracts.identities/Forms",
            "projections/dotnet/foundation/hlp.foundation.forms",
            "projections/dotnet/foundation/hlp.foundation.forms-engine",
            "projections/dotnet/foundation/hlp.foundation.rule-authoring",
            "projections/dotnet/foundation/hlp.foundation.rule-runtime",
            "projections/react/ui/hlp.ui.app-shell/src",
            "projections/blazor/ui/hlp.ui.app-shell",
        };
        foreach (var relative in roots)
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, relative), "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.Contains(".tests", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains("__tests__", StringComparison.OrdinalIgnoreCase)))
            yield return (path, File.ReadAllText(path));
    }

    [GeneratedRegex(@"IReadOnlyList\s*<\s*string\s*>\??\s+(RequiredRoles|AllowedRoles|ReadRoles|WriteRoles|FieldReadRoles|FieldWriteRoles)|(?:requiredRoles|allowedRoles|readRoles|writeRoles|fieldReadRoles|fieldWriteRoles)\??\s*:\s*(?:readonly\s+)?string\[\]|(?:Set|HashSet)<string>[^;\n]*(?:Role|role)", RegexOptions.IgnoreCase)]
    private static partial Regex RawGatePattern();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
