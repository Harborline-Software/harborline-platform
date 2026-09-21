using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.LayoutRuntime;
using Harborline.Blocks.MeasureCatalogue;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

public sealed class LayoutMeasureBindingResolverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-09-21T09:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task ObserveMeasureDelegatesTheSuppliedRequestOnceAndReturnsTheTypedResult()
    {
        var expected = new MeasureResult(new MeasureRef("invoice.total"), "basis-42", []);
        var catalogue = new ProbeCatalogue((_, _, _) => ValueTask.FromResult(expected));
        var request = Request([]);
        using var cancellation = new CancellationTokenSource();

        var outcome = await new LayoutMeasureBindingResolver(catalogue).ResolveAsync(
            Block(new LayoutMeasureBinding("invoice.total"), LayoutIntent.Observe),
            LayoutIntent.Observe,
            "/blocks/2/binding",
            request,
            cancellation.Token);

        var result = Assert.IsType<LayoutMeasureBindingResult>(outcome);
        Assert.Equal("total", result.BlockId);
        Assert.Equal("measure", result.BindingKind);
        Assert.Same(expected, result.Measure);
        Assert.Equal(1, catalogue.Evaluations);
        Assert.Equal(new MeasureRef("invoice.total"), catalogue.Reference);
        Assert.Same(request, catalogue.Request);
        Assert.Equal(cancellation.Token, catalogue.CancellationToken);
    }

    [Fact]
    public async Task AbsentBlockIntentOnObserveSurfaceUsesInheritedIntent()
    {
        var expected = new MeasureResult(new MeasureRef("invoice.total"), "basis-42", []);
        var catalogue = new ProbeCatalogue((_, _, _) => ValueTask.FromResult(expected));

        var outcome = await new LayoutMeasureBindingResolver(catalogue).ResolveAsync(
            Block(new LayoutMeasureBinding("invoice.total"), intent: null),
            LayoutIntent.Observe,
            "/blocks/2/binding",
            Request([]));

        var result = Assert.IsType<LayoutMeasureBindingResult>(outcome);
        Assert.Same(expected, result.Measure);
        Assert.Equal(1, catalogue.Evaluations);
    }

    [Theory]
    [InlineData("invoice.absent", MeasureCodes.UnknownReference, "/measures/invoice.absent", 1)]
    [InlineData("Invoice Total", MeasureCodes.ReferenceMalformed, "/measures/Invoice Total", 0)]
    public async Task UnknownOrMalformedMeasureReturnsOneStableBindingRefusalWithoutEnumeratingRows(
        string measurePath,
        string causeCode,
        string causePointer,
        int expectedEvaluations)
    {
        var rows = new CountingRows([]);
        var catalogue = new ProbeCatalogue((reference, _, _) =>
            ValueTask.FromException<MeasureResult>(new MeasureException(
                MeasureCodes.UnknownReference,
                $"/measures/{reference.Value}",
                "Unknown measure.")));

        var outcome = await new LayoutMeasureBindingResolver(catalogue).ResolveAsync(
            Block(new LayoutMeasureBinding(measurePath), LayoutIntent.Observe),
            LayoutIntent.Observe,
            "/blocks/2/binding",
            Request(rows));

        var refusal = Assert.IsType<LayoutBindingRefusal>(outcome);
        Assert.Equal("total", refusal.BlockId);
        Assert.Equal("measure", refusal.BindingKind);
        Assert.Equal("/blocks/2/binding", refusal.Pointer);
        Assert.Equal(LayoutBindingCodes.Unresolvable, refusal.Code);
        Assert.Equal(causeCode, refusal.CauseCode);
        Assert.Equal(causePointer, refusal.CausePointer);
        Assert.Equal(expectedEvaluations, catalogue.Evaluations);
        Assert.Equal(0, rows.Enumerations);
    }

    [Fact]
    public async Task SharedMeasureRefusalRemainsVisibleAndIsNotRetried()
    {
        var catalogue = new ProbeCatalogue((_, _, _) =>
            ValueTask.FromException<MeasureResult>(new MeasureException(
                MeasureCodes.FilterWidened,
                "/measures/invoice.total/filter",
                "The filter widened the authored measure.")));

        var outcome = await new LayoutMeasureBindingResolver(catalogue).ResolveAsync(
            Block(new LayoutMeasureBinding("invoice.total"), LayoutIntent.Observe),
            LayoutIntent.Observe,
            "/blocks/2/binding",
            Request([]));

        var refusal = Assert.IsType<LayoutBindingRefusal>(outcome);
        Assert.Equal(LayoutBindingCodes.Unresolvable, refusal.Code);
        Assert.Equal(MeasureCodes.FilterWidened, refusal.CauseCode);
        Assert.Equal("/measures/invoice.total/filter", refusal.CausePointer);
        Assert.Equal(1, catalogue.Evaluations);
    }

    [Theory]
    [MemberData(nameof(UnsupportedBlocks))]
    public async Task NonMeasureOrNonObserveBlockRefusesAtTheMeasureBoundary(
        LayoutBlock block,
        string bindingKind)
    {
        var catalogue = new ProbeCatalogue((_, _, _) =>
            ValueTask.FromException<MeasureResult>(new InvalidOperationException("Must not evaluate.")));

        var outcome = await new LayoutMeasureBindingResolver(catalogue).ResolveAsync(
            block,
            LayoutIntent.Observe,
            "/blocks/2/binding",
            Request([]));

        var refusal = Assert.IsType<LayoutBindingRefusal>(outcome);
        Assert.Equal("total", refusal.BlockId);
        Assert.Equal(bindingKind, refusal.BindingKind);
        Assert.Equal("/blocks/2/binding", refusal.Pointer);
        Assert.Equal(LayoutBindingCodes.Unsupported, refusal.Code);
        Assert.Null(refusal.CauseCode);
        Assert.Null(refusal.CausePointer);
        Assert.Equal(0, catalogue.Evaluations);
    }

    [Theory]
    [InlineData(LayoutIntent.Capture)]
    [InlineData(LayoutIntent.Issue)]
    public async Task AbsentBlockIntentRefusesWhenSurfaceDefaultIsNotObserve(
        LayoutIntent surfaceDefaultIntent)
    {
        var catalogue = new ProbeCatalogue((_, _, _) =>
            ValueTask.FromException<MeasureResult>(new InvalidOperationException("Must not evaluate.")));

        var outcome = await new LayoutMeasureBindingResolver(catalogue).ResolveAsync(
            Block(new LayoutMeasureBinding("invoice.total"), intent: null),
            surfaceDefaultIntent,
            "/blocks/2/binding",
            Request([]));

        var refusal = Assert.IsType<LayoutBindingRefusal>(outcome);
        Assert.Equal(LayoutBindingCodes.Unsupported, refusal.Code);
        Assert.Equal(0, catalogue.Evaluations);
    }

    public static TheoryData<LayoutBlock, string> UnsupportedBlocks => new()
    {
        { Block(new LayoutQueryBinding("invoice.lines"), LayoutIntent.Observe), "query" },
        { Block(new LayoutMeasureBinding("invoice.total"), LayoutIntent.Capture), "measure" },
    };

    private static LayoutBlock Block(LayoutBinding binding, LayoutIntent? intent) =>
        new("total", "layout.metric", binding, [], intent);

    private static MeasureRequest Request(IReadOnlyList<MeasureRow> rows) =>
        new("tenant-a", "alice", new SuppliedRows(rows), Now);

    private sealed class ProbeCatalogue(
        Func<MeasureRef, MeasureRequest, CancellationToken, ValueTask<MeasureResult>> evaluate)
        : IMeasureCatalogue
    {
        internal int Evaluations { get; private set; }
        internal MeasureRef? Reference { get; private set; }
        internal MeasureRequest? Request { get; private set; }
        internal CancellationToken CancellationToken { get; private set; }

        public ValueTask<MeasureDescriptor?> ResolveAsync(
            MeasureRef reference,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The measure binding adapter uses the catalogue's one evaluation path.");

        public ValueTask<MeasureResult> EvaluateAsync(
            MeasureRef reference,
            MeasureRequest request,
            CancellationToken cancellationToken = default)
        {
            Evaluations++;
            Reference = reference;
            Request = request;
            CancellationToken = cancellationToken;
            return evaluate(reference, request, cancellationToken);
        }
    }

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
