using System.Text.RegularExpressions;
using Harborline.Blocks.Aggregates;
using Harborline.Blocks.EntityViews;
using Harborline.Blocks.MeasureCatalogue;
using Harborline.Blocks.Reports.Measures;
using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// T-624. One catalogue, reached the same way by both contract consumers, and no second path to
/// the report math for a platform consumer to fall back on.
/// </summary>
public sealed partial class MeasureCatalogueArchitectureTests
{
    private const string Tenant = "tenant-a";
    private const string Principal = "alice";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    // measure-catalogue-cc-3 / measure-catalogue-cc-6.
    [Fact]
    public async Task Views_and_Reports_callers_resolve_one_reference_and_obtain_identical_results()
    {
        var reference = new MeasureRef("work.open-value");
        var catalogue = Catalogue(reference);
        var views = new CatalogueViewMeasures(catalogue);
        ViewRow[] rows =
        [
            new("open-1", new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 10m }),
            new("hidden-1", new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 400m }),
            new("open-2", new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 20m }),
        ];

        // The Views contract caller.
        var fromViews = await views.EvaluateAsync(new ViewMeasureBinding(reference.Value,
            new Dictionary<string, string>(StringComparer.Ordinal)), rows, Now, Tenant, Principal);

        // The Reports contract caller, over exactly the same authorized inputs.
        var fromReports = await catalogue.EvaluateAsync(reference, new MeasureRequest(Tenant, Principal,
            new SuppliedRows(rows.Select(row => new MeasureRow(row.Id,
                new Dictionary<string, AggregateValue>(StringComparer.Ordinal)
                {
                    ["value"] = new(AggregateValueType.Decimal, CanonicalDecimal.Parse(((decimal)row.Values["value"]!).ToString(System.Globalization.CultureInfo.InvariantCulture))),
                })).ToArray()), Now));

        var reportsCell = fromReports.Groups.Single(group => group.Kind == AggregateGroupKind.GrandTotal).Measures.Single();
        Assert.Equal("30", reportsCell.Value);
        Assert.Equal(reportsCell.Value, fromViews.Value);

        // Both consumers resolve a bound report entry through the same method and the same
        // descriptor shape, with nothing naming its kind.
        var viaViews = await views.ResolveAsync(ReportMeasureEntries.TrialBalance.Value);
        var viaCatalogue = await catalogue.ResolveAsync(ReportMeasureEntries.TrialBalance);
        Assert.NotNull(viaViews);
        Assert.NotNull(viaCatalogue);
        Assert.Equal(viaCatalogue!.Reference.Value, viaViews!.Name);
        Assert.Equal(viaCatalogue.ParameterNames, viaViews.ParameterNames);
    }

    // measure-catalogue-cc-3 / measure-catalogue-cc-6.
    [Fact]
    public async Task An_unknown_reference_refuses_with_a_stable_code_and_pointer_before_row_enumeration()
    {
        var catalogue = Catalogue(new MeasureRef("work.open-value"));
        var views = new CatalogueViewMeasures(catalogue);
        var rows = new CountingRows([new ViewRow("open-1", new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 10m })]);

        Assert.Null(await views.ResolveAsync("work.absent"));
        var refusal = await Assert.ThrowsAsync<MeasureException>(async () => await views.EvaluateAsync(
            new ViewMeasureBinding("work.absent", new Dictionary<string, string>(StringComparer.Ordinal)),
            rows, Now, Tenant, Principal));

        Assert.Equal(MeasureCodes.UnknownReference, refusal.Code);
        Assert.Equal("/measures/work.absent", refusal.Pointer);
        Assert.Equal(0, rows.Enumerations);
    }

    // measure-catalogue-eng-9: the report math is reachable only through the catalogue, so no
    // platform consumer has a Reports-side copy to fall back to.
    [Fact]
    public void A_production_reports_side_math_copy_cannot_be_the_platform_consumers_fallback()
    {
        var root = RepositoryRoot();
        var owner = Path.Combine("blocks", "hlp.blocks.reports") + Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "projections"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".tests", StringComparison.OrdinalIgnoreCase)
                && !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")
                && !path.Contains(owner, StringComparison.Ordinal))
            .Where(path => RunsCartridgeMath(File.ReadAllText(path)))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "A measure consumer must reach the report computations through the measure catalogue, "
            + "never through a cartridge, the cartridge registry or the report runner: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("var result = await new TrialBalanceCartridge(source).ExecuteAsync(context, parameters);")]
    [InlineData("var cartridge = registry.Resolve<RentRollParameters, RentRollResult>(ReportKind.RentRoll);")]
    [InlineData("sealed class ViewReports { IReportRunner runner; }")]
    [InlineData("_ = new ReportCartridgeRegistry();")]
    public void Canary_rejects_a_planted_consumer_side_copy_of_the_report_math(string consumer) =>
        Assert.True(RunsCartridgeMath(consumer));

    [Fact]
    public void Reaching_the_same_figures_through_the_catalogue_remains_permitted() =>
        Assert.False(RunsCartridgeMath(
            "var result = await catalogue.EvaluateAsync(ReportMeasureEntries.TrialBalance, request, cancellationToken);"));

    private static bool RunsCartridgeMath(string text) => CartridgeMath().IsMatch(text);

    [GeneratedRegex(@"\b(?:TrialBalance|BalanceSheet|ProfitAndLoss|ProfitAndLossByProperty|ArAgingSummary|ApAgingSummary|RentRoll)(?:Cartridge|Parameters|Result)\b|\bReportCartridgeRegistry\b|\bIReportRunner\b|\bReportKind\s*\.")]
    private static partial Regex CartridgeMath();

    private static MeasureCatalogue Catalogue(MeasureRef reference)
    {
        var definition = new AggregateDefinition(1, "declared", 1, AggregateDefinitionStatus.Published, "Declared",
            new AggregateSourceDefinition("views:rows/v1", new Dictionary<string, AggregateValueType>(StringComparer.Ordinal)
            {
                ["value"] = AggregateValueType.Decimal,
            }),
            null, [], [new AggregateMeasure("sum", AggregateOperator.Sum, "value", AggregateValueType.Decimal, AggregateValueType.Decimal)],
            new AggregateTotals([], true), new AggregateBounds(100, 100, 100));
        return new MeasureCatalogue(new AccessProvider(new HostGate("hidden-1")),
        [
            new DeclaredMeasureEntry(reference, definition, "records:read", "work",
                new AggregateDefinitionValidator(), new AggregateHostBounds(1000, 1000, 1000)),
            .. ReportMeasureEntries.All(),
        ]);
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

    // The host's sole authorization decider.
    private sealed class HostGate(params string[] hidden) : IAuthorizationDecider
    {
        private readonly HashSet<string> _hidden = new(hidden, StringComparer.Ordinal);

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var allowed = !_hidden.Contains(request.Record.Id);
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, allowed,
                allowed ? "None" : "record_not_visible", "grant:1", [], []));
        }
    }

    private sealed class CountingRows(IReadOnlyList<ViewRow> rows) : IReadOnlyList<ViewRow>
    {
        internal int Enumerations { get; private set; }

        public ViewRow this[int index] => rows[index];
        public int Count => rows.Count;
        public IEnumerator<ViewRow> GetEnumerator()
        {
            Enumerations++;
            return rows.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
