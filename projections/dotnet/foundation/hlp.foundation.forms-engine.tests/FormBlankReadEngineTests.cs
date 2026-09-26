using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;
using Xunit;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// DES-0016 forms-eng-8 (T-485 slice 4, L342, L358): permission to submit a form implies reading its
/// blank form with no separate read permission. It implies nothing about reading a submission.
/// </summary>
public sealed class FormBlankReadEngineTests
{
    private static readonly FormSubmitGate InspectorGate = new(Role: RoleReference.Domain("inspector"));
    private static readonly HashSet<FormEngineAction> SubmitOnly = [FormEngineAction.Submit];

    [Fact(DisplayName = "forms-eng-8: a principal permitted only to submit reads the blank form without a separate read permission")]
    public async Task SubmitPermissionReadsBlankForm()
    {
        var harness = await Harness.CreateAsync(grantedActions: SubmitOnly);

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, null);

        Assert.NotNull(view);
        Assert.Equal([FormEngineAction.Read, FormEngineAction.Submit], harness.Context.Actions);
    }

    [Fact(DisplayName = "forms-eng-8: permission to submit does not imply reading an existing submission")]
    public async Task SubmitPermissionDoesNotReadSubmission()
    {
        var harness = await Harness.CreateAsync(grantedActions: SubmitOnly);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "idem"));

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId));
    }

    [Fact(DisplayName = "forms-eng-8: submit permission implies the blank read only for a principal the form's own gate admits")]
    public async Task ImpliedBlankReadPassesTheFormsGate()
    {
        var refused = new GateAccess(satisfied: false);
        var denied = await Harness.CreateAsync(definitionFactory: Gated, submitGates: refused, grantedActions: SubmitOnly);
        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await denied.Engine.RenderAsync(denied.Definition.Id, null));
        Assert.Equal([InspectorGate], refused.Asked);

        var admitted = new GateAccess(satisfied: true);
        var allowed = await Harness.CreateAsync(definitionFactory: Gated, submitGates: admitted, grantedActions: SubmitOnly);
        Assert.NotNull(await allowed.Engine.RenderAsync(allowed.Definition.Id, null));
        Assert.Equal([InspectorGate], admitted.Asked);
    }

    [Fact(DisplayName = "forms-eng-8: a principal with the read permission reads the blank form without the submit gate being asked")]
    public async Task ReadPermissionDoesNotAskTheSubmitGate()
    {
        var gates = new GateAccess(satisfied: false);
        var harness = await Harness.CreateAsync(definitionFactory: Gated, submitGates: gates, grantedActions: new HashSet<FormEngineAction> { FormEngineAction.Read });

        Assert.NotNull(await harness.Engine.RenderAsync(harness.Definition.Id, null));
        Assert.Empty(gates.Asked);
        Assert.Equal([FormEngineAction.Read], harness.Context.Actions);
    }

    private static FormDefinition Gated(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate };

    private sealed class GateAccess(bool satisfied) : IFormSubmitGateAccess
    {
        public List<FormSubmitGate> Asked { get; } = [];
        public ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, FormDefinition definition, FormSubmitGate gate, CancellationToken cancellationToken = default)
        {
            Asked.Add(gate);
            return ValueTask.FromResult(satisfied);
        }
    }
}
