using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// T-626 / ADR 0080: the availability supply is composed at exactly one site,
/// <c>FreeBusyService</c> (the <c>IAvailabilityRuntime</c>). Any other production code that resolves
/// shared exception days or expands supply itself is a second derivation, the defect ADR 0080 names
/// against <c>BookingService.ResolveSharedExceptionDays</c>.
/// </summary>
public sealed partial class AvailabilityCompositionArchitectureTests
{
    [Fact]
    public void Scheduling_consumes_foundation_availability_without_referencing_calendar()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        var scheduling = Path.Combine(root.FullName, "projections/dotnet/blocks/hlp.blocks.scheduling");
        Assert.False(ReferencesCalendar(File.ReadAllText(Path.Combine(scheduling, "Harborline.Blocks.Scheduling.csproj"))));
        var sources = Directory.EnumerateFiles(scheduling, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .Select(File.ReadAllText).ToArray();
        Assert.All(sources, text =>
        {
            Assert.DoesNotContain("Harborline.Blocks.Calendar", text, StringComparison.Ordinal);
            Assert.False(ComposesSupply(text));
        });
        Assert.Contains(sources, text => text.Contains("Harborline.Foundation.Scheduling.Services", StringComparison.Ordinal)
            && text.Contains("IAvailabilityRuntime", StringComparison.Ordinal));
    }

    // The composition site, the layer implementations it composes, and the DI file that registers them.
    private static readonly string[] CompositionFiles =
    [
        "FreeBusyService.cs",
        "AvailabilityExpansionService.cs", "IAvailabilityExpansionService.cs",
        "SharedCalendarResolver.cs", "ISharedCalendarResolver.cs",
        "CalendarServiceCollectionExtensions.cs",
    ];

    [Fact]
    public void Only_the_runtime_composes_availability_supply()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        var offenders = Directory.EnumerateFiles(Path.Combine(root.FullName, "projections"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".tests", StringComparison.OrdinalIgnoreCase)
                && !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")
                && !CompositionFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Where(path => ComposesSupply(File.ReadAllText(path))).ToArray();
        Assert.True(offenders.Length == 0, "Availability consumers must read through IAvailabilityRuntime: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("private readonly ISharedCalendarResolver? _sharedCalendarResolver;")]
    [InlineData("var days = await _sharedCalendarResolver.ResolveSharedExceptionDaysAsync(tenantId, resourceRef, localStart, localEnd, ct);")]
    [InlineData("var supply = _availabilityExpansion.Expand(availability, startUtc, endUtc, sharedExceptionDays);")]
    [InlineData("sealed class BookingGate { IAvailabilityExpansionService expansion; }")]
    public void Canary_rejects_a_planted_second_resolver_call(string consumer) => Assert.True(ComposesSupply(consumer));

    [Fact]
    public void Reading_through_the_runtime_remains_permitted() => Assert.False(ComposesSupply(
        "var read = await _runtime.Read(tenantId, new AvailabilityRequest(startUtc, endUtc, [ResourceCapacity.Exclusive(resourceRef, padding)]), ct);"));

    [Theory]
    [InlineData("<Project><ItemGroup><ProjectReference Include='../HLP.BLOCKS.CALENDAR/HARBORLINE.BLOCKS.CALENDAR.CSPROJ'/></ItemGroup></Project>", true)]
    [InlineData("<Project><ItemGroup><PackageReference Include='Harborline.Blocks.Calendar' Version='1.0.0'/></ItemGroup></Project>", true)]
    [InlineData("<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'><ItemGroup><packagereference Include='HARBORLINE.BLOCKS.CALENDAR'/></ItemGroup></Project>", true)]
    [InlineData("<Project><ItemGroup><ProjectReference Include='..\\hlp.blocks.calendar\\Harborline.Blocks.Calendar.csproj'/></ItemGroup></Project>", true)]
    [InlineData("<Project><!-- hlp.blocks.calendar --><ItemGroup><PackageReference Include='Harborline.Foundation.Scheduling'/></ItemGroup></Project>", false)]
    public void Calendar_reference_fence_checks_project_and_package_items(string project, bool expected)
        => Assert.Equal(expected, ReferencesCalendar(project));

    private static bool ReferencesCalendar(string project) => XDocument.Parse(project).Descendants().Any(item =>
    {
        var reference = (string?)item.Attribute("Include");
        if (item.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
            return string.Equals(Path.GetFileName(reference?.Replace('\\', '/')), "Harborline.Blocks.Calendar.csproj", StringComparison.OrdinalIgnoreCase);
        return item.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase)
            && string.Equals(reference, "Harborline.Blocks.Calendar", StringComparison.OrdinalIgnoreCase);
    });

    private static bool ComposesSupply(string text) => SecondComposition().IsMatch(text);

    [GeneratedRegex(@"\bISharedCalendarResolver\b|\bResolveSharedExceptionDays\w*\b|\bIAvailabilityExpansionService\b|\b\w*[Aa]vailabilityExpansion\w*\s*\.\s*Expand\s*\(")]
    private static partial Regex SecondComposition();
}
