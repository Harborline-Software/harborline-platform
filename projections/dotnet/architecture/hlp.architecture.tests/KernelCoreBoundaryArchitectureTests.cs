using System.Text.RegularExpressions;

using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class KernelCoreBoundaryArchitectureTests
{
    private static readonly Regex ForbiddenClock = new(
        @"(?:\b(?:DateTime(?:Offset)?\s*\.\s*(?:Now|UtcNow)|TimeProvider\s*\.\s*System|Environment\s*\.\s*TickCount(?:64)?)|:\s*TimeProvider\b|TimeProvider\s*\?)",
        RegexOptions.Compiled);

    private static readonly Regex StoreCommit = new(
        @"\.\s*(?:CommitAsync|CommitCreateAsync|CommitTransitionAsync)\s*\(",
        RegexOptions.Compiled);

    // Existing non-kernel-facing clocks are frozen, not blessed. Their owning member tickets must
    // remove the row when migrating the seam; T-623 removes Forms Engine and Work Items from it.
    private static readonly Dictionary<string, (int Count, string Reason)> ClockAllowList = new(StringComparer.Ordinal)
    {
        ["projections/dotnet/blocks/hlp.blocks.activity-timeline/ActivityTimeline.cs"] = (2, "member-local reference adapter"),
        ["projections/dotnet/blocks/hlp.blocks.reports/DependencyInjection/ReportSubstrateServiceCollectionExtensions.cs"] = (1, "report host composition"),
        ["projections/dotnet/blocks/hlp.blocks.scheduling/FileJournalSchedulingStore.cs"] = (2, "scheduling store adapter"),
        ["projections/dotnet/blocks/hlp.blocks.workflow.restart-probe/Program.cs"] = (2, "restart fixture executable"),
        ["projections/dotnet/blocks/hlp.blocks.workflow/Durable/DurableWorkflowServiceCollectionExtensions.cs"] = (1, "workflow host composition"),
        ["projections/dotnet/blocks/hlp.blocks.workflow/Durable/FileJournalWorkflowStore.cs"] = (2, "workflow store adapter"),
        ["projections/dotnet/blocks/hlp.blocks.workflow/InMemoryWorkflowRuntime.cs"] = (2, "retiring workflow runtime"),
        ["projections/dotnet/foundation/hlp.foundation.forms/DependencyInjection/FormsServiceCollectionExtensions.cs"] = (1, "Forms state host composition"),
        ["projections/dotnet/foundation/hlp.foundation.forms/Drafts/PreAuthCaptureService.cs"] = (2, "Forms draft member seam"),
        ["projections/dotnet/foundation/hlp.foundation.forms/Drafts/SubmissionDraftService.cs"] = (2, "Forms draft member seam"),
        ["projections/dotnet/foundation/hlp.foundation.forms/InMemoryFormDefinitionStore.cs"] = (1, "Forms reference adapter"),
        ["projections/dotnet/foundation/hlp.foundation.forms/InMemoryReusableUnitStore.cs"] = (1, "Forms reference adapter"),
        ["projections/dotnet/foundation/hlp.foundation.rule-authoring/RuleCatalog.cs"] = (2, "rule-authoring member seam"),
        ["projections/dotnet/foundation/hlp.foundation.rule-runtime/Evaluators.cs"] = (4, "rule-runtime member seams"),
        ["projections/dotnet/foundation/hlp.foundation.rule-runtime/Graph/FormRuleGraph.cs"] = (2, "rule graph member seam"),
    };

    private static readonly HashSet<string> AtomicAdapters = new(StringComparer.Ordinal)
    {
        "projections/dotnet/foundation/hlp.foundation.forms-engine/Persistence/FormSubmissionKernelTransactionPort.cs",
        "projections/dotnet/kernel/hlp.kernel.core/KernelTransactionBoundary.cs",
        "projections/dotnet/kernel/hlp.kernel.work-items/WorkItemKernelTransactionPort.cs",
        "projections/dotnet/kernel/hlp.kernel.work-items/FileJournalWorkItemStore.cs",
    };

    [Fact]
    public void ProductionWallTimeReferencesMatchTheCommentedAllowListExactly()
    {
        Assert.All(ClockAllowList.Values, row => Assert.False(string.IsNullOrWhiteSpace(row.Reason)));
        var actual = ScanClock(RepositoryRoot()).ToDictionary(row => row.Path, row => row.Count, StringComparer.Ordinal);
        Assert.Equal(
            ClockAllowList.Select(row => $"{row.Key}\t{row.Value.Count}").Order(StringComparer.Ordinal),
            actual.Select(row => $"{row.Key}\t{row.Value}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ClockFenceReportsExactlyFourPlantedReferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "t623-clock-" + Guid.NewGuid().ToString("N"));
        var planted = Path.Combine(root, "projections", "dotnet", "kernel", "planted");
        Directory.CreateDirectory(planted);
        try
        {
            File.WriteAllText(Path.Combine(planted, "Offender.cs"),
                "sealed class BadClock : TimeProvider { long N() => Environment.TickCount64; " +
                "DateTimeOffset U() => TimeProvider.System.GetUtcNow(); DateTime L() => DateTime.Now; }");

            var finding = Assert.Single(ScanClock(root));
            Assert.Equal("projections/dotnet/kernel/planted/Offender.cs", finding.Path);
            Assert.Equal(4, finding.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProducerWritesReachStoresOnlyThroughNamedAtomicAdapters()
    {
        var actual = ScanCommitCalls(RepositoryRoot()).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(AtomicAdapters.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CommitFenceFindsABypassOutsideTheKernelDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "t623-commit-" + Guid.NewGuid().ToString("N"));
        var planted = Path.Combine(root, "projections", "dotnet", "blocks", "planted");
        Directory.CreateDirectory(planted);
        try
        {
            File.WriteAllText(Path.Combine(planted, "Writer.cs"), "await store.CommitAsync(value);");
            Assert.Equal(
                ["projections/dotnet/blocks/planted/Writer.cs"],
                ScanCommitCalls(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (string Path, int Count)[] ScanClock(string root) =>
        ProductionFiles(root)
            .Select(row => (Path: row.Relative, Count: ForbiddenClock.Matches(StripComments(File.ReadAllText(row.File))).Count))
            .Where(row => row.Count > 0)
            .OrderBy(row => row.Path, StringComparer.Ordinal)
            .ToArray();

    private static string[] ScanCommitCalls(string root) =>
        ProductionFiles(root)
            .Where(row => StoreCommit.IsMatch(StripComments(File.ReadAllText(row.File))))
            .Select(row => row.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<(string File, string Relative)> ProductionFiles(string root)
    {
        var scanRoot = Path.Combine(root, "projections", "dotnet");
        if (!Directory.Exists(scanRoot)) return [];
        return Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => (File: file, Relative: Path.GetRelativePath(root, file).Replace('\\', '/')))
            .Where(row => !row.Relative.Split('/').Any(segment =>
                segment.EndsWith(".tests", StringComparison.OrdinalIgnoreCase)
                || segment is "bin" or "obj" or ".claude"))
            .Where(row => !row.Relative.EndsWith(".Designer.cs", StringComparison.Ordinal)
                && !row.Relative.EndsWith(".g.cs", StringComparison.Ordinal)
                && !row.Relative.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase));
    }

    private static string StripComments(string source) => Regex.Replace(
        source,
        @"//.*?$|/\*.*?\*/",
        string.Empty,
        RegexOptions.Multiline | RegexOptions.Singleline);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
