using System.Reflection;
using System.Xml.Linq;

using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// Ticket 080: the projection tiers point inward. Kernel assemblies must not acquire foundation or block
/// dependencies, and foundation assemblies must not acquire block dependencies. The assembly names come from
/// the projects on disk so the fence follows the repository's real identities rather than a guessed prefix.
/// </summary>
public sealed class TierDirectionArchitectureTests
{
    private static readonly IReadOnlySet<string> KernelToFoundationExemptions = new HashSet<string>(StringComparer.Ordinal)
    {
        // WorkItems predates the systematic tier fence and directly injects ITenantContext and IPartyContext.
        // Naming only those two existing edges keeps the inversion visible without granting Kernel.WorkItems a
        // general foundation exemption; moving those context contracts belongs to a separate boundary change.
        "Harborline.Kernel.WorkItems -> Harborline.Foundation.Authorization",
        "Harborline.Kernel.WorkItems -> Harborline.Foundation.MultiTenancy",
    };

    [Fact]
    public void Kernel_assemblies_reference_no_foundation_or_blocks_assemblies()
    {
        var tiers = LoadTierAssemblies();
        var forbidden = tiers["foundation"].Keys.Concat(tiers["blocks"].Keys).ToHashSet(StringComparer.Ordinal);
        var found = FindViolations(tiers["kernel"], forbidden).ToList();

        // STALE DETECTION, matching harborline-api's ApiTierDependencyArchTests. An exemption that
        // outlives the edge it excuses is worse than no exemption: the debt reads as still-owed, and
        // the day someone reintroduces that exact edge the fence waves it through. Fixing an inversion
        // must therefore also delete its entry above, and this is what forces that.
        var stale = KernelToFoundationExemptions
            .Where(exemption => !found.Contains(exemption))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(
            stale.Count == 0,
            "STALE EXEMPTION: these edges no longer exist, so their entries in " +
            "KernelToFoundationExemptions must be deleted. Leaving them would silently re-permit the " +
            "edge if it came back:\n" + string.Join("\n", stale));

        var violations = found
            .Where(violation => !KernelToFoundationExemptions.Contains(violation))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "TIER DIRECTION VIOLATED: kernel assemblies must reference no foundation or blocks assembly. " +
            "Offending assembly references:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Foundation_assemblies_reference_no_blocks_assemblies()
    {
        var tiers = LoadTierAssemblies();
        var forbidden = tiers["blocks"].Keys.ToHashSet(StringComparer.Ordinal);
        var violations = FindViolations(tiers["foundation"], forbidden);

        Assert.True(
            violations.Count == 0,
            "TIER DIRECTION VIOLATED: foundation assemblies must reference no blocks assembly. " +
            "Offending assembly references:\n" + string.Join("\n", violations));
    }

    private static List<string> FindViolations(
        IReadOnlyDictionary<string, Assembly> assemblies,
        IReadOnlySet<string> forbidden)
    {
        return assemblies
            .SelectMany(pair => pair.Value.GetReferencedAssemblies()
                .Where(reference => reference.Name is not null && forbidden.Contains(reference.Name))
                .Select(reference => $"{pair.Key} -> {reference.Name}"))
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, Assembly>> LoadTierAssemblies()
    {
        var repositoryRoot = RepositoryRoot();
        return new[] { "kernel", "foundation", "blocks" }.ToDictionary(
            tier => tier,
            tier => (IReadOnlyDictionary<string, Assembly>)LoadTier(repositoryRoot, tier),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, Assembly> LoadTier(string repositoryRoot, string tier)
    {
        var tierRoot = Path.Combine(repositoryRoot, "projections", "dotnet", tier);
        var projects = Directory.EnumerateFiles(tierRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsAuxiliaryProject(path))
            .Select(path => new
            {
                Project = path,
                AssemblyName = XDocument.Load(path)
                    .Descendants("AssemblyName")
                    .Select(element => element.Value.Trim())
                    .Single(),
            })
            .OrderBy(project => project.AssemblyName, StringComparer.Ordinal)
            .ToList();

        Assert.True(projects.Count > 0, $"tier direction scan found no production projects under {tierRoot}");

        var loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, project.AssemblyName + ".dll");
            Assert.True(
                File.Exists(assemblyPath),
                $"tier direction scan could not load {project.AssemblyName} from {assemblyPath}; " +
                $"register production project {project.Project} in Harborline.Architecture.Tests.csproj");
            loaded.Add(project.AssemblyName, Assembly.LoadFrom(assemblyPath));
        }

        return loaded;
    }

    private static bool IsAuxiliaryProject(string projectPath)
    {
        var directoryName = Path.GetFileName(Path.GetDirectoryName(projectPath)) ?? string.Empty;
        return directoryName.EndsWith(".tests", StringComparison.OrdinalIgnoreCase)
            || directoryName.EndsWith(".restart-probe", StringComparison.OrdinalIgnoreCase);
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
}
