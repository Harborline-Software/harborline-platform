using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// T-485/definition-versioning: the store mints the next version on save, restores an old version as a new Draft,
/// and never changes the body of a revision that has left Draft (DES-0016 forms-ck-8, forms-ck-9, forms-ck-10;
/// ADR 0006). The expected versions are literals: the patch increment the retiring api copy's
/// <c>ReSave_Mints_NextVersion</c> and <c>Restore_Creates_New_Draft_Without_Mutating_History</c> pin, which T-538
/// aligns to.
/// </summary>
public sealed class FormDefinitionVersioningTests
{
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "forms-ck-8: saving an id that has revisions mints the highest version's next patch, not the caller's version")]
    public async Task SaveNext_mints_above_the_highest_existing_version()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published));
        await store.RegisterAsync(Form("1.2.3"));

        var saved = await store.SaveNextAsync(Form("9.9.9"));

        Assert.Equal(SemanticVersion.Parse("1.2.4"), saved.Version);
        Assert.Equal(saved, await store.GetAsync(Tenant, saved.Id, SemanticVersion.Parse("1.2.4")));
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(Tenant, saved.Id, SemanticVersion.Parse("9.9.9")));
    }

    [Fact(DisplayName = "forms-ck-8: a re-save after 1.0.0 mints 1.0.1, the retiring api copy's increment")]
    public async Task ReSave_mints_the_next_patch_version()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var first = await store.SaveNextAsync(Form("1.0.0"));
        var second = await store.SaveNextAsync(Form("1.0.0"));

        Assert.Equal(SemanticVersion.Parse("1.0.0"), first.Version);
        Assert.Equal(SemanticVersion.Parse("1.0.1"), second.Version);
    }

    [Fact(DisplayName = "forms-ck-8: the published head is read by semantic version, so 1.10.0 outranks 1.9.0")]
    public async Task Published_head_is_read_by_semantic_version()
    {
        foreach (var order in new[] { new[] { "1.9.0", "1.10.0" }, new[] { "1.10.0", "1.9.0" } })
        {
            using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
            foreach (var version in order) await store.RegisterAsync(Form(version, FormDefinitionStatus.Published));

            var head = await store.GetCurrentPublishedAsync(Tenant, Id);

            Assert.Equal(SemanticVersion.Parse("1.10.0"), head!.Version);
            Assert.Equal(SemanticVersion.Parse("1.10.1"), (await store.SaveNextAsync(Form("1.0.0"))).Version);
        }
    }

    [Fact(DisplayName = "forms-ck-9: restoring 1.0.0 registers 1.0.2 as a Draft derived from 1.0.0 and leaves history unchanged")]
    public async Task Restore_registers_a_new_draft_without_mutating_history()
    {
        var clock = new FixedClock(Now);
        using var store = new InMemoryFormDefinitionStore(clock);
        var v100 = await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published));
        var v101 = await store.RegisterAsync(Form("1.0.1", FormDefinitionStatus.Published, label: "Street name"));
        clock.Advance(TimeSpan.FromHours(1));

        var restored = await store.RestoreAsDraftAsync(Tenant, Id, v100.Version);

        Assert.Equal(SemanticVersion.Parse("1.0.2"), restored.Version);
        Assert.Equal(FormDefinitionStatus.Draft, restored.Status);
        Assert.Equal(new FormDefinitionLineage(Id, v100.Version), restored.Lineage);
        Assert.True(JsonNode.DeepEquals(Content(v100), Content(restored)));
        Assert.Equal(Now.AddHours(1), restored.CreatedAt);
        Assert.Equal(Now.AddHours(1), restored.UpdatedAt);
        Assert.Equal(3, (await Revisions(store)).Count);
        Assert.Equal(v100, await store.GetAsync(Tenant, Id, v100.Version));
        Assert.Equal(v101, await store.GetAsync(Tenant, Id, v101.Version));
        Assert.Equal(v101, await store.GetCurrentPublishedAsync(Tenant, Id));
    }

    [Fact(DisplayName = "forms-ck-9: restoring a version the tenant does not hold is refused and registers nothing")]
    public async Task Restore_of_an_unknown_version_is_refused()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published));

        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.RestoreAsDraftAsync(Tenant, Id, SemanticVersion.Parse("2.0.0")));
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.RestoreAsDraftAsync(new TenantId("tenant:zenith"), Id, SemanticVersion.Parse("1.0.0")));
        Assert.Single(await Revisions(store));
    }

    [Fact(DisplayName = "forms-ck-9: a restored Draft runs the Draft submit-gate admission and registers nothing when refused")]
    public async Task Restore_runs_the_draft_submit_gate_admission()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        store.SeedLegacyRevisionForTesting(Form("1.0.0", FormDefinitionStatus.Published) with
        {
            SubmitGate = new SubmitGate(Capability: new AuthorizationCapabilityReference("access:undeclared")),
        });

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RestoreAsDraftAsync(Tenant, Id, SemanticVersion.Parse("1.0.0")));

        Assert.Equal(FormDefinitionCodes.SubmitGateCapabilityUnknown, refusal.Code);
        Assert.Single(await Revisions(store));
    }

    [Fact(DisplayName = "forms-ck-9: a restored Draft runs the Draft record-type target admission and registers nothing when refused")]
    public async Task Restore_runs_the_draft_record_type_admission()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        store.SeedLegacyRevisionForTesting(Form("1.0.0", FormDefinitionStatus.Published) with { RecordTypeId = " " });

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RestoreAsDraftAsync(Tenant, Id, SemanticVersion.Parse("1.0.0")));

        Assert.Equal(FormDefinitionCodes.RecordTypeRequired, refusal.Code);
        Assert.Single(await Revisions(store));
    }

    [Fact(DisplayName = "forms-ck-10: no store operation changes the body of a Published, Deprecated or Withdrawn revision")]
    public async Task No_mutating_operation_changes_a_released_body()
    {
        var clock = new FixedClock(Now);
        using var store = new InMemoryFormDefinitionStore(clock);
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published));
        await store.RegisterAsync(Form("1.0.1", FormDefinitionStatus.Published, label: "Street name"));
        await store.RegisterAsync(Form("1.0.2", FormDefinitionStatus.Published, label: "Road"));
        await store.DeprecateAsync(Tenant, Id, SemanticVersion.Parse("1.0.1"));
        await store.WithdrawAsync(Tenant, Id, SemanticVersion.Parse("1.0.2"));

        var pack = new FormDefinitionId("platform.pack.form");
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published, id: pack.Value, owner: IdentityRef.System));
        await store.WithdrawAsync(Tenant, pack, SemanticVersion.Parse("1.0.0"));

        // Each operation names the one revision it may move (status and UpdatedAt only); every other revision must
        // come back byte for byte.
        var operations = new (string Name, (FormDefinitionId, SemanticVersion)? Target, Func<Task> Run)[]
        {
            ("RegisterAsync", null, async () => await store.RegisterAsync(Form("2.0.0", label: "Avenue"))),
            ("RegisterAndPublishAsync", null, async () => await store.RegisterAndPublishAsync(Form("3.0.0", label: "Lane"))),
            ("CreateAsync", null, async () => await store.CreateAsync(Form("1.0.0", id: "other.form"))),
            ("SaveNextAsync", null, async () => await store.SaveNextAsync(Form("1.0.0", label: "Close"))),
            ("RestoreAsDraftAsync", null, async () => await store.RestoreAsDraftAsync(Tenant, Id, SemanticVersion.Parse("1.0.1"))),
            ("PublishAsync", (Id, SemanticVersion.Parse("2.0.0")), async () => await store.PublishAsync(Tenant, Id, SemanticVersion.Parse("2.0.0"))),
            ("DeprecateAsync", (Id, SemanticVersion.Parse("1.0.0")), async () => await store.DeprecateAsync(Tenant, Id, SemanticVersion.Parse("1.0.0"))),
            ("WithdrawAsync", (Id, SemanticVersion.Parse("3.0.0")), async () => await store.WithdrawAsync(Tenant, Id, SemanticVersion.Parse("3.0.0"))),
            ("RestorePackProjectionAsync", (pack, SemanticVersion.Parse("1.0.0")), async () => await store.RestorePackProjectionAsync(Tenant, pack, SemanticVersion.Parse("1.0.0"))),
        };

        foreach (var (name, target, run) in operations)
        {
            var before = (await Revisions(store)).ToDictionary(revision => (revision.Id, revision.Version));
            clock.Advance(TimeSpan.FromMinutes(1));

            await run();

            var after = (await Revisions(store)).ToDictionary(revision => (revision.Id, revision.Version));
            foreach (var (key, revision) in before)
            {
                Assert.True(after.TryGetValue(key, out var now), $"{name} removed {key.Id} v{key.Version}");
                if (key == target)
                {
                    if (revision.Status != FormDefinitionStatus.Draft)
                        Assert.True(JsonNode.DeepEquals(Body(revision), Body(now!)), $"{name} changed the body of {key.Id} v{key.Version}");
                }
                else
                {
                    Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(revision), JsonSerializer.SerializeToNode(now)),
                        $"{name} changed {key.Id} v{key.Version}, which it does not target");
                }
            }
        }
    }

    [Fact(DisplayName = "forms-ck-8: a Published save runs the publication submit-gate check and registers nothing when refused")]
    public async Task Published_save_runs_publication_checks()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.SaveNextAsync(Form("1.0.0", FormDefinitionStatus.Published) with { SubmitGate = null }));

        Assert.Equal(FormDefinitionCodes.SubmitGateRequired, refusal.Code);
        Assert.Single(await Revisions(store));
    }

    [Fact(DisplayName = "forms-ck-8: a Published save runs record-type continuity against the revisions it mints above")]
    public async Task Published_save_runs_record_type_continuity()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new RoleGatedReader());
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published) with { RecordTypeId = "notes.entry" });

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.SaveNextAsync(Form("1.0.0", FormDefinitionStatus.Published) with { RecordTypeId = "work.order" }));

        Assert.Equal(FormDefinitionCodes.RecordTypeChanged, refusal.Code);
        Assert.Single(await Revisions(store));
    }

    [Fact(DisplayName = "forms-ck-8: concurrent saves of one id mint distinct versions")]
    public async Task Concurrent_saves_mint_distinct_versions()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await store.RegisterAsync(Form("1.0.0"));

        var saved = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => store.SaveNextAsync(Form("1.0.0")).AsTask()));

        Assert.Equal(24, saved.Select(revision => revision.Version).Distinct().Count());
        Assert.Equal(SemanticVersion.Parse("1.0.24"), saved.Max(revision => revision.Version));
    }

    [Fact(DisplayName = "forms-ck-9: a System-owned pack projection is not restored as a tenant Draft")]
    public async Task Restore_refuses_a_system_owned_projection()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published, owner: IdentityRef.System));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.RestoreAsDraftAsync(Tenant, Id, SemanticVersion.Parse("1.0.0")));
        Assert.Equal(
            "'property.intake' v1.0.0 is a System-owned pack projection; restore it through RestorePackProjectionAsync, not as a tenant Draft.",
            refusal.Message);
        Assert.Single(await Revisions(store));
    }

    [Fact(DisplayName = "forms-ck-9: a restored Draft's lineage names the restored revision, replacing any extension lineage it carried")]
    public async Task Restore_lineage_names_the_restored_revision()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var parent = await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published, id: "property.base"));
        await store.RegisterAsync(Form("1.0.0", FormDefinitionStatus.Published) with
        {
            Lineage = new FormDefinitionLineage(parent.Id, parent.Version),
        });

        var restored = await store.RestoreAsDraftAsync(Tenant, Id, SemanticVersion.Parse("1.0.0"));

        Assert.Equal(new FormDefinitionLineage(Id, SemanticVersion.Parse("1.0.0")), restored.Lineage);
    }

    [Fact(DisplayName = "forms-ck-8: a save refuses a null definition, lineage to an unregistered parent, and a patch overflow")]
    public async Task Save_refuses_malformed_input()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SaveNextAsync(null!));

        await store.RegisterAsync(Form("1.0.0"));
        await Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.SaveNextAsync(Form("1.0.0") with
        {
            Lineage = new FormDefinitionLineage(new FormDefinitionId("absent.parent"), SemanticVersion.Parse("1.0.0")),
        }));

        await store.RegisterAsync(Form("1.0.0") with { Version = new SemanticVersion(1, 0, int.MaxValue) });
        await Assert.ThrowsAsync<OverflowException>(async () => await store.SaveNextAsync(Form("1.0.0")));
        Assert.Equal(2, (await Revisions(store)).Count);
    }

    [Fact(DisplayName = "forms-ck-8, forms-ck-9: a store that does not persist revisions refuses to mint or restore, naming itself")]
    public async Task Non_persisting_store_refuses_versioning_operations()
    {
        IFormDefinitionStore store = new ReadOnlyStore();

        var save = await Assert.ThrowsAsync<NotSupportedException>(async () => await store.SaveNextAsync(Form("1.0.0")));
        var restore = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await store.RestoreAsDraftAsync(Tenant, Id, SemanticVersion.Parse("1.0.0")));

        Assert.Equal("ReadOnlyStore does not mint form definition versions.", save.Message);
        Assert.Equal("ReadOnlyStore does not restore form definition revisions.", restore.Message);
    }

    private static readonly FormDefinitionId Id = new("property.intake");
    private static readonly IdentityRef Author = new("party", "author-1");

    // A revision's body: everything except its lifecycle status and the timestamp a transition stamps.
    private static JsonNode Body(FormDefinition revision)
    {
        var node = JsonSerializer.SerializeToNode(revision)!.AsObject();
        node.Remove(nameof(FormDefinition.Status));
        node.Remove(nameof(FormDefinition.UpdatedAt));
        return node;
    }

    // What a restore copies: the body without the identity a new revision gets (version, lineage, creation time).
    private static JsonNode Content(FormDefinition revision)
    {
        var node = Body(revision).AsObject();
        node.Remove(nameof(FormDefinition.Version));
        node.Remove(nameof(FormDefinition.Lineage));
        node.Remove(nameof(FormDefinition.CreatedAt));
        return node;
    }

    private static async Task<List<FormDefinition>> Revisions(InMemoryFormDefinitionStore store)
    {
        var all = new List<FormDefinition>();
        await foreach (var revision in store.ListByTenantAsync(Tenant)) all.Add(revision);
        return all;
    }

    private static FormDefinition Form(
        string version,
        FormDefinitionStatus status = FormDefinitionStatus.Draft,
        string label = "Street",
        string? id = null,
        IdentityRef? owner = null)
        => new(
            Id: id is null ? Id : new FormDefinitionId(id),
            Version: SemanticVersion.Parse(version),
            Status: status,
            Tenant: Tenant,
            Owner: owner ?? Author,
            SchemaRef: new SchemaId("sha256:test-property-intake"),
            Overlay: new HarborlineOverlay(
                Fields: new Dictionary<string, FieldOverlay> { ["street"] = new(Label: InternationalizedText.FromInvariant(label)) },
                Sections: new[]
                {
                    new FormSection(
                        "address",
                        InternationalizedText.FromInvariant("Address"),
                        Fields: new[] { "street" },
                        Access: new SectionAccess(
                            ReadRoles: new[] { RoleReference.Domain("*") },
                            WriteRoles: new[] { RoleReference.Domain("tenant:admin") })),
                },
                Rules: Array.Empty<RuleDefinition>()),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now,
            SubmitGate: new SubmitGate(Role: RoleReference.Domain("tenant:admin")));

    // Implements only the members every store must; inherits the two versioning members' refusals.
    private sealed class ReadOnlyStore : IFormDefinitionStore
    {
        public ValueTask<FormDefinition> GetAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition?> GetCurrentPublishedAsync(TenantId tenant, FormDefinitionId id, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition> RegisterAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition> RegisterAndPublishAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition> CreateAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition> PublishAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition> DeprecateAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition> WithdrawAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<FormDefinition> RestorePackProjectionAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<FormDefinition> ListByTenantAsync(TenantId tenant, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<FormDefinition> ListCurrentPublishedByTenantAsync(TenantId tenant, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class RoleGatedReader : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<RecordTypeCreationGate?>(RecordTypeCreationGate.Role);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
