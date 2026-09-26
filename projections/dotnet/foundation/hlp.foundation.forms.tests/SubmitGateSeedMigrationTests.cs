using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
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
        await store.RegisterAsync(alpha);
        await store.RegisterAsync(beta);
        await store.RegisterAsync(unmapped);

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

    private static FormDefinition Form(string id)
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId(id),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Published,
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
