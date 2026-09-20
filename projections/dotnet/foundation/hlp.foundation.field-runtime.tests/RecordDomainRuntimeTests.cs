using Harborline.Contracts.Fields;
using Harborline.Foundation.RuleEngine;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class RecordDomainRuntimeTests
{
    [Fact]
    public async Task Cancellation_during_rules_evaluation_propagates_as_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new DomainFixture();
        fixture.Records["case"] = [DomainFixture.Member("a")];
        IFieldDomainRuntime runtime = new ValueDomainRuntime(fixture, fixture, clock: new CancellingClock(cancellation));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runtime.ResolveAsync(
            new(RecordQuery: new("case", "true")), DomainFixture.Scope, "/domain", cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData(false, "field.value_domain_predicate_failed")]
    [InlineData(true, "field.value_domain_predicate_invalid")]
    public async Task Rules_limits_refuse_failed_evaluation_and_compile_even_without_candidates(bool empty, string code)
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = empty ? [] : [DomainFixture.Member("a")];
        var limits = empty ? new RuleEngineLimits { MaxAstNodes = 0 } : new RuleEngineLimits { StepBudget = 0 };
        IFieldDomainRuntime runtime = new ValueDomainRuntime(fixture, fixture, limits);
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await runtime.ResolveAsync(
            new(RecordQuery: new("case", "true")), DomainFixture.Scope, "/domain"));
        Assert.Equal(code, Assert.Single(error.Refusals).Code);
        Assert.Equal("/domain", error.Refusals[0].JsonPointer);
    }

    private sealed class CancellingClock(CancellationTokenSource cancellation) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            cancellation.Cancel();
            return DateTimeOffset.UnixEpoch;
        }
    }

    [Fact]
    public async Task Cancellation_from_an_empty_candidate_read_is_not_returned_as_success()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new DomainFixture();
        fixture.Records["case"] = [];
        fixture.OnRecords = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Runtime().ResolveAsync(
            new(RecordQuery: new("case", "true")), DomainFixture.Scope, "/domain", cancellation.Token));
    }

    [Theory]
    [InlineData("not json", "{}", false, "field.value_domain_predicate_invalid")]
    [InlineData("not json", "{}", true, "field.value_domain_predicate_invalid")]
    [InlineData("{\"var\":42}", "{}", true, "field.value_domain_predicate_invalid")]
    [InlineData("{\"unknown_operator\":[]}", "{}", false, "field.value_domain_predicate_invalid")]
    [InlineData("{\"/\":[1,0]}", "{}", false, "field.value_domain_predicate_failed")]
    [InlineData("{\"var\":\"field.active\"}", "{\"active\":{\"@pending\":true}}", false, "field.value_domain_predicate_failed")]
    [InlineData("true", "[]", false, "field.value_domain_predicate_failed")]
    [InlineData("true", "{\"secret\":1,\"secret\":2}", false, "field.value_domain_predicate_failed")]
    public async Task Predicate_failures_refuse_the_resolution_instead_of_proving_empty_membership(
        string predicate, string fields, bool empty, string code)
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = empty ? [] : [DomainFixture.Member("secret", fields)];
        fixture.Hidden.Add("secret");
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await fixture.Runtime().ResolveAsync(
            new(RecordQuery: new("case", predicate)), DomainFixture.Scope, "/fields/a~1b/domain"));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal(code, refusal.Code);
        Assert.Equal("/fields/a~1b/domain", refusal.JsonPointer);
        Assert.DoesNotContain("secret", error.ToString() + refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_predicates_exclude_false_rows_then_filter_read_authority_without_normalizing_values()
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] =
        [
            DomainFixture.Member(" A ", "{\"active\":true}"),
            DomainFixture.Member("B", "{\"active\":false}"),
            DomainFixture.Member("secret", "{\"active\":true}"),
            DomainFixture.Member("a", "{\"active\":true}"),
        ];
        fixture.Hidden.Add("secret");
        const string predicate = "{\"==\":[{\"var\":\"field.active\"},true]}";
        var resolved = await fixture.Runtime().ResolveAsync(new(RecordQuery: new("case", predicate)), DomainFixture.Scope, "/domain");
        Assert.Equal(new[] { " A ", "a" }, resolved.Values);
        Assert.Equal(FieldEditorKind.RadioGroup, resolved.Editor);
        Assert.Equal(ValueDomainSourceKind.RecordQuery, resolved.SourceKind);
        Assert.Equal(predicate, resolved.Predicate);
        Assert.Equal("snapshot-1", resolved.SnapshotRevision);
    }
}
