using Harborline.Blocks.Aggregates;
using Xunit;

namespace Harborline.Blocks.Aggregates.Tests;

/// <summary>The engine refuses, rather than guesses, when the definition, the actor, the source contract or a bound is wrong.</summary>
public sealed class EngineFailClosedTests
{
    private static readonly AggregateValueType Str = AggregateValueType.String, Int = AggregateValueType.Integer, Num = AggregateValueType.Number, Dec = AggregateValueType.Decimal;

    private static AggregateDefinition CountRows(AggregateValueType type, AggregateDimensionNullPolicy nulls = AggregateDimensionNullPolicy.Bucket) => TestFixture.Definition(
        new Dictionary<string, AggregateValueType> { ["f"] = type },
        [new("f", "f", type, nulls, AggregateSortDirection.Asc)],
        [new("c", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows)],
        totals: new AggregateTotals([], false));

    private static AggregateRow F(AggregateValue value) => TestFixture.Row(("f", value));

    private static async Task Fails(string code, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<AggregateException>(action);
        Assert.Equal(code, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static Task Mismatch(AggregateValueType type, object? raw) => Fails("aggregates.source.contract_mismatch", () => TestFixture.Evaluate(CountRows(type), F(new AggregateValue(type, raw))));

    [Fact] public Task NullContextIsRefused() => Assert.ThrowsAsync<ArgumentNullException>(() => Engine(new Probe()).EvaluateAsync(null!, TestFixture.Definition()).AsTask());
    [Fact] public Task NullDefinitionIsRefused() => Assert.ThrowsAsync<ArgumentNullException>(() => Engine(new Probe()).EvaluateAsync(new("t", "a"), null!).AsTask());

    [Fact]
    public async Task DraftRevisionIsNotEvaluatedAndNothingIsAuthorized()
    {
        var probe = new Probe();
        await Fails("aggregates.definition.invalid", () => Engine(probe).EvaluateAsync(new("t", "a"), TestFixture.Definition() with { Status = AggregateDefinitionStatus.Draft }).AsTask());
        Assert.Equal(0, probe.Authorizations);
        Assert.Equal(0, probe.Opens);
    }

    [Fact]
    public async Task InvalidPublishedDefinitionIsRefusedBeforeAuthorization()
    {
        var probe = new Probe();
        await Fails("aggregates.definition.invalid", () => Engine(probe).EvaluateAsync(new("t", "a"), TestFixture.Definition(measures: [])).AsTask());
        Assert.Equal(0, probe.Authorizations);
    }

    [Fact]
    public async Task DeniedActorGetsForbiddenAndTheSourceStaysClosed()
    {
        var probe = new Probe { Allowed = false };
        await Fails("aggregates.source.forbidden", () => Engine(probe).EvaluateAsync(new("t", "a"), TestFixture.Definition()).AsTask());
        Assert.Equal(0, probe.Opens);
    }

    [Fact]
    public async Task CancellationDuringAuthorizationStopsBeforeTheSourceOpens()
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new Probe { OnAuthorize = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine(probe).EvaluateAsync(new("t", "a"), TestFixture.Definition(), cancellation.Token).AsTask());
        Assert.Equal(0, probe.Opens);
    }

    [Fact]
    public async Task AuthorizationAndSourceSeeTheTrustedContextSourceBoundsAndEveryFieldTheEvaluationReads()
    {
        var fields = new Dictionary<string, AggregateValueType> { ["group"] = Str, ["measure"] = Dec, ["deep"] = Int, ["any"] = Int, ["all"] = Int, ["unused"] = Int };
        var filter = new AggregateNotFilter(new AggregateAnyFilter([new AggregateAllFilter([new AggregateComparisonFilter("deep", AggregateComparisonOperator.IsNull)]), new AggregateComparisonFilter("any", AggregateComparisonOperator.IsNull), new AggregateAllFilter([new AggregateComparisonFilter("all", AggregateComparisonOperator.IsNull)])]));
        var bounds = new AggregateBounds(7, 8, 9);
        var definition = TestFixture.Definition(fields, [new("g", "group", Str, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc)], [new("m", AggregateOperator.Sum, "measure", Dec, Dec)], filter, bounds: bounds);
        var probe = new Probe();
        await Engine(probe).EvaluateAsync(new("tenant-1", "actor-1"), definition);
        var expected = new[] { "all", "any", "deep", "group", "measure" };
        Assert.Equal(expected, probe.AuthorizedFields!.Order());
        Assert.Equal(expected, probe.OpenedFields!.Order());
        Assert.Equal(new AggregateExecutionContext("tenant-1", "actor-1"), probe.AuthorizedContext);
        Assert.Equal(new AggregateExecutionContext("tenant-1", "actor-1"), probe.OpenedContext);
        Assert.Equal("test:rows/v1", probe.AuthorizedSource);
        Assert.Equal("test:rows/v1", probe.OpenedSource);
        Assert.Equal(bounds, probe.OpenedBounds);
    }

    [Fact] public Task InputLimitIsRefused() => Fails("aggregates.evaluation.input_limit", () => TestFixture.Evaluate(TestFixture.Definition(bounds: new AggregateBounds(1, 10, 10)), Dec1(), Dec1()));
    [Fact] public async Task InputExactlyAtTheLimitIsEvaluated() => Assert.Equal("2", (await TestFixture.Evaluate(TestFixture.Definition(bounds: new AggregateBounds(2, 10, 10)), Dec1(), Dec1())).Groups.Last().Measures.Single().Value);
    [Fact] public async Task GroupsExactlyAtTheLimitAreEvaluated() => Assert.Equal(2, (await TestFixture.Evaluate(CountRows(Str) with { Bounds = new AggregateBounds(10, 2, 10) }, F(TestFixture.V(Str, "a")), F(TestFixture.V(Str, "b")))).Groups.Count);
    [Fact] public Task GroupLimitIsRefused() => Fails("aggregates.evaluation.group_limit", () => TestFixture.Evaluate(CountRows(Str) with { Bounds = new AggregateBounds(10, 1, 10) }, F(TestFixture.V(Str, "a")), F(TestFixture.V(Str, "b"))));

    [Fact]
    public async Task ResultCellsAreGroupsTimesMeasures()
    {
        var measures = new AggregateMeasure[] { new("c1", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows), new("c2", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows) };
        var rows = new[] { F(TestFixture.V(Str, "a")), F(TestFixture.V(Str, "b")) };
        Assert.Equal(2, (await TestFixture.Evaluate(CountRows(Str) with { Measures = measures, Bounds = new AggregateBounds(10, 10, 4) }, rows)).Groups.Count);
        await Fails("aggregates.evaluation.cell_limit", () => TestFixture.Evaluate(CountRows(Str) with { Measures = measures, Bounds = new AggregateBounds(10, 10, 3) }, rows));
    }

    [Fact] public Task RowMissingARequiredFieldIsRefused() => Fails("aggregates.source.contract_mismatch", () => TestFixture.Evaluate(CountRows(Str), TestFixture.Row(("other", TestFixture.V(Str, "x")))));
    [Fact] public Task RowValueOfAnotherDeclaredTypeIsRefused() => Fails("aggregates.measure.type_mismatch", () => TestFixture.Evaluate(CountRows(Str), F(TestFixture.V(Int, 5L))));
    [Fact] public Task ValueStateWithoutAValueIsRefused() => Mismatch(Str, null);
    [Fact] public Task UnavailableGroupingKeyIsRefused() => Fails("aggregates.source.contract_mismatch", () => TestFixture.Evaluate(CountRows(Str), F(AggregateValue.Unavailable(Str))));

    [Fact] public Task StringFieldRefusesAnInteger() => Mismatch(Str, 5);
    [Fact] public Task BooleanFieldRefusesAString() => Mismatch(AggregateValueType.Boolean, "true");
    [Fact] public Task IntegerFieldRefusesAString() => Mismatch(Int, "5");
    [Fact] public Task IntegerFieldRefusesADouble() => Mismatch(Int, 5d);
    [Fact] public Task NumberFieldRefusesNaN() => Mismatch(Num, double.NaN);
    [Fact] public Task NumberFieldRefusesInfinity() => Mismatch(Num, double.PositiveInfinity);
    [Fact] public Task NumberFieldRefusesNonFiniteFloat() => Mismatch(Num, float.NaN);
    [Fact] public Task DecimalFieldRefusesExponentText() => Mismatch(Dec, "1e5");
    [Fact] public Task DecimalFieldRefusesGarbage() => Mismatch(Dec, "abc");
    [Fact] public Task DecimalFieldRefusesABinaryNumber() => Mismatch(Dec, 5d);
    [Fact] public Task DateFieldRefusesNonIsoText() => Mismatch(AggregateValueType.Date, "2024-1-5");
    [Fact] public Task DateFieldRefusesGarbage() => Mismatch(AggregateValueType.Date, "garbage");
    [Fact] public Task DateFieldRefusesAnInstant() => Mismatch(AggregateValueType.Date, DateTimeOffset.UnixEpoch);
    [Fact] public Task DateTimeFieldRefusesANonUtcInstant() => Mismatch(AggregateValueType.DateTime, new DateTimeOffset(2024, 1, 5, 8, 0, 0, TimeSpan.FromHours(2)));
    [Fact] public Task DateTimeFieldRefusesGarbage() => Mismatch(AggregateValueType.DateTime, "garbage");

    [Fact]
    public async Task UnavailableInputFailsTheMeasureByDefault()
    {
        var definition = TestFixture.Definition();
        await Fails("aggregates.source.contract_mismatch", () => TestFixture.Evaluate(definition, TestFixture.Row(("value", AggregateValue.Unavailable(Dec)))));
    }

    [Fact]
    public async Task IntegerSumOverflowIsNotWrappedSilently()
    {
        var definition = TestFixture.Definition(new Dictionary<string, AggregateValueType> { ["value"] = Int }, measures: [new("m", AggregateOperator.Sum, "value", Int, Int)]);
        await Assert.ThrowsAnyAsync<Exception>(() => TestFixture.Evaluate(definition, Row(long.MaxValue), Row(1L)));
        static AggregateRow Row(long value) => TestFixture.Row(("value", TestFixture.V(Int, value)));
    }

    private static AggregateRow Dec1() => TestFixture.Row(("value", TestFixture.V(Dec, "1")));
    private static AggregateEngine Engine(Probe probe) => new(probe, probe, new AggregateDefinitionValidator(), new AggregateHostBounds(1000, 1000, 1000));

    private sealed class Probe : IAggregateRowSource, IAggregateAuthorization
    {
        public bool Allowed { get; init; } = true;
        public Action? OnAuthorize { get; init; }
        public int Authorizations { get; private set; }
        public int Opens { get; private set; }
        public IReadOnlySet<string>? AuthorizedFields { get; private set; }
        public IReadOnlySet<string>? OpenedFields { get; private set; }
        public AggregateExecutionContext? AuthorizedContext { get; private set; }
        public AggregateExecutionContext? OpenedContext { get; private set; }
        public string? AuthorizedSource { get; private set; }
        public string? OpenedSource { get; private set; }
        public AggregateBounds? OpenedBounds { get; private set; }

        public ValueTask<bool> AuthorizeAsync(AggregateExecutionContext context, string sourceRef, IReadOnlySet<string> requiredFields, CancellationToken cancellationToken)
        {
            Authorizations++; AuthorizedContext = context; AuthorizedSource = sourceRef; AuthorizedFields = requiredFields; OnAuthorize?.Invoke();
            return ValueTask.FromResult(Allowed);
        }

        public ValueTask<AggregateRowSnapshot> OpenSnapshotAsync(AggregateExecutionContext context, string sourceRef, IReadOnlySet<string> requiredFields, AggregateBounds bounds, CancellationToken cancellationToken)
        {
            Opens++; OpenedContext = context; OpenedSource = sourceRef; OpenedFields = requiredFields; OpenedBounds = bounds;
            return ValueTask.FromResult(new AggregateRowSnapshot("s", AsyncEnumerable.Empty<AggregateRow>()));
        }
    }
}
