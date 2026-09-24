using Harborline.Blocks.Aggregates;
using Harborline.Foundation.Authorization;
using Xunit;

using static Harborline.Blocks.MeasureCatalogue.Tests.CatalogueFixture;

namespace Harborline.Blocks.MeasureCatalogue.Tests;

public sealed class MeasureCatalogueTests
{
    private static readonly MeasureRef Declared0 = new("work.open-value");
    private static readonly MeasureRef Bound0 = new("work.staleness");

    // measure-catalogue-eng-8 / measure-catalogue-ck-3.
    [Fact]
    public async Task Declared_and_bound_entries_resolve_through_one_reference_type_and_method()
    {
        var catalogue = new MeasureCatalogue(new AccessProvider(new HostGate()),
            [Declared(Declared0), new StalenessEntry(Bound0)]);
        var rows = new[] { Row("r1", "a", 10m), Row("r2", "a", 20m) };

        // A consumer that cannot tell the two kinds apart: one loop, no branch on entry kind.
        var seen = new List<(string Reference, IReadOnlyList<string> Parameters, int Cells)>();
        foreach (var reference in new[] { Declared0, Bound0 })
        {
            var descriptor = await catalogue.ResolveAsync(reference);
            Assert.NotNull(descriptor);
            var result = await catalogue.EvaluateAsync(reference, Request(rows));
            Assert.Equal(reference, result.Reference);
            seen.Add((descriptor!.Reference.Value, descriptor.ParameterNames, result.Groups.Single(group => group.Kind == AggregateGroupKind.GrandTotal).Measures.Count));
        }

        Assert.Equal(["work.open-value", "work.staleness"], seen.Select(item => item.Reference));
        Assert.All(seen, item => Assert.True(item.Cells > 0));
        // The descriptor carries no kind for a consumer to dispatch on.
        Assert.DoesNotContain(typeof(MeasureDescriptor).GetProperties(), property =>
            property.Name.Contains("Kind", StringComparison.Ordinal) || property.Name.Contains("Declared", StringComparison.Ordinal));
    }

    // measure-catalogue-eng-8 / measure-catalogue-ck-3: a replacement of the other kind keeps the address.
    [Fact]
    public async Task Replacing_a_bound_entry_with_an_equivalent_declaration_keeps_every_reference()
    {
        var held = new MeasureRef("work.staleness");
        var rows = new[] { Row("r1", "a", 10m), Row("r2", "a", 20m) };
        var boundOnly = new MeasureCatalogue(new AccessProvider(new HostGate()), [new StalenessEntry(held)]);
        var before = await boundOnly.EvaluateAsync(held, Request(rows));

        // Same address, an entry of the other kind behind it, and the reference the consumer holds
        // is unchanged; only the definition of the figure moved.
        var declaredInstead = new MeasureCatalogue(new AccessProvider(new HostGate()),
            [Declared(held, Definition(measures: [new("rows", AggregateOperator.Count, null, null, AggregateValueType.Integer, Count: AggregateCountMode.Rows)]))]);
        var after = await declaredInstead.EvaluateAsync(held, Request(rows));

        Assert.Equal(before.Reference, after.Reference);
        Assert.Equal(2L, Cell(before, "rows").Value);
        Assert.Equal(2L, Cell(after, "rows").Value);
    }

    // measure-catalogue-eng-1 / measure-catalogue-cc-12.
    [Fact]
    public async Task The_production_access_filter_runs_before_paging_grouping_and_aggregation()
    {
        var gate = new HostGate("hidden-1", "hidden-2");
        var catalogue = new MeasureCatalogue(new AccessProvider(gate), [Declared(Declared0)]);
        MeasureRow[] interleaved =
        [
            Row("open-1", "a", 10m),
            Row("hidden-1", "a", 100m),
            Row("open-2", "a", 20m),
            Row("hidden-2", "a", 200m),
            Row("open-3", "a", 30m),
        ];

        // A page of three over five interleaved rows. Filtering after paging would see two.
        var result = await catalogue.EvaluateAsync(Declared0, Request(interleaved, page: new MeasurePage(0, 3)));

        Assert.Equal(3L, Cell(result, "count").Value);
        Assert.Equal("60", Cell(result, "sum").Value);
        Assert.All(gate.Instants, instant => Assert.Equal(Now, instant));
    }

