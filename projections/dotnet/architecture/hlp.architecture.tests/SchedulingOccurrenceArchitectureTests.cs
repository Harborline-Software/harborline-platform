using System.Reflection;
using System.Text.RegularExpressions;
using Harborline.Blocks.Scheduling;
using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class SchedulingOccurrenceArchitectureTests
{
    private const string Owner = "Harborline.Blocks.Scheduling";
    private static readonly Dictionary<string, string> ProducerAllowList = new(StringComparer.Ordinal)
    {
        ["projections/dotnet/foundation/hlp.foundation.scheduling/Services/IRruleExpansionService.cs"] = "The date-math producer contract.",
        ["projections/dotnet/foundation/hlp.foundation.scheduling/Services/InMemoryRruleExpansionService.cs"] = "The one date-math producer implementation.",
        ["projections/dotnet/foundation/hlp.foundation.scheduling/DependencyInjection/SchedulingServiceCollectionExtensions.cs"] = "Registers the producer, generates no due work.",
        ["projections/dotnet/blocks/hlp.blocks.calendar/Services/CalendarEventExpansionService.cs"] = "Calendar events own their occurrence exceptions, not Scheduling obligations.",
        ["projections/dotnet/blocks/hlp.blocks.calendar/Services/AvailabilityExpansionService.cs"] = "Availability expands supply, not due work.",
        ["projections/dotnet/blocks/hlp.blocks.calendar/DependencyInjection/CalendarServiceCollectionExtensions.cs"] = "Registers the calendar producer dependency.",
    };

    // ADR-0121: these API consumers are job firings, not due occurrences. They are outside this repository's scan.
    private static readonly Dictionary<string, string> JobFiringCarveOuts = new(StringComparer.Ordinal)
    {
        ["NodeRecurringScheduleSource"] = "Workflow schedule triggers start jobs; a missed firing is not overdue work.",
        ["NodeEfRecurringInvoiceService"] = "The recurring run creates invoices; only the resulting unpaid invoice can be due.",
    };

    [Fact]
    public void DueSeamImplementationsBelongOnlyToScheduling()
    {
        var implementations = Directory.EnumerateFiles(AppContext.BaseDirectory, "Harborline.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom).SelectMany(a => a.GetTypes()).Where(IsDueSource).ToArray();
        Assert.Equal(5, implementations.Length);
        Assert.DoesNotContain(implementations, IsOffender);
    }

    [Fact]
    public void DueSeamFenceRejectsPlantedOffenderFromAnotherAssembly()
    {
        Assert.True(IsDueSource(typeof(PlantedDueSource)));
        Assert.True(IsOffender(typeof(PlantedDueSource)));
    }

    [Fact]
    public void DirectRruleConsumersOutsideSchedulingAreNamedAndExplained()
    {
        Assert.All(ProducerAllowList.Values.Concat(JobFiringCarveOuts.Values), reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        var root = RepositoryRoot();
        var callers = Directory.EnumerateFiles(Path.Combine(root, "projections", "dotnet"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(p => p.EndsWith(".tests", StringComparison.OrdinalIgnoreCase) || p is "obj" or "bin"))
            .Select(path => (Path: Path.GetRelativePath(root, path).Replace('\\', '/'), Source: File.ReadAllText(path)))
            .Where(row => ReferencesProducer(row.Source) && !row.Path.StartsWith("projections/dotnet/blocks/hlp.blocks.scheduling/", StringComparison.Ordinal))
            .Select(row => row.Path).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(ProducerAllowList.Keys.Order(StringComparer.Ordinal), callers);
    }

    [Theory]
    [InlineData("sealed class Bad(IRruleExpansionService producer) { void Run() { producer.ExpandOccurrences(); } }")]
    [InlineData("var producer = new InMemoryRruleExpansionService(); producer.ExpandOccurrences();")]
    public void DirectProducerFenceRejectsPlantedCallerOffAllowList(string caller)
    {
        const string path = "projections/dotnet/blocks/planted/SecondDueGenerator.cs";
        Assert.True(ReferencesProducer(caller));
        Assert.False(ProducerAllowList.ContainsKey(path));
        Assert.False(ReferencesProducer("// IRruleExpansionService is not a call\nsealed class Innocent {}"));
    }

    private static bool IsDueSource(Type type) => type.IsClass && !type.IsAbstract && typeof(IDueOccurrenceSource).IsAssignableFrom(type);
    private static bool IsOffender(Type type) => IsDueSource(type) && type.Assembly.GetName().Name != Owner;
    private static bool ReferencesProducer(string source) => Regex.IsMatch(
        Regex.Replace(source, @"//[^\r\n]*|/\*[\s\S]*?\*/", ""), @"\b(?:IRruleExpansionService|InMemoryRruleExpansionService)\b");
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed class PlantedDueSource : IDueOccurrenceSource
    {
        public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant) => [];
    }
}
