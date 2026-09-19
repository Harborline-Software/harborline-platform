using System.Text.RegularExpressions;
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

    private static bool ComposesSupply(string text) => SecondComposition().IsMatch(text);

    [GeneratedRegex(@"\bISharedCalendarResolver\b|\bResolveSharedExceptionDays\w*\b|\bIAvailabilityExpansionService\b|\b\w*[Aa]vailabilityExpansion\w*\s*\.\s*Expand\s*\(")]
    private static partial Regex SecondComposition();
}