    // measure-catalogue-eng-1 / measure-catalogue-cc-12: the caller narrows and never widens.
    [Fact]
    public async Task A_narrowing_is_admitted_and_a_widening_of_the_authored_filter_is_refused()
    {
        var authored = Bucket("a");
        var catalogue = new MeasureCatalogue(new AccessProvider(new HostGate()),
            [Declared(Declared0, Definition(authored))]);
        MeasureRow[] rows = [Row("r1", "a", 10m), Row("r2", "b", 100m)];

        var narrowed = await catalogue.EvaluateAsync(Declared0, Request(rows,
            narrow: new AggregateAllFilter([Bucket("a"), new AggregateComparisonFilter("value", AggregateComparisonOperator.Gte,
                new AggregateValue(AggregateValueType.Decimal, CanonicalDecimal.Parse("5")))])));
        Assert.Equal("10", Cell(narrowed, "sum").Value);

        var widened = await Assert.ThrowsAsync<MeasureException>(async () => await catalogue.EvaluateAsync(Declared0,
            Request(rows, narrow: new AggregateAnyFilter([Bucket("a"), Bucket("b")]))));
        Assert.Equal(MeasureCodes.FilterWidened, widened.Code);
        Assert.Equal("/measures/work.open-value/filter", widened.Pointer);
    }

    // measure-catalogue-eng-8 / measure-catalogue-ck-5: one measure, three bindings.
    [Fact]
    public async Task One_reference_bound_three_ways_uses_exactly_the_callers_rows_and_instant()
    {
        var catalogue = new MeasureCatalogue(new AccessProvider(new HostGate()), [new StalenessEntry(Bound0)]);
        MeasureRow[] live = [Row("r1", "a", 10m), Row("r2", "a", 20m)];
        MeasureRow[] draft = [Row("r1", "a", 10m)];
        var periodEnd = DateTimeOffset.Parse("2026-06-30T23:59:59Z", System.Globalization.CultureInfo.InvariantCulture);

        var atNow = await catalogue.EvaluateAsync(Bound0, Request(live));
        var atPeriod = await catalogue.EvaluateAsync(Bound0, Request(live, periodEnd));
        var overDraft = await catalogue.EvaluateAsync(Bound0, Request(draft));
        var pinned = await catalogue.EvaluateAsync(Bound0, new MeasureRequest(Tenant, Principal,
            new BasisRows(new PinnedBasis("basis-2026-06", live)), periodEnd));

        // Only the clock changed.
        Assert.Equal(2L, Cell(atNow, "rows").Value);
        Assert.Equal(2L, Cell(atPeriod, "rows").Value);
        Assert.NotEqual(Cell(atNow, "instant").Value, Cell(atPeriod, "instant").Value);
        // Only the rows changed.
        Assert.Equal(1L, Cell(overDraft, "rows").Value);
        Assert.Equal(Cell(atNow, "instant").Value, Cell(overDraft, "instant").Value);
        // The pinned basis is the caller's too, and it is what the result reports.
        Assert.Equal("basis-2026-06", pinned.Basis);
        Assert.Equal("live", atNow.Basis);
        Assert.Equal(Cell(atPeriod, "instant").Value, Cell(pinned, "instant").Value);
    }

    // measure-catalogue-cc-3 / measure-catalogue-cc-6.
    [Fact]
    public async Task An_unknown_reference_refuses_with_a_stable_code_and_pointer_before_row_enumeration()
    {
        var gate = new HostGate();
        var catalogue = new MeasureCatalogue(new AccessProvider(gate), [Declared(Declared0)]);
        var counted = new CountingRows([Row("r1", "a", 10m)]);

        Assert.Null(await catalogue.ResolveAsync(new MeasureRef("work.absent")));
        var refusal = await Assert.ThrowsAsync<MeasureException>(async () => await catalogue.EvaluateAsync(
            new MeasureRef("work.absent"), new MeasureRequest(Tenant, Principal, new SuppliedRows(counted), Now)));

        Assert.Equal(MeasureCodes.UnknownReference, refusal.Code);
        Assert.Equal("/measures/work.absent", refusal.Pointer);
        Assert.Equal(0, counted.Enumerations);
        Assert.Empty(gate.Instants);
    }

    // measure-catalogue-cc-3 / measure-catalogue-cc-6: two contract callers, one reference, one answer.
    [Fact]
    public async Task Two_contract_callers_obtain_identical_results_for_identical_authorized_inputs()
    {
        var catalogue = new MeasureCatalogue(new AccessProvider(new HostGate("hidden-1")), [Declared(Declared0)]);
        MeasureRow[] rows = [Row("open-1", "a", 10m), Row("hidden-1", "a", 100m), Row("open-2", "a", 20m)];

        var views = await catalogue.EvaluateAsync(Declared0, Request(rows));
        var reports = await catalogue.EvaluateAsync(Declared0, Request(rows));

        Assert.Equal(Cell(views, "sum").Value, Cell(reports, "sum").Value);
        Assert.Equal("30", Cell(views, "sum").Value);
        Assert.Equal(Cell(views, "count").Value, Cell(reports, "count").Value);
    }

