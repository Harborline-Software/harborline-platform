using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Models;
using Xunit;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// K9 (approved 2026-10-10) at submit: the host's projection sink declares which Record Type a form's submissions create
/// (<see cref="IFormRecordCreationDeclarations"/>), and missing target metadata never exempts the submit from that type's
/// creation policy. K9 allows a legacy mapping only where a compatibility contract permits it; Platform has none (it has
/// no legacy Records-creating form), so a published head with no target whose form is declared to create a type is
/// refused until migrated (F3), whatever its gate or the type's policy. A failing or ambiguous declaration, or a head
/// targeting another type, denies before anything is written. A head targeting its declared type follows the D3 target
/// rules, and a role type adds no gate (D1). Forms that create no Records submit as before. Oracles are literals or the
/// fixture decider's own objects.
/// </summary>
public sealed class FormRecordCreationSubmitTests
{
    private const string Declared = "notes.entry";
    private static readonly SubmitGate InspectorGate = new(Role: RoleReference.Domain("inspector"));

    public static TheoryData<bool, string> TargetlessDeclaredHeads => new()
    {
        { false, "FormOnly" }, { true, "FormOnly" },
        { false, "Role" }, { true, "Role" },
        { false, "Engine" }, { true, "Engine" },
    };

    [Theory(DisplayName = "K9 (F3): a head without a target whose form is declared to create a type is refused until migrated, whatever its gate or the type's policy; nothing is asked or written")]
    [MemberData(nameof(TargetlessDeclaredHeads))]
    public async Task TargetlessDeclaredHeadIsRefusedUntilMigrated(bool gated, string policy)
    {
        var sink = new DeclaringSink(Declared);
        var decider = new AllowingDecider();
        var reader = new Reader(Enum.Parse<RecordTypeCreationGate>(policy));
        var harness = await Harness.CreateAsync(definitionFactory: gated ? GatedLegacy : Legacy, submitGates: Port(decider),
            recordTypePolicies: reader, projectionSink: sink);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        // No compatibility contract defines a legacy mapping in Platform, so the declared type is never applied to a
        // targetless head: the policy reader and the decider are not asked, and nothing is persisted or delivered.
        Assert.Equal([("tenant-engine", "inspection")], sink.Asked);
        Assert.Empty(reader.Asked);
        Assert.Empty(decider.Requests);
        Assert.Empty(sink.Delivered);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "K9 (F3): the targetless refusal also covers the blank read a submit permission implies")]
    public async Task TargetlessDeclaredHeadBlankReadIsRefused()
    {
        var harness = await Harness.CreateAsync(definitionFactory: GatedLegacy, submitGates: Port(new AllowingDecider()),
            recordTypePolicies: new Reader(RecordTypeCreationGate.Role), projectionSink: new DeclaringSink(Declared),
            grantedActions: new HashSet<FormEngineAction> { FormEngineAction.Submit });

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.RenderAsync(harness.Definition.Id, null));
    }

    [Fact(DisplayName = "D1 with K9: an ungated head targeting its declared role-gated type submits with no gate asked")]
    public async Task UngatedHeadTargetingDeclaredRoleTypeSubmits()
    {
        var sink = new DeclaringSink(Declared);
        var decider = new AllowingDecider();
        var reader = new Reader(RecordTypeCreationGate.Role);
        var harness = await Harness.CreateAsync(definitionFactory: UngatedTargeted, submitGates: Port(decider), recordTypePolicies: reader,
            projectionSink: sink);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Null(receipt.Authorization);
        Assert.Empty(decider.Requests);
        Assert.Equal([("tenant-engine", Declared)], reader.Asked);
        Assert.Single(sink.Delivered);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    public static TheoryData<string> Undecidable => ["ambiguous", "throws", "blank", "other-target", "no-policy-reader"];

    [Theory(DisplayName = "K9: an ambiguous, failing or malformed declaration, a head targeting another type, or no policy reader denies before anything is written")]
    [MemberData(nameof(Undecidable))]
    public async Task UndecidableCreationDenies(string shape)
    {
        var decider = new AllowingDecider();
        IFormProjectionSink sink = shape switch
        {
            "ambiguous" => new DeclaringSink(Declared, "notes.other"),
            "throws" => new ThrowingDeclaringSink(),
            "blank" => new DeclaringSink(""),
            _ => new DeclaringSink(Declared),
        };
        var harness = await Harness.CreateAsync(
            definitionFactory: shape == "other-target" ? TargetedOther : Targeted,
            submitGates: Port(decider),
            recordTypePolicies: shape == "no-policy-reader" ? null : new Reader(RecordTypeCreationGate.FormOnly),
            projectionSink: sink);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Empty(decider.Requests);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "K9: a head whose own target is the declared type submits exactly as a targeted form")]
    public async Task HeadTargetingTheDeclaredTypeSubmits()
    {
        var decider = new AllowingDecider();
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider),
            recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly), projectionSink: new DeclaringSink(Declared));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Equal(Declared, Assert.IsType<FormSubmitAuthorization>(receipt.Authorization).Intent.RecordTypeId);
        // A form_only target: accepted (ADR-0110, W6) and pending for the worker, not delivered inline.
        Assert.Equal((1, 1, 1, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "K9: a form the sink declares nothing for keeps its untargeted behaviour and asks no policy")]
    public async Task UndeclaredFormKeepsItsBehaviour()
    {
        var sink = new DeclaringSink();
        var reader = new Reader(RecordTypeCreationGate.FormOnly);
        var harness = await Harness.CreateAsync(definitionFactory: Legacy, recordTypePolicies: reader, projectionSink: sink);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Null(receipt.Authorization);
        Assert.Equal([("tenant-engine", "inspection")], sink.Asked);
        Assert.Empty(reader.Asked);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    // The harness's published head: no submit gate and no record type target, as a row persisted before either existed.
    private static FormDefinition Legacy(string schemaId, TenantId tenant) => Harness.CreateDefinition(schemaId, tenant);

    private static FormDefinition GatedLegacy(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate };

    private static FormDefinition Targeted(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate, RecordTypeId = Declared };

    private static FormDefinition UngatedTargeted(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { RecordTypeId = Declared };

    private static FormDefinition TargetedOther(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate, RecordTypeId = "notes.other" };

    private static AccessProviderFormSubmitGateAccess Port(AllowingDecider decider) =>
        new(new AccessProvider(decider), new FixedClock(FormEngineOrchestrationTests.Now));

    // A host projection sink that creates Records records and declares which type it creates for which form.
    private sealed class DeclaringSink(params string[] created) : IFormProjectionSink, IFormRecordCreationDeclarations, IFormAcceptedRecordBodies
    {
        // Review M4: the host's mapped-body digest an accepted form_only submit binds (any stable value here).
        public ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormAcceptedRecordBodyDigest("records-write-intent/1", "declaring-sink-digest"));

        public List<(string Tenant, string FormId)> Asked { get; } = [];
        public List<FormProjectionEnvelope> Delivered { get; } = [];

        public ValueTask<IReadOnlyCollection<string>> GetCreatedRecordTypesAsync(TenantId tenant, FormDefinitionId formId, CancellationToken cancellationToken = default)
        {
            Asked.Add((tenant.Value, formId.Value));
            return ValueTask.FromResult<IReadOnlyCollection<string>>(formId.Value == "inspection" ? created : []);
        }

        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Delivered.Add(envelope);
            return ValueTask.FromResult(new FormProjectionDeliveryResult([]));
        }
    }

    private sealed class ThrowingDeclaringSink : IFormProjectionSink, IFormRecordCreationDeclarations
    {
        public ValueTask<IReadOnlyCollection<string>> GetCreatedRecordTypesAsync(TenantId tenant, FormDefinitionId formId, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IReadOnlyCollection<string>>(new IOException("producer registry unavailable"));

        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("a denied submit delivers nothing");
    }

    private sealed class AllowingDecider : IAuthorizationDecider
    {
        public List<AccessRequest> Requests { get; } = [];
        public List<AuthorizationDecisionEvidence> Produced { get; } = [];

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var evidence = new AuthorizationDecisionEvidence(request, true, "None", "grant:fixture@1", [], []);
            Produced.Add(evidence);
            return ValueTask.FromResult(evidence);
        }
    }

    private sealed class Reader(RecordTypeCreationGate? declared) : IFormRecordTypePolicyReader
    {
        public List<(string Tenant, string RecordType)> Asked { get; } = [];

        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default)
        {
            Asked.Add((tenant.Value, recordTypeId));
            return ValueTask.FromResult(declared);
        }
    }

    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
