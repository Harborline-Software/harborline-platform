using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Records;
using Harborline.Foundation.RuleEngine.References;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// T-616/prevalidation-mutation, records-eng-5 and ADR 0124 decision 1: a derived field takes its named calculation's value
/// over the bound candidate before validation, failing closed. Oracles: literal expressions and the values arithmetic gives
/// them, the three stable codes this stage publishes, and DES-0030 field-runtime-ck-4 ("Nobody edits the value").
/// </summary>
public sealed class RecordDerivedFieldsTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly NamedCalculation LineTotal = new("line-total", "1.0.0", """{"*":[{"var":"price"},{"var":"qty"}]}""");
    private static readonly NamedCalculation WithTax = new("with-tax", "1.0.0", """{"+":[{"var":"total"},{"var":"tax"}]}""");

    private static RecordFieldCalculation Derived(string field, NamedCalculation calculation) => new(field, calculation.Pin);

    private static RecordDerivation Derive(string body, PinnedClosure? closure, params RecordFieldCalculation[] fields) =>
        RecordDerivedFields.Derive(JsonNode.Parse(body)!.AsObject(), fields, closure, At);

    private static PinnedClosure Closure(params NamedCalculation[] calculations) => new([], calculations);

    [Fact(DisplayName = "records-eng-5: a derived field takes its calculation's value and every other value is kept as supplied")]
    public void Records_eng_5_a_derived_field_takes_its_calculations_value_and_every_other_value_is_kept()
    {
        var bound = JsonNode.Parse("""{"price":2.5,"qty":4,"note":null}""")!.AsObject();

        var result = RecordDerivedFields.Derive(bound, [Derived("total", LineTotal)], Closure(LineTotal), At);

        Assert.Null(result.Refusal);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"price":2.5,"qty":4,"note":null,"total":10}"""), result.Candidate),
            result.Candidate!.ToJsonString());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"price":2.5,"qty":4,"note":null}"""), bound), "the bound body is not changed");
    }

    [Fact(DisplayName = "records-eng-5: a supplied value for a derived field is replaced, never kept")]
    public void Records_eng_5_a_supplied_value_for_a_derived_field_is_replaced()
    {
        var result = Derive("""{"price":2.5,"qty":4,"total":999}""", Closure(LineTotal), Derived("total", LineTotal));

        Assert.Equal("10", result.Candidate!["total"]!.ToJsonString());
    }

    [Fact(DisplayName = "records-eng-5: a derived field that reads another runs after it, so it never reads a supplied value for it")]
    public void Records_eng_5_a_derived_field_that_reads_another_runs_after_it()
    {
        // Declared out of dependency order, with a supplied total that must not be read.
        var result = Derive("""{"price":2.5,"qty":4,"tax":1,"total":999}""", Closure(LineTotal, WithTax),
            Derived("gross", WithTax), Derived("total", LineTotal));

        Assert.Null(result.Refusal);
        Assert.Equal("10", result.Candidate!["total"]!.ToJsonString());
        Assert.Equal("11", result.Candidate!["gross"]!.ToJsonString());
    }

    [Fact(DisplayName = "records-eng-5: no derived field leaves a copy of the candidate")]
    public void Records_eng_5_no_derived_field_leaves_a_copy_of_the_candidate()
    {
        var bound = JsonNode.Parse("""{"price":2.5}""")!.AsObject();

        var result = RecordDerivedFields.Derive(bound, [], null, At);

        Assert.Null(result.Refusal);
        Assert.NotSame(bound, result.Candidate);
        Assert.True(JsonNode.DeepEquals(bound, result.Candidate));
    }

    public static TheoryData<string, PinnedClosure?, string> Unresolved() => new()
    {
        { "no closure composed", null, "line-total" },
        { "the pin is absent from the closure", Closure(WithTax), "line-total" },
        { "the closure's expression differs from the pin's digest", Closure(new NamedCalculation("line-total", "1.0.0", """{"+":[{"var":"price"},{"var":"qty"}]}""")), "line-total" },
    };

    [Theory(DisplayName = "records-eng-5: a pin that does not resolve refuses calculation_unresolved")]
    [MemberData(nameof(Unresolved))]
    public void Records_eng_5_a_pin_that_does_not_resolve_refuses(string because, PinnedClosure? closure, string _)
    {
        var result = Derive("""{"price":2.5,"qty":4}""", closure, Derived("total", LineTotal));

        Assert.Null(result.Candidate);
        Assert.True(new RecordDerivedFieldRefusal("total", "records.mutate.calculation_unresolved") == result.Refusal, because);
    }

    [Fact(DisplayName = "records-eng-5: derived fields that read each other in a cycle refuse calculation_unreadable")]
    public void Records_eng_5_derived_fields_that_read_each_other_in_a_cycle_refuse()
    {
        var a = new NamedCalculation("a", "1.0.0", """{"+":[{"var":"b"},1]}""");
        var b = new NamedCalculation("b", "1.0.0", """{"+":[{"var":"a"},1]}""");

        var result = Derive("""{"a":1,"b":1}""", Closure(a, b), Derived("a", a), Derived("b", b));

        Assert.Null(result.Candidate);
        Assert.Equal("records.mutate.calculation_unreadable", result.Refusal!.Code);
    }

    [Fact(DisplayName = "records-eng-5: a derived field that reads itself refuses calculation_unreadable")]
    public void Records_eng_5_a_derived_field_that_reads_itself_refuses()
    {
        var self = new NamedCalculation("self", "1.0.0", """{"+":[{"var":"total"},1]}""");

        var result = Derive("""{"total":1}""", Closure(self), Derived("total", self));

        Assert.Equal(new RecordDerivedFieldRefusal("total", "records.mutate.calculation_unreadable"), result.Refusal);
    }

    [Fact(DisplayName = "records-eng-5: a calculation that reads beyond the record's own fields refuses calculation_unreadable")]
    public void Records_eng_5_a_calculation_that_reads_beyond_the_records_own_fields_refuses()
    {
        // A child-section aggregate: not a row-scoped read of the record's own fields (DES-0030 field-runtime-ck-4).
        var rollup = new NamedCalculation("rollup", "1.0.0", """{"agg":["sum","items","amount"]}""");

        var result = Derive("""{"items":[{"amount":1}]}""", Closure(rollup), Derived("total", rollup));

        Assert.Equal(new RecordDerivedFieldRefusal("total", "records.mutate.calculation_unreadable"), result.Refusal);
    }

    [Fact(DisplayName = "records-eng-5: a value that does not resolve refuses calculation_failed rather than being skipped")]
    public void Records_eng_5_a_value_that_does_not_resolve_refuses()
    {
        var sum = new NamedCalculation("money-sum", "1.0.0", """{"money.add":[{"var":"price"},{"var":"qty"}]}""");

        var result = Derive("""{"price":"not money","qty":4}""", Closure(sum), Derived("total", sum));

        Assert.Equal(new RecordDerivedFieldRefusal("total", "records.mutate.calculation_failed"), result.Refusal);
    }

    public static TheoryData<string, string> OverEnvelope() => new()
    {
        // 256 KiB of UTF-8 and 5,000 JSON values are the rule context's input envelope (RuleContextSnapshot.MaxUtf8Bytes, MaxNodes).
        { "a string over 256 KiB", "\"" + new string('x', 262_145) + "\"" },
        { "more than 5,000 JSON values", "[" + string.Join(",", Enumerable.Repeat("0", 5_001)) + "]" },
    };

    [Theory(DisplayName = "records-eng-5: a body the rule context cannot admit refuses calculation_failed rather than throwing")]
    [MemberData(nameof(OverEnvelope))]
    public void Records_eng_5_a_body_the_rule_context_cannot_admit_refuses(string because, string note)
    {
        // The calculation never reads note, but the whole candidate is the context it is captured from.
        var result = Derive("{\"price\":2.5,\"qty\":4,\"note\":" + note + "}", Closure(LineTotal), Derived("total", LineTotal));

        Assert.Null(result.Candidate);
        Assert.True(new RecordDerivedFieldRefusal("total", "records.mutate.calculation_failed") == result.Refusal, because);
    }
}
