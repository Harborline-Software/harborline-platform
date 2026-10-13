using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// K9 (approved 2026-10-10): missing target metadata never exempts a form whose submissions create Records. The host's
/// submission producers declare which Record Type a form's submissions create (<see cref="IFormRecordCreationDeclarations"/>);
/// publication of any revision of a declared form requires exactly that <c>record_type_id</c> target, and the target's
/// creation policy is then checked as before (D3). A form the producers declare nothing for, and every form when no
/// producer declares anything, publishes as before. The store chooses its declaration by the same rule as the form
/// engine: the explicit port, else a producer (the engine's sink) that implements the port (F4). K1: a standing submit gate stays admissible on a form_only target.
/// Oracles: codes and pointers are literals.
/// </summary>
public sealed class FormRecordCreationDeclarationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly SubmitGate InspectorGate = new(Role: RoleReference.Domain("inspector"));

    [Fact(DisplayName = "K9: a declared form without a target is refused at /record_type_id on every publication path and nothing is published")]
    public async Task DeclaredFormWithoutTargetIsRefusedOnEveryPath()
    {
        var declarations = new Declarations(("capture", "notes.entry"));
        var reader = new Reader(RecordTypeCreationGate.FormOnly);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader, recordCreations: declarations);

        var atomic = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(InspectorGate, "capture")));
        var direct = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAsync(Form(InspectorGate, "capture") with { Status = FormDefinitionStatus.Published }));
        var draft = Form(InspectorGate, "capture");
        await store.RegisterAsync(draft);
        var transition = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.PublishAsync(draft.Tenant, draft.Id, draft.Version));

        foreach (var refusal in new[] { atomic, direct, transition })
        {
            Assert.Equal("form.record_type.target_required", refusal.Code);
            Assert.Equal("/record_type_id", refusal.Target);
        }
        Assert.Equal(FormDefinitionStatus.Draft, (await store.GetAsync(draft.Tenant, draft.Id, draft.Version)).Status);
        Assert.Null(await store.GetCurrentPublishedAsync(draft.Tenant, draft.Id));
        Assert.Empty(reader.Asked);
        Assert.All(declarations.Asked, asked => Assert.Equal(("tenant:acme", "capture"), asked));
    }

    [Fact(DisplayName = "K9: a pack restore of a declared Withdrawn revision without a target stays Withdrawn")]
    public async Task PackRestoreOfDeclaredTargetlessRevisionIsRefused()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly),
            recordCreations: new Declarations(("capture", "notes.entry")));
        var legacy = Form(InspectorGate, "capture") with { Status = FormDefinitionStatus.Withdrawn };
        store.SeedLegacyRevisionForTesting(legacy);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RestorePackProjectionAsync(legacy.Tenant, legacy.Id, legacy.Version));

        Assert.Equal(("form.record_type.target_required", "/record_type_id"), (refusal.Code, refusal.Target));
        Assert.Equal(FormDefinitionStatus.Withdrawn, (await store.GetAsync(legacy.Tenant, legacy.Id, legacy.Version)).Status);
    }

    [Fact(DisplayName = "K9: a target other than the declared type is refused at /record_type_id")]
    public async Task DeclaredFormWithAnotherTargetIsRefused()
    {
        var reader = new Reader(RecordTypeCreationGate.FormOnly);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader,
            recordCreations: new Declarations(("capture", "notes.entry")));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(InspectorGate, "capture", "notes.other")));

        Assert.Equal(("form.record_type.target_mismatch", "/record_type_id"), (refusal.Code, refusal.Target));
        Assert.Empty(reader.Asked);
    }

    [Fact(DisplayName = "K9: a declared form targeting its declared form_only type with a gate publishes, and the policy is read once")]
    public async Task DeclaredFormWithMatchingGatedTargetPublishes()
    {
        var reader = new Reader(RecordTypeCreationGate.FormOnly);
        var declarations = new Declarations(("capture", "notes.entry"));
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader, recordCreations: declarations);

        var published = await store.RegisterAndPublishAsync(Form(InspectorGate, "capture", "notes.entry"));

        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal("notes.entry", published.RecordTypeId);
        Assert.Equal([("tenant:acme", "notes.entry")], reader.Asked);
        Assert.Equal([("tenant:acme", "capture")], declarations.Asked);
    }

    [Fact(DisplayName = "D1 with K9: a declared form targeting its role-gated type publishes; the role target adds no creation requirement")]
    public async Task DeclaredRoleTargetPublishes()
    {
        var reader = new Reader(RecordTypeCreationGate.Role);
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader,
            recordCreations: new Declarations(("capture", "work.order")));
        var draft = Form(InspectorGate, "capture", "work.order");
        await store.RegisterAsync(draft);

        var published = await store.PublishAsync(draft.Tenant, draft.Id, draft.Version);

        // The generic T-756 rule asks every Published form for a submit gate; that gate is the form's own, not a second
        // creation gate: the record create stays the one role-gated Records decision.
        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal([("tenant:acme", "work.order")], reader.Asked);
    }

    [Fact(DisplayName = "K1: a standing submit gate on a declared form_only target publishes; no standing prohibition applies to Records")]
    public async Task StandingGateOnFormOnlyTargetPublishes()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly),
            recordCreations: new Declarations(("capture", "notes.entry")));

        var published = await store.RegisterAndPublishAsync(
            Form(new SubmitGate(Standing: new RecordStandingReference("author")), "capture", "notes.entry"));

        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal("author", published.SubmitGate!.Standing!.Name);
    }

    public static TheoryData<string> BrokenDeclarations => ["throws", "blank", "ambiguous"];

    [Theory(DisplayName = "K9: a failing, malformed or ambiguous declaration refuses publication at /record_type_id")]
    [MemberData(nameof(BrokenDeclarations))]
    public async Task BrokenDeclarationRefusesPublication(string shape)
    {
        IFormRecordCreationDeclarations declarations = shape switch
        {
            "throws" => new ThrowingDeclarations(),
            "blank" => new Declarations(("capture", " ")),
            _ => new Declarations(("capture", "notes.entry"), ("capture", "notes.other")),
        };
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly),
            recordCreations: declarations);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(InspectorGate, "capture", "notes.entry")));

        Assert.Equal(shape == "ambiguous" ? "form.record_type.target_ambiguous" : "form.record_type.policy_unavailable", refusal.Code);
        Assert.Equal("/record_type_id", refusal.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(async () =>
            await store.GetAsync(new TenantId("tenant:acme"), new FormDefinitionId("capture"), new SemanticVersion(1, 0, 0)));
    }

    [Fact(DisplayName = "K9: a declaration repeating one type is unambiguous")]
    public async Task RepeatedDeclarationIsOneType()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly),
            recordCreations: new Declarations(("capture", "notes.entry"), ("capture", "notes.entry")));

        var published = await store.RegisterAndPublishAsync(Form(InspectorGate, "capture", "notes.entry"));

        Assert.Equal("notes.entry", published.RecordTypeId);
    }

    [Fact(DisplayName = "K9: forms that genuinely create no Records publish as before, with or without a declarations port")]
    public async Task UndeclaredFormsAreUnaffected()
    {
        var declarations = new Declarations(("capture", "notes.entry"));
        var reader = new Reader(RecordTypeCreationGate.FormOnly);
        using var declared = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: reader, recordCreations: declarations);
        using var bare = new InMemoryFormDefinitionStore(new FixedClock(Now));

        var other = await declared.RegisterAndPublishAsync(Form(InspectorGate, "survey"));
        var plain = await bare.RegisterAndPublishAsync(Form(InspectorGate, "capture"));

        Assert.Null(other.RecordTypeId);
        Assert.Null(plain.RecordTypeId);
        Assert.Equal(FormDefinitionStatus.Published, other.Status);
        Assert.Equal(FormDefinitionStatus.Published, plain.Status);
        Assert.Equal([("tenant:acme", "survey")], declarations.Asked);
        Assert.Empty(reader.Asked);
    }

    [Fact(DisplayName = "K9: a Draft saves without asking the producers, and an ungated Published form is refused at /submit_gate first")]
    public async Task DraftAndGenericChecksPrecedeTheDeclaration()
    {
        var declarations = new Declarations(("capture", "notes.entry"));
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly),
            recordCreations: declarations);

        await store.RegisterAsync(Form(InspectorGate, "capture"));
        var ungated = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(null, "capture", version: new SemanticVersion(1, 1, 0))));

        Assert.Equal(("form.submit_gate.required", "/submit_gate"), (ungated.Code, ungated.Target));
        Assert.Empty(declarations.Asked);
    }

    [Fact(DisplayName = "K9: a legacy Published revision without a target stays readable, and its next revision must add the declared target")]
    public async Task LegacyRevisionStaysReadableAndMigrationAddsTheTarget()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly),
            recordCreations: new Declarations(("capture", "notes.entry")));
        var legacy = Form(InspectorGate, "capture") with { Status = FormDefinitionStatus.Published };
        store.SeedLegacyRevisionForTesting(legacy);

        Assert.Equal(legacy, await store.GetCurrentPublishedAsync(legacy.Tenant, legacy.Id));
        var unmigrated = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(InspectorGate, "capture", version: new SemanticVersion(1, 1, 0))));
        var migrated = await store.RegisterAndPublishAsync(Form(InspectorGate, "capture", "notes.entry", new SemanticVersion(1, 1, 0)));

        Assert.Equal("form.record_type.target_required", unmigrated.Code);
        Assert.Equal("notes.entry", migrated.RecordTypeId);
        Assert.Equal(new SemanticVersion(1, 1, 0), (await store.GetCurrentPublishedAsync(legacy.Tenant, legacy.Id))!.Version);
        Assert.Equal(legacy, await store.GetAsync(legacy.Tenant, legacy.Id, legacy.Version));
    }

    [Fact(DisplayName = "K9: the declaration check itself, independently of the store")]
    public void DeclarationCheckRefusesMissingAndOtherTargets()
    {
        FormDefinitionValidation.ValidateDeclaredCreationOrThrow(Form(InspectorGate, "capture"), null);
        FormDefinitionValidation.ValidateDeclaredCreationOrThrow(Form(InspectorGate, "capture", "notes.entry"), null);
        FormDefinitionValidation.ValidateDeclaredCreationOrThrow(Form(InspectorGate, "capture", "notes.entry"), "notes.entry");

        var missing = Assert.Throws<FormDefinitionValidationException>(() =>
            FormDefinitionValidation.ValidateDeclaredCreationOrThrow(Form(InspectorGate, "capture"), "notes.entry"));
        var other = Assert.Throws<FormDefinitionValidationException>(() =>
            FormDefinitionValidation.ValidateDeclaredCreationOrThrow(Form(InspectorGate, "capture", "Notes.Entry"), "notes.entry"));

        Assert.Equal(("form.record_type.target_required", "/record_type_id"), (missing.Code, missing.Target));
        Assert.Equal(("form.record_type.target_mismatch", "/record_type_id"), (other.Code, other.Target));
    }

    [Fact(DisplayName = "K9: the resolver returns null without a port and propagates caller cancellation")]
    public async Task ResolverHandlesAbsentPortAndCancellation()
    {
        Assert.Null(await RecordCreationDeclarations.ResolveAsync(null, new TenantId("tenant:acme"), new FormDefinitionId("capture")));
        Assert.Null(await RecordCreationDeclarations.ResolveAsync(new Declarations(), new TenantId("tenant:acme"), new FormDefinitionId("capture")));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await RecordCreationDeclarations.ResolveAsync(new CancellingDeclarations(), new TenantId("tenant:acme"), new FormDefinitionId("capture"), cancelled.Token));
    }

    [Fact(DisplayName = "K9 (F4): with no explicit port, the store uses its producer's own declaration, the form engine's sink rule")]
    public async Task ProducerDeclarationIsUsedWhenNoPortIsGiven()
    {
        var producer = new Declarations(("capture", "notes.entry"));
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new Reader(RecordTypeCreationGate.FormOnly),
            recordCreationProducer: producer);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(InspectorGate, "capture")));

        Assert.Equal(("form.record_type.target_required", "/record_type_id"), (refusal.Code, refusal.Target));
        Assert.Equal([("tenant:acme", "capture")], producer.Asked);
    }

    [Fact(DisplayName = "K9 (F4): an explicit port wins over the producer, and a producer that declares nothing is ignored")]
    public async Task ExplicitPortWinsAndNonDeclaringProducerIsIgnored()
    {
        var producer = new Declarations(("capture", "notes.entry"));
        var explicitPort = new Declarations();
        using var explicitStore = new InMemoryFormDefinitionStore(new FixedClock(Now), recordCreations: explicitPort, recordCreationProducer: producer);
        using var plainProducerStore = new InMemoryFormDefinitionStore(new FixedClock(Now), recordCreationProducer: new object());

        var withExplicit = await explicitStore.RegisterAndPublishAsync(Form(InspectorGate, "capture"));
        var withPlainProducer = await plainProducerStore.RegisterAndPublishAsync(Form(InspectorGate, "capture"));

        Assert.Equal(FormDefinitionStatus.Published, withExplicit.Status);
        Assert.Equal(FormDefinitionStatus.Published, withPlainProducer.Status);
        Assert.Empty(producer.Asked);
        Assert.Equal([("tenant:acme", "capture")], explicitPort.Asked);
    }

    [Fact(DisplayName = "K9 (F4): one selection rule for the store and the form engine")]
    public void SelectionRuleIsExplicitThenDeclaringProducer()
    {
        var explicitPort = new Declarations();
        var producer = new Declarations();

        Assert.Same(explicitPort, RecordCreationDeclarations.Select(explicitPort, producer));
        Assert.Same(producer, RecordCreationDeclarations.Select(null, producer));
        Assert.Null(RecordCreationDeclarations.Select(null, new object()));
        Assert.Null(RecordCreationDeclarations.Select(null, null));
    }

    private static FormDefinition Form(SubmitGate? gate, string id, string? target = null, SemanticVersion? version = null)
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId(id),
            Version: version ?? new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: new TenantId("tenant:acme"),
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:record-creation"),
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

    private sealed class Declarations(params (string FormId, string RecordTypeId)[] declared) : IFormRecordCreationDeclarations
    {
        public List<(string Tenant, string FormId)> Asked { get; } = [];

        public ValueTask<IReadOnlyCollection<string>> GetCreatedRecordTypesAsync(TenantId tenant, FormDefinitionId formId, CancellationToken cancellationToken = default)
        {
            Asked.Add((tenant.Value, formId.Value));
            return ValueTask.FromResult<IReadOnlyCollection<string>>(
                declared.Where(row => row.FormId == formId.Value).Select(row => row.RecordTypeId).ToArray());
        }
    }

    private sealed class ThrowingDeclarations : IFormRecordCreationDeclarations
    {
        public ValueTask<IReadOnlyCollection<string>> GetCreatedRecordTypesAsync(TenantId tenant, FormDefinitionId formId, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IReadOnlyCollection<string>>(new IOException("producer registry unavailable"));
    }

    private sealed class CancellingDeclarations : IFormRecordCreationDeclarations
    {
        public ValueTask<IReadOnlyCollection<string>> GetCreatedRecordTypesAsync(TenantId tenant, FormDefinitionId formId, CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<IReadOnlyCollection<string>>(cancellationToken);
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
