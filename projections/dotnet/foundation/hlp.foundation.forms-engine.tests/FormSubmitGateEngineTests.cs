using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;
using Xunit;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// DES-0016 forms-eng-2 (T-485 slice 3): the form's own submit gate (forms-ck-4) is checked on the
/// published head before anything else is resolved, and a denied submit evaluates and writes nothing.
/// </summary>
public sealed class FormSubmitGateEngineTests
{
    private static readonly SubmitGate InspectorGate = new(Role: RoleReference.Domain("inspector"));

    [Fact(DisplayName = "forms-eng-2: a submitter who fails the form's own gate is refused before reuse or the candidate is resolved, and nothing is written")]
    public async Task FailedGateRefusesBeforeResolvingAnything()
    {
        var reuse = new CountingReuseResolver();
        var gates = new GateAccess(satisfied: false);
        var harness = await Harness.CreateAsync(definitionFactory: Gated, reuseResolver: reuse, submitGates: gates);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "idem")));

        Assert.Equal([InspectorGate], gates.Asked);
        Assert.Equal(0, reuse.Calls);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "forms-eng-2: a submitter who satisfies the form's own gate submits one record")]
    public async Task SatisfiedGateSubmits()
    {
        var gates = new GateAccess(satisfied: true);
        var harness = await Harness.CreateAsync(definitionFactory: Gated, submitGates: gates);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "idem"));

        Assert.Equal([InspectorGate], gates.Asked);
        Assert.Equal(1, (await harness.Store.CountsAsync()).Submissions);
    }

    [Fact(DisplayName = "forms-eng-2: a form that declares a gate is refused when the host supplies no gate port")]
    public async Task DeclaredGateWithoutPortFailsClosed()
    {
        var harness = await Harness.CreateAsync(definitionFactory: Gated);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "idem")));
        Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);
    }

    private static FormDefinition Gated(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate };

    private sealed class GateAccess(bool satisfied) : IFormSubmitGateAccess
    {
        public List<SubmitGate> Asked { get; } = [];
        public ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default)
        {
            Asked.Add(gate);
            return ValueTask.FromResult(satisfied);
        }
    }

    private sealed class CountingReuseResolver : IReuseResolver
    {
        public int Calls { get; private set; }
        public ValueTask<ResolvedFormDefinition> ResolveAsync(FormDefinition definition, CancellationToken ct = default)
        {
            Calls++;
            return ValueTask.FromResult(new ResolvedFormDefinition(definition, new Dictionary<string, ReuseProvenance>()));
        }
    }
}
