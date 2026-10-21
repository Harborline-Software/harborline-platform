using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// T-485 / decision D3 (W4): a form may declare the Records record type its submission creates. Publication resolves the
/// target's creation gate through the narrow <see cref="IFormRecordTypePolicyReader"/> seam and fails closed when no
/// reader is wired, the reader fails, or the type is unknown (K6: Forms never defaults a policy). It refuses a form_only
/// target without a submit gate, an engine target, and a change to a target an earlier published revision captured
/// (ADR 0099 decision 7). A form without a target is unaffected.
/// Oracles: the refusal codes and pointers are literals from the approved plan (W4) and the existing Forms convention
/// (`form.submit_gate.required` at `/submit_gate`, T-756), never the production constants.
/// </summary>
public sealed class FormRecordTypeTargetAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly SubmitGate InspectorGate = new(Role: RoleReference.Domain("inspector"));

    public static TheoryData<string> FormOnlySources => ["no-reader", "unknown-type", "declared-form-only"];

    [Theory(DisplayName = "D3: publishing an ungated targeted form is refused at /submit_gate before any policy lookup and persists nothing")]
    [MemberData(nameof(FormOnlySources))]
    public async Task FormOnlyTargetWithoutGateIsRefusedAtPublication(string source)
    {
        var reader = source switch
        {
            "no-reader" => null,
            "unknown-type" => new RecordingReader(null),
            _ => new RecordingReader(RecordTypeCreationGate.FormOnly),
        };
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);
        var definition = Form(target: "notes.entry");

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(definition));

        Assert.Equal("form.submit_gate.required", refusal.Code);
        Assert.Equal("/submit_gate", refusal.Target);
        if (reader is not null) Assert.Empty(reader.Asked);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(definition.Tenant, definition.Id, definition.Version));
    }

    [Fact(DisplayName = "D3: the publication check refuses a form_only target with no gate by itself, independently of the generic publish rule")]
    public void PublicationCheckRefusesUngatedFormOnlyTarget()
    {
        var refusal = Assert.Throws<FormDefinitionValidationException>(() =>
            FormDefinitionValidation.ValidateRecordTypePublicationOrThrow(Form(target: "notes.entry"), RecordTypeCreationGate.FormOnly));

        Assert.Equal("form.submit_gate.required", refusal.Code);
        Assert.Equal("/submit_gate", refusal.Target);

        // A gated form_only target, a role target and an untargeted form pass the target check.
        FormDefinitionValidation.ValidateRecordTypePublicationOrThrow(Form(InspectorGate, target: "notes.entry"), RecordTypeCreationGate.FormOnly);
        FormDefinitionValidation.ValidateRecordTypePublicationOrThrow(Form(target: "notes.entry"), RecordTypeCreationGate.Role);
        FormDefinitionValidation.ValidateRecordTypePublicationOrThrow(Form(), RecordTypeCreationGate.Engine);
    }

    [Fact(DisplayName = "D3: a gated form targeting a form_only type publishes and keeps its target; the reader is asked for exactly that tenant and type")]
    public async Task GatedFormOnlyTargetPublishes()
    {
        var reader = new RecordingReader(RecordTypeCreationGate.FormOnly);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);

        var published = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry"));

        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal("notes.entry", published.RecordTypeId);
        Assert.Equal([("tenant:acme", "notes.entry")], reader.Asked);
    }

    [Fact(DisplayName = "D1: a gated form targeting a role-created type publishes; the form gate is not an extra creation gate")]
    public async Task RoleTargetPublishes()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RecordingReader(RecordTypeCreationGate.Role));

        var published = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "work.order"));

        Assert.Equal("work.order", published.RecordTypeId);
    }

    [Fact(DisplayName = "D3: a form targeting an engine-created type is refused at /record_type_id on every publication path and persists nothing published")]
    public async Task EngineTargetIsRefusedAtPublication()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RecordingReader(RecordTypeCreationGate.Engine));

        var atomic = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(InspectorGate, "engine-atomic", "ledger.posting")));
        Assert.Equal("form.record_type.not_form_creatable", atomic.Code);
        Assert.Equal("/record_type_id", atomic.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(async () =>
            await store.GetAsync(new TenantId("tenant:acme"), new FormDefinitionId("engine-atomic"), new SemanticVersion(1, 0, 0)));

        var direct = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () =>
            await store.RegisterAsync(Form(InspectorGate, "engine-direct", "ledger.posting") with { Status = FormDefinitionStatus.Published }));
        Assert.Equal("form.record_type.not_form_creatable", direct.Code);

        var draft = Form(InspectorGate, "engine-transition", "ledger.posting");
        await store.RegisterAsync(draft);
        var transition = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.PublishAsync(draft.Tenant, draft.Id, draft.Version));
        Assert.Equal("form.record_type.not_form_creatable", transition.Code);
        Assert.Equal("/record_type_id", transition.Target);
        Assert.Equal(FormDefinitionStatus.Draft, (await store.GetAsync(draft.Tenant, draft.Id, draft.Version)).Status);
    }

    [Theory(DisplayName = "T-485: a declared but blank record_type_id is refused at /record_type_id, even for a Draft")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankTargetIsRefused(string target)
    {
        var reader = new RecordingReader(RecordTypeCreationGate.FormOnly);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAsync(Form(target: target)));

        Assert.Equal("form.record_type.required", refusal.Code);
        Assert.Equal("/record_type_id", refusal.Target);
        Assert.Empty(reader.Asked);
    }

    [Fact(DisplayName = "D3: a Draft with a form_only target saves ungated without a Records lookup; publication is where the policy is read")]
    public async Task DraftSavesWithoutPolicyLookup()
    {
        var reader = new RecordingReader(RecordTypeCreationGate.FormOnly);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);

        var saved = await store.RegisterAsync(Form(target: "notes.entry"));

        Assert.Equal(FormDefinitionStatus.Draft, saved.Status);
        Assert.Equal("notes.entry", saved.RecordTypeId);
        Assert.Empty(reader.Asked);
    }

    [Fact(DisplayName = "untargeted: a form without a target publishes exactly as before and never consults the policy reader")]
    public async Task UntargetedFormIsUnaffected()
    {
        var reader = new RecordingReader(RecordTypeCreationGate.Engine);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);

        var published = await store.RegisterAndPublishAsync(Form(InspectorGate));
        var ungatedDraft = await store.RegisterAsync(Form(id: "untargeted-draft"));
        var ungatedPublish = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(id: "untargeted-ungated")));

        Assert.Null(published.RecordTypeId);
        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal(FormDefinitionStatus.Draft, ungatedDraft.Status);
        Assert.Equal("form.submit_gate.required", ungatedPublish.Code);
        Assert.Empty(reader.Asked);
        // The stored shape of an untargeted definition carries no target value.
        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(published).GetProperty("RecordTypeId").ValueKind);
    }

    [Fact(DisplayName = "forms-ck-3: the captured target is immutable within a version; an atomic retry naming another type conflicts")]
    public async Task TargetIsImmutableWithinAVersion()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RecordingReader(RecordTypeCreationGate.FormOnly));
        var first = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry"));

        var identical = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry"));
        await Assert.ThrowsAsync<FormDefinitionConflictException>(
            async () => await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.other")));

        Assert.Equal(first, identical);
        Assert.Equal("notes.entry", (await store.GetAsync(first.Tenant, first.Id, first.Version)).RecordTypeId);
    }

    public static TheoryData<string> UnresolvablePolicies => ["no-reader", "throwing-reader", "out-of-range-value", "unknown-type"];

    [Theory(DisplayName = "K6 (F3): a gated targeted form whose creation policy cannot be resolved is refused at /record_type_id and persists nothing")]
    [MemberData(nameof(UnresolvablePolicies))]
    public async Task UnresolvablePolicyFailsPublicationClosed(string source)
    {
        IFormRecordTypePolicyReader? reader = source switch
        {
            "no-reader" => null,
            "throwing-reader" => new ThrowingReader(),
            "out-of-range-value" => new RecordingReader((RecordTypeCreationGate)7),
            _ => new RecordingReader(null),
        };
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);
        var definition = Form(InspectorGate, target: "notes.entry");

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(definition));

        Assert.Equal(source == "unknown-type" ? "form.record_type.unknown" : "form.record_type.policy_unavailable", refusal.Code);
        Assert.Equal("/record_type_id", refusal.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(definition.Tenant, definition.Id, definition.Version));
    }

    [Fact(DisplayName = "K6 (F3): an untargeted form publishes with no policy reader wired")]
    public async Task UntargetedFormNeedsNoReader()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));

        var published = await store.RegisterAndPublishAsync(Form(InspectorGate));

        Assert.Equal(FormDefinitionStatus.Published, published.Status);
    }

    // ---- captured target continuity across versions (F2, ADR 0099 decision 7) ----------------------------------------

    [Theory(DisplayName = "ADR 0099 d7 (F2): a later version changing or removing a published revision's target is refused at /record_type_id")]
    [InlineData("notes.other")]
    [InlineData(null)]
    public async Task ChangingACapturedTargetIsRefused(string? laterTarget)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RecordingReader(RecordTypeCreationGate.FormOnly));
        await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry"));
        var later = Form(InspectorGate, target: laterTarget, version: new SemanticVersion(1, 1, 0));

        var atomic = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAndPublishAsync(later));
        Assert.Equal("form.record_type.changed", atomic.Code);
        Assert.Equal("/record_type_id", atomic.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(later.Tenant, later.Id, later.Version));

        // The same version saves as a Draft, and is refused when published by transition.
        await store.RegisterAsync(later);
        var transition = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.PublishAsync(later.Tenant, later.Id, later.Version));
        Assert.Equal("form.record_type.changed", transition.Code);
        Assert.Equal(FormDefinitionStatus.Draft, (await store.GetAsync(later.Tenant, later.Id, later.Version)).Status);
    }

    [Fact(DisplayName = "ADR 0099 d7 / K9 (F2): adding a target to a definition whose published revisions have none is allowed")]
    public async Task AddingATargetIsAllowed()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RecordingReader(RecordTypeCreationGate.FormOnly));
        await store.RegisterAndPublishAsync(Form(InspectorGate));

        var migrated = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry", version: new SemanticVersion(1, 1, 0)));
        var kept = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry", version: new SemanticVersion(1, 2, 0)));

        Assert.Equal("notes.entry", migrated.RecordTypeId);
        Assert.Equal("notes.entry", kept.RecordTypeId);
    }

    [Fact(DisplayName = "ADR 0099 d7 (F2): a Deprecated revision's captured target still binds later versions")]
    public async Task DeprecatedRevisionStillCapturesTarget()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RecordingReader(RecordTypeCreationGate.FormOnly));
        var first = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry"));
        await store.DeprecateAsync(first.Tenant, first.Id, first.Version);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () =>
            await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.other", version: new SemanticVersion(2, 0, 0))));

        Assert.Equal("form.record_type.changed", refusal.Code);
    }

    [Theory(DisplayName = "ADR 0099 d7 (R2-2): withdrawing the revision that captured a target does not release it; only the same target publishes")]
    [InlineData("notes.other", "form.record_type.changed")]
    [InlineData(null, "form.record_type.changed")]
    [InlineData("notes.entry", null)]
    public async Task WithdrawnRevisionStillCapturesTarget(string? laterTarget, string? expectedCode)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RecordingReader(RecordTypeCreationGate.FormOnly));
        var first = await store.RegisterAndPublishAsync(Form(InspectorGate, target: "notes.entry"));
        await store.WithdrawAsync(first.Tenant, first.Id, first.Version);
        var later = Form(InspectorGate, target: laterTarget, version: new SemanticVersion(2, 0, 0));

        if (expectedCode is null)
        {
            Assert.Equal("notes.entry", (await store.RegisterAndPublishAsync(later)).RecordTypeId);
            return;
        }

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAndPublishAsync(later));
        Assert.Equal(expectedCode, refusal.Code);
        Assert.Equal("/record_type_id", refusal.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(later.Tenant, later.Id, later.Version));
    }

    // ---- pack restore (F7) --------------------------------------------------------------------------------------------

    [Theory(DisplayName = "D3 (F7): restoring a System-owned Withdrawn revision re-runs the target checks; a refused restore stays Withdrawn")]
    [InlineData("engine-gated", "form.record_type.not_form_creatable", "/record_type_id")]
    [InlineData("form-only-ungated", "form.submit_gate.required", "/submit_gate")]
    [InlineData("no-reader-gated", "form.record_type.policy_unavailable", "/record_type_id")]
    public async Task PackRestoreRerunsTargetChecks(string shape, string code, string pointer)
    {
        IFormRecordTypePolicyReader? reader = shape switch
        {
            "engine-gated" => new RecordingReader(RecordTypeCreationGate.Engine),
            "form-only-ungated" => new RecordingReader(RecordTypeCreationGate.FormOnly),
            _ => null,
        };
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);
        var legacy = Form(shape == "form-only-ungated" ? null : InspectorGate, "pack-restore", "notes.entry") with { Status = FormDefinitionStatus.Withdrawn };
        store.SeedLegacyRevisionForTesting(legacy);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RestorePackProjectionAsync(legacy.Tenant, legacy.Id, legacy.Version));

        Assert.Equal(code, refusal.Code);
        Assert.Equal(pointer, refusal.Target);
        Assert.Equal(FormDefinitionStatus.Withdrawn, (await store.GetAsync(legacy.Tenant, legacy.Id, legacy.Version)).Status);
    }

    [Fact(DisplayName = "D3 (F7): a gated form_only pack revision restores, and its policy is read once, before the store lock")]
    public async Task PackRestoreOfGatedFormOnlyTargetSucceeds()
    {
        var reader = new RecordingReader(RecordTypeCreationGate.FormOnly);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader);
        var legacy = Form(InspectorGate, "pack-restore", "notes.entry") with { Status = FormDefinitionStatus.Withdrawn };
        store.SeedLegacyRevisionForTesting(legacy);

        var restored = await store.RestorePackProjectionAsync(legacy.Tenant, legacy.Id, legacy.Version);

        Assert.Equal(FormDefinitionStatus.Published, restored.Status);
        Assert.Equal([("tenant:acme", "notes.entry")], reader.Asked);
    }

    private static FormDefinition Form(SubmitGate? gate = null, string id = "record-target", string? target = null, SemanticVersion? version = null)
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId(id),
            Version: version ?? new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: new TenantId("tenant:acme"),
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:record-target"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: [new FormSection("sec", InternationalizedText.FromInvariant("sec"), ["name"], access)],
                Rules: []),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now,
            SubmitGate: gate,
            RecordTypeId: target);
    }

    private sealed class RecordingReader(RecordTypeCreationGate? declared) : IFormRecordTypePolicyReader
    {
        public List<(string Tenant, string RecordType)> Asked { get; } = [];

        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default)
        {
            Asked.Add((tenant.Value, recordTypeId));
            return ValueTask.FromResult(declared);
        }
    }

    private sealed class ThrowingReader : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<RecordTypeCreationGate?>(new IOException("records catalogue unavailable"));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