    // The last acceptance line, on the declared adapter; the bound adapter is proved in
    // Harborline.Blocks.Reports.Tests.
    [Fact]
    public async Task Null_and_unavailable_stay_distinct_from_zero_through_the_declared_adapter()
    {
        var definition = Definition(measures:
        [
            new("nulls", AggregateOperator.Sum, "value", AggregateValueType.Decimal, AggregateValueType.Decimal, AggregateNullPolicy.Propagate, AggregateUnavailablePolicy.Propagate),
            new("unavailable", AggregateOperator.Sum, "value", AggregateValueType.Decimal, AggregateValueType.Decimal, AggregateNullPolicy.Ignore, AggregateUnavailablePolicy.Propagate),
        ]);
        var catalogue = new MeasureCatalogue(new AccessProvider(new HostGate()), [Declared(Declared0, definition)]);

        var withNull = await catalogue.EvaluateAsync(Declared0,
            Request([Row("r1", "a", 10m), Row("r2", "a", AggregateValue.Null(AggregateValueType.Decimal))]));
        var withUnavailable = await catalogue.EvaluateAsync(Declared0,
            Request([Row("r1", "a", 10m), Row("r2", "a", AggregateValue.Unavailable(AggregateValueType.Decimal))]));
        var empty = await catalogue.EvaluateAsync(Declared0, Request([]));

        Assert.Equal(AggregateCellState.Null, Cell(withNull, "nulls").State);
        Assert.Null(Cell(withNull, "nulls").Value);
        Assert.Equal(AggregateCellState.Unavailable, Cell(withUnavailable, "unavailable").State);
        Assert.Null(Cell(withUnavailable, "unavailable").Value);
        // A genuinely empty authorized set is a typed zero, which is a different answer again.
        Assert.Equal(AggregateCellState.Value, Cell(empty, "nulls").State);
        Assert.Equal("0", Cell(empty, "nulls").Value?.ToString());
    }

    [Fact]
    public void A_malformed_address_is_refused_and_an_address_cannot_be_claimed_twice()
    {
        Assert.Equal(MeasureCodes.ReferenceMalformed, Assert.Throws<MeasureException>(() => new MeasureRef("WorkOpenValue")).Code);
        Assert.False(MeasureRef.TryParse("nodots", out _));
        Assert.Equal(MeasureCodes.ReferenceDuplicated, Assert.Throws<MeasureException>(() =>
            new MeasureCatalogue(new AccessProvider(new HostGate()), [Declared(Declared0), new StalenessEntry(Declared0)])).Code);
    }

    // A stand-in for an entry written in code at bound depth: it folds over the caller's rows at
    // the caller's instant and reports both, so a binding change is visible in the result.
    private sealed class StalenessEntry(MeasureRef reference) : IMeasureEntry
    {
        public MeasureRef Reference { get; } = reference;
        public string Operation => CatalogueFixture.Operation;
        public string RecordKind => Kind;
        public AggregateFilter? AuthoredFilter => null;
        public IReadOnlyList<string> ParameterNames { get; } = Array.Empty<string>();

        public ValueTask<MeasureResult> EvaluateAsync(MeasureEvaluation evaluation, CancellationToken cancellationToken = default)
        {
            var rows = evaluation.Basis is PinnedBasis pinned ? pinned.Rows : evaluation.Rows;
            return ValueTask.FromResult(new MeasureResult(Reference, evaluation.BasisToken,
            [
                new AggregateGroup(AggregateGroupKind.GrandTotal, 0, Array.Empty<AggregateKey>(),
                [
                    new AggregateCell("rows", AggregateValueType.Integer, AggregateCellState.Value, (long)rows.Count),
                    new AggregateCell("instant", AggregateValueType.DateTime, AggregateCellState.Value,
                        evaluation.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ]),
            ]));
        }
    }

    private sealed record PinnedBasis(string Token, IReadOnlyList<MeasureRow> Rows) : IMeasureBasis;

    // Proves the refusal happens before anything walks the caller's rows.
    private sealed class CountingRows(IReadOnlyList<MeasureRow> rows) : IReadOnlyList<MeasureRow>
    {
        internal int Enumerations { get; private set; }

        public MeasureRow this[int index] => rows[index];
        public int Count => rows.Count;
        public IEnumerator<MeasureRow> GetEnumerator()
        {
            Enumerations++;
            return rows.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
