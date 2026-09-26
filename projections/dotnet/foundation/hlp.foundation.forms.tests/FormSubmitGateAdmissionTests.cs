using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// DES-0016 forms-ck-4 and forms-auth-16 (T-485 slice 3; T-724 ruling 77; T-747): the form declares who
/// may submit it, as exactly one of a role, a standing or a capability Access registers.
/// </summary>
public sealed class FormSubmitGateAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly AuthorizationCapabilityReference RoleGrant = new("access:role-grant");
    private static readonly AuthorizationCapabilityRegister Register =
        AuthorizationCapabilityRegister.FromDeclarations([new AuthorizationCapabilityDefinition(RoleGrant, 1)]);

    public static TheoryData<string> Arms => ["role", "standing", "capability"];

    [Theory(DisplayName = "forms-ck-4: a submit gate naming exactly one of role, standing or capability is stored and round-trips through JSON")]
    [MemberData(nameof(Arms))]
    public async Task OneArmGateIsStoredAndRoundTrips(string arm)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), Register);
        var gate = arm switch
        {
            "role" => new SubmitGate(Role: RoleReference.Domain("inspector")),
            "standing" => new SubmitGate(Standing: new RecordStandingReference("author")),
            _ => new SubmitGate(Capability: RoleGrant),
        };

        var stored = await store.RegisterAsync(Form(gate));
        var roundTripped = JsonSerializer.Deserialize<SubmitGate>(JsonSerializer.Serialize(stored.SubmitGate));

        Assert.Equal(gate, stored.SubmitGate);
        Assert.Equal(gate, roundTripped);
    }

    [Theory(DisplayName = "forms-ck-4: a submit gate naming no arm, or more than one, is refused by name")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedGateIsRefused(bool twoArms)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), Register);
        var gate = twoArms
            ? new SubmitGate(Role: RoleReference.Domain("inspector"), Capability: RoleGrant)
            : new SubmitGate();

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAsync(Form(gate)));

        Assert.Equal(FormDefinitionCodes.SubmitGateFormInvalid, refusal.Code);
        Assert.Equal("/submit_gate", refusal.Target);
    }

    [Fact(DisplayName = "forms-auth-16: a form declaring a capability Access does not register is refused by name and persists nothing")]
    public async Task FormOwnCapabilityIsRefused()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), Register);
        var definition = Form(new SubmitGate(Capability: new AuthorizationCapabilityReference("forms:submit-inspection")));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAsync(definition));

        Assert.Equal(FormDefinitionCodes.SubmitGateCapabilityUnknown, refusal.Code);
        Assert.Equal("/submit_gate/capability/name", refusal.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(definition.Tenant, definition.Id, definition.Version));
    }

    [Fact(DisplayName = "forms-auth-16: with no Access register, every capability arm is refused")]
    public async Task CapabilityArmWithoutRegisterIsRefused()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAsync(Form(new SubmitGate(Capability: RoleGrant))));

        Assert.Equal(FormDefinitionCodes.SubmitGateCapabilityUnknown, refusal.Code);
    }

    [Fact(DisplayName = "forms-ck-4: publish requires an explicit submit gate while draft saves remain ungated")]
    public async Task PublishRequiresGateWhileDraftSaveDoesNot()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), Register);

        var atomicRefusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(id: "missing-gate-atomic")));

        Assert.Equal(FormDefinitionCodes.SubmitGateRequired, atomicRefusal.Code);
        Assert.Equal("/submit_gate", atomicRefusal.Target);

        var transitionDraft = Form(id: "missing-gate-transition");
        await store.RegisterAsync(transitionDraft);
        var transitionRefusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.PublishAsync(transitionDraft.Tenant, transitionDraft.Id, transitionDraft.Version));

        Assert.Equal(FormDefinitionCodes.SubmitGateRequired, transitionRefusal.Code);
        Assert.Equal("/submit_gate", transitionRefusal.Target);

        await store.RegisterAsync(Form(id: "ungated-register-draft"));
        await store.CreateAsync(Form(id: "ungated-create-draft"));

        var gate = new SubmitGate(Role: RoleReference.Domain("inspector"));
        var atomicallyPublished = await store.RegisterAndPublishAsync(Form(gate, "gated-atomic"));
        var gatedTransitionDraft = Form(gate, "gated-transition");
        await store.RegisterAsync(gatedTransitionDraft);
        var transitioned = await store.PublishAsync(
            gatedTransitionDraft.Tenant, gatedTransitionDraft.Id, gatedTransitionDraft.Version);

        Assert.Equal(FormDefinitionStatus.Published, atomicallyPublished.Status);
        Assert.Equal(FormDefinitionStatus.Published, transitioned.Status);
    }

    [Fact(DisplayName = "forms-ck-4: RegisterAsync and CreateAsync refuse a directly persisted Published definition with no gate")]
    public async Task DirectPersistOfPublishedRequiresGate()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), Register);

        var registerRefusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAsync(Form(id: "direct-register-published") with { Status = FormDefinitionStatus.Published }));
        Assert.Equal(FormDefinitionCodes.SubmitGateRequired, registerRefusal.Code);
        Assert.Equal("/submit_gate", registerRefusal.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(new TenantId("tenant:acme"), new FormDefinitionId("direct-register-published"), new SemanticVersion(1, 0, 0)));

        var createRefusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.CreateAsync(Form(id: "direct-create-published") with { Status = FormDefinitionStatus.Published }));
        Assert.Equal(FormDefinitionCodes.SubmitGateRequired, createRefusal.Code);
        Assert.Equal("/submit_gate", createRefusal.Target);

        var gate = new SubmitGate(Role: RoleReference.Domain("inspector"));
        var registeredGated = await store.RegisterAsync(
            Form(gate, "direct-register-published-gated") with { Status = FormDefinitionStatus.Published });
        var createdGated = await store.CreateAsync(
            Form(gate, "direct-create-published-gated") with { Status = FormDefinitionStatus.Published });

        Assert.Equal(FormDefinitionStatus.Published, registeredGated.Status);
        Assert.Equal(FormDefinitionStatus.Published, createdGated.Status);
    }

    [Fact(DisplayName = "forms-ck-4: restoring a System-owned Withdrawn legacy revision with no gate is refused by the same stable code")]
    public async Task RestoreOfUngatedWithdrawnLegacyRevisionIsRefused()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), Register);
        // Simulates a row persisted before submit_gate became required at publish (T-756); no
        // write path in this package can produce an ungated Withdrawn revision today.
        var legacy = Form(id: "legacy-pack-projection") with { Status = FormDefinitionStatus.Withdrawn };
        store.SeedLegacyRevisionForTesting(legacy);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RestorePackProjectionAsync(legacy.Tenant, legacy.Id, legacy.Version));

        Assert.Equal(FormDefinitionCodes.SubmitGateRequired, refusal.Code);
        Assert.Equal("/submit_gate", refusal.Target);
        var stillWithdrawn = await store.GetAsync(legacy.Tenant, legacy.Id, legacy.Version);
        Assert.Equal(FormDefinitionStatus.Withdrawn, stillWithdrawn.Status);

        var gated = legacy with { SubmitGate = new SubmitGate(Role: RoleReference.Domain("inspector")) };
        store.SeedLegacyRevisionForTesting(gated);
        var restored = await store.RestorePackProjectionAsync(gated.Tenant, gated.Id, gated.Version);
        Assert.Equal(FormDefinitionStatus.Published, restored.Status);
    }

    private static FormDefinition Form(SubmitGate? gate = null, string id = "submit-gate")
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId(id),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: new TenantId("tenant:acme"),
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:submit-gate"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: [new FormSection("sec", InternationalizedText.FromInvariant("sec"), ["name"], access)],
                Rules: []),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now,
            SubmitGate: gate);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
