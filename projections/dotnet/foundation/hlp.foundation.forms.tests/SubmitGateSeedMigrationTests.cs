using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

public sealed class SubmitGateSeedMigrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = new("tenant:acme");

    [Fact(DisplayName = "forms-ck-4: the submit-gate seed migration republishes explicitly mapped legacy forms once and reports unmapped forms")]
    public async Task RepublishesLegacyFormsOnceAndReportsFormsWithoutReplacementGates()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var alpha = Form("alpha");
        var beta = Form("beta");
        var unmapped = Form("unmapped");
        // Simulates rows persisted before submit_gate became required at publish (T-756); today's
        // RegisterAsync/CreateAsync refuse an ungated Published definition outright.
        store.SeedLegacyRevisionForTesting(alpha);
        store.SeedLegacyRevisionForTesting(beta);
        store.SeedLegacyRevisionForTesting(unmapped);

        var migration = new SubmitGateSeedMigration(store);
        var result = await migration.ApplyAsync(
            Tenant,
            new Dictionary<FormDefinitionId, SubmitGate>
            {
                [alpha.Id] = new SubmitGate(Role: RoleReference.Domain("inspector")),
                [beta.Id] = new SubmitGate(Standing: new RecordStandingReference("author")),
            });

        Assert.Equal([alpha.Id, beta.Id], result.Published.Select(definition => definition.Id));
        Assert.Equal([unmapped.Id], result.Skipped.Select(definition => definition.Id));

        var originalAlpha = await store.GetAsync(Tenant, alpha.Id, alpha.Version);
        var migratedAlpha = await store.GetCurrentPublishedAsync(Tenant, alpha.Id);
        var migratedBeta = await store.GetCurrentPublishedAsync(Tenant, beta.Id);
        Assert.Equal(alpha, originalAlpha);
        Assert.Null(originalAlpha.SubmitGate);
        Assert.NotNull(migratedAlpha);
        Assert.NotNull(migratedBeta);
        Assert.Equal(new SemanticVersion(1, 0, 1), migratedAlpha!.Version);
        Assert.Equal(new SemanticVersion(1, 0, 1), migratedBeta!.Version);
        Assert.Equal(new SubmitGate(Role: RoleReference.Domain("inspector")), migratedAlpha.SubmitGate);
        Assert.Equal(new SubmitGate(Standing: new RecordStandingReference("author")), migratedBeta.SubmitGate);

        var afterFirstRun = (await AllAsync(store)).ToArray();
        var rerun = await migration.ApplyAsync(
            Tenant,
            new Dictionary<FormDefinitionId, SubmitGate>
            {
                [alpha.Id] = new SubmitGate(Role: RoleReference.Domain("inspector")),
                [beta.Id] = new SubmitGate(Standing: new RecordStandingReference("author")),
            });
        var afterSecondRun = (await AllAsync(store)).ToArray();

        Assert.Empty(rerun.Published);
        Assert.Equal([unmapped.Id], rerun.Skipped.Select(definition => definition.Id));
        Assert.Equal(afterFirstRun, afterSecondRun);
        Assert.Empty(result.DeferredPendingDraft);
        Assert.Empty(result.WithdrawnPendingGate);
    }

    [Fact(DisplayName = "forms-ck-4: the migration defers a form whose pending Draft outranks the ungated Published revision, instead of publishing legacy content above it")]
    public async Task DefersFormWithHigherPendingDraft()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var published = Form("gamma");
        store.SeedLegacyRevisionForTesting(published);
        var pendingDraft = Form("gamma", version: new SemanticVersion(2, 0, 0), status: FormDefinitionStatus.Draft);
        store.SeedLegacyRevisionForTesting(pendingDraft);

        var migration = new SubmitGateSeedMigration(store);
        var result = await migration.ApplyAsync(
            Tenant,
            new Dictionary<FormDefinitionId, SubmitGate> { [published.Id] = new SubmitGate(Role: RoleReference.Domain("inspector")) });

        Assert.Empty(result.Published);
        Assert.Empty(result.Skipped);
        Assert.Equal([published.Id], result.DeferredPendingDraft.Select(definition => definition.Id));

        var stillCurrent = await store.GetCurrentPublishedAsync(Tenant, published.Id);
        Assert.Equal(published, stillCurrent);
        var draftUnchanged = await store.GetAsync(Tenant, pendingDraft.Id, pendingDraft.Version);
        Assert.Equal(pendingDraft, draftUnchanged);
    }

    [Fact(DisplayName = "forms-ck-4: the migration reports a System-owned Withdrawn revision with no gate so an operator can repair and restore it")]
    public async Task ReportsWithdrawnSystemOwnedRevisionWithNoGate()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var withdrawn = Form("delta", status: FormDefinitionStatus.Withdrawn);
        store.SeedLegacyRevisionForTesting(withdrawn);

        var migration = new SubmitGateSeedMigration(store);
        var result = await migration.ApplyAsync(Tenant, new Dictionary<FormDefinitionId, SubmitGate>());

        Assert.Equal([withdrawn.Id], result.WithdrawnPendingGate.Select(definition => definition.Id));
        // The migration never republishes a Withdrawn revision on the operator's behalf.
        Assert.Null(await store.GetCurrentPublishedAsync(Tenant, withdrawn.Id));
    }

    // T-485/standing-gate-on-record-creation: the migration republishes through RegisterAndPublishAsync, so a standing
    // replacement gate on a form that targets a record type is refused there too. Oracle: the slice record's literals.
    [Fact(DisplayName = "T-485 standing gate: the seed migration refuses a standing replacement gate on a targeted legacy form and leaves the legacy revision current")]
    public async Task MigrationRefusesAStandingGateOnATargetedForm()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new FormOnlyReader());
        var legacy = Form("targeted") with { RecordTypeId = "notes.entry" };
        store.SeedLegacyRevisionForTesting(legacy);

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () =>
            await new SubmitGateSeedMigration(store).ApplyAsync(Tenant, new Dictionary<FormDefinitionId, SubmitGate>
            {
                [legacy.Id] = new SubmitGate(Standing: new RecordStandingReference("author")),
            }));

        Assert.Equal(("form.submit_gate.standing_on_record_creation", "/submit_gate/standing"), (refusal.Code, refusal.Target));
        Assert.Equal(legacy, await store.GetCurrentPublishedAsync(Tenant, legacy.Id));
        Assert.Equal([legacy], await AllAsync(store));
    }

    private sealed class FormOnlyReader : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RecordTypeCreationGate?>(RecordTypeCreationGate.FormOnly);
    }

    private static async ValueTask<IReadOnlyList<FormDefinition>> AllAsync(IFormDefinitionStore store)
    {
        var definitions = new List<FormDefinition>();
        await foreach (var definition in store.ListByTenantAsync(Tenant))
        {
            definitions.Add(definition);
        }

        return definitions;
    }

    private static FormDefinition Form(string id, SemanticVersion? version = null, FormDefinitionStatus status = FormDefinitionStatus.Published)
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId(id),
            Version: version ?? new SemanticVersion(1, 0, 0),
            Status: status,
            Tenant: Tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId($"sha256:{id}"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: [new FormSection("sec", InternationalizedText.FromInvariant("sec"), ["name"], access)],
                Rules: []),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
