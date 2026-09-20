using Harborline.Contracts.Fields;
using Harborline.Foundation.RuleEngine;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class RecordDomainInfrastructureTests
{
    [Fact]
    public async Task A_rules_infrastructure_timeout_does_not_become_a_field_admission_verdict()
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = [DomainFixture.Member("a")];
        var timeout = new RuleEngineTimeoutException();
        IFieldDomainRuntime runtime = new ValueDomainRuntime(fixture, fixture, clock: new FaultingClock(timeout));

        var actual = await Assert.ThrowsAsync<RuleEngineTimeoutException>(async () => await runtime.ResolveAsync(
            new(RecordQuery: new("case", "true")), DomainFixture.Scope, "/domain"));

        Assert.Same(timeout, actual);
    }

    // Inject a non-authoritative fault at the evaluator's external clock boundary.
    // The real GuardEvaluator and the field runtime's exception handling still execute.
    private sealed class FaultingClock(RuleEngineTimeoutException failure) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw failure;
    }
}
