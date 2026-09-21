using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class RecordsDefinitionLifecycleTests
{
    private static readonly DefinitionPrincipalContext Principal = new("actor-a");

    [Fact]
    public async Task Complete_definition_round_trips_and_head_and_exact_pin_bind_real_runtime()
    {
        var harness = Harness.Create();
        var one = CompleteDefinition("1.0.0");
        var oneDraft = await harness.Lifecycle.CreateDraftAsync(
            Principal, one, "version-one", 0, "create-one");
        using (var body = JsonDocument.Parse(oneDraft.Source.BodyJson))
        {
            var envelope = body.RootElement.GetProperty("envelope");
            Assert.False(envelope.TryGetProperty("definition_id", out _));
            Assert.False(envelope.TryGetProperty("tenant_id", out _));
            Assert.False(envelope.TryGetProperty("version", out _));
            Assert.Equal("package-a", envelope.GetProperty("package_id").GetString());
        }
        await harness.Lifecycle.PublishAsync(
            Principal, "tenant-a", one.Envelope.DefinitionId, "version-one", oneDraft.Revision, "publish-one");

        var two = CompleteDefinition("2.0.0") with { Name = "Asset v2" };
        var twoDraft = await harness.Lifecycle.SaveDraftAsync(
            Principal, two, "version-two", 2, "save-two");
        await harness.Lifecycle.PublishAsync(
            Principal, "tenant-a", two.Envelope.DefinitionId, "version-two", twoDraft.Revision, "publish-two");

        var head = await harness.Binder.BindPublishedHeadAsync(
            Principal, "tenant-a", one.Envelope.DefinitionId);
        var exact = await harness.Binder.BindPublishedAsync(
            Principal, "tenant-a", one.Envelope.DefinitionId, "version-one");

        Assert.Equal("2.0.0", head.Definition.Envelope.Version);
        Assert.Equal("1.0.0", exact.Definition.Envelope.Version);
        Assert.Equal(
            RecordsDefinitionJson.SerializeCanonical(one),
            RecordsDefinitionJson.SerializeCanonical(exact.Definition));
        Assert.Equal("text", Assert.Single(exact.FieldKinds).Kind.KindId);
        Assert.Equal(one.Policies, exact.Policies);
        var validation = await harness.Registry.ValidateAsync(
            exact.Schema.Id, Encoding.UTF8.GetBytes("{\"asset_code\":\"A-1\"}"));
        Assert.True(validation.IsValid);

        var crossTenant = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Binder
            .BindPublishedAsync(Principal, "tenant-b", one.Envelope.DefinitionId, "version-one").AsTask());
        Assert.Contains(crossTenant.Refusals, refusal => refusal.Code == "records.binding.published_required");
    }

    [Fact]
    public async Task Creation_materializes_defaults_once_and_restore_preserves_edits_source_and_digest()
    {
        var harness = Harness.Create();
        var created = CompleteDefinition("1.0.0") with
        {
            Fields = [Field("Asset code", "asset_code") with { Governance = null, KindDefaultProvenance = null }],
        };

        var first = await harness.Lifecycle.CreateDraftAsync(
            Principal, created, "version-one", 0, "create");
        var firstField = Assert.Single(first.Definition.Fields);
        Assert.Equal(new FieldGovernanceDefinition(true, true, false, "personal"), firstField.Governance);
        Assert.Equal(new FieldKindDefaultProvenance("text", "1.0.0"), firstField.KindDefaultProvenance);

        var edited = first.Definition with
        {
            Fields = [firstField with
            {
                Kind = new FieldKindReference("text", "2.0.0", new Dictionary<string, string>()),
                Governance = new FieldGovernanceDefinition(false, false, true, "internal"),
            }],
        };
        var saved = await harness.Lifecycle.SaveDraftAsync(
            Principal, edited, "version-one", first.Revision, "edit");
        var savedField = Assert.Single(saved.Definition.Fields);
        Assert.Equal(new FieldGovernanceDefinition(false, false, true, "internal"), savedField.Governance);
        Assert.Equal(new FieldKindDefaultProvenance("text", "1.0.0"), savedField.KindDefaultProvenance);

        var published = await harness.Lifecycle.PublishAsync(
            Principal, "tenant-a", created.Envelope.DefinitionId, "version-one", saved.Revision, "publish");
        var restored = await harness.Lifecycle.RestoreAsDraftAsync(
            Principal, "tenant-a", created.Envelope.DefinitionId, "version-one", "version-restored", "1.1.0",
            published.Revision, "restore");

        Assert.Equal("version-one", restored.RestoredFromVersionId);
        Assert.Equal("1.1.0", restored.Definition.Envelope.Version);
        Assert.Equal(published.Source.BodyJson, restored.Source.BodyJson);
        Assert.Equal(published.Digest, restored.Digest);
        Assert.Equal(savedField.Governance, Assert.Single(restored.Definition.Fields).Governance);
        var head = await harness.Lifecycle.GetPublishedHeadAsync("tenant-a", created.Envelope.DefinitionId);
        Assert.Equal("version-one", head!.Source.VersionId);
        Assert.Equal(published.Source.BodyJson, head.Source.BodyJson);
    }

    [Fact]
    public async Task Direct_store_replacement_refuses_changed_record_type_identity_without_mutation()
    {
        var harness = Harness.Create();
        var original = RecordsDefinitionCodec.Encode(CompleteDefinition("1.0.0"), "version-one");
        await harness.Store.SaveDraftAsync(Principal, original, 0, "save-original");
        var changed = RecordsDefinitionCodec.Encode(
            CompleteDefinition("1.0.0") with { RecordTypeId = "records.other" }, "version-one");

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Store
            .SaveDraftAsync(Principal, changed, 1, "save-changed").AsTask());

        Assert.Contains(error.Refusals, refusal => refusal is
            { Code: "records.definition.record_type_id_immutable", Pointer: "/record_type_id" });
        Assert.Single(await harness.Store.ListHistoryAsync(original.Key));
        Assert.Null(await harness.Store.GetPublishedHeadAsync(original.Key));
    }

    [Fact]
    public async Task Authoritative_member_alias_and_missing_principal_never_enter_history()
    {
        var harness = Harness.Create();
        var valid = RecordsDefinitionCodec.Encode(CompleteDefinition("1.0.0"), "version-one");
        var body = JsonNode.Parse(valid.BodyJson)!.AsObject();
        body["envelope"]!.AsObject()["VERSION"] = "9.9.9";
        var malformed = valid with { BodyJson = body.ToJsonString() };

        var malformedError = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Store
            .SaveDraftAsync(Principal, malformed, 0, "malformed").AsTask());
        Assert.Contains(malformedError.Refusals, refusal => refusal is
            { Code: "records.source.authoritative_member_forbidden", Pointer: "/body/envelope/VERSION" });

        var installError = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Admission
            .AdmitInstallAsync(malformed, Principal, CancellationToken.None).AsTask());
        Assert.Contains(installError.Refusals, refusal => refusal is
            { Code: "records.source.authoritative_member_forbidden", Pointer: "/body/envelope/VERSION" });

        var principalError = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Store
            .SaveDraftAsync(valid, 0, "legacy-bypass").AsTask());
        Assert.Contains(principalError.Refusals, refusal => refusal.Code == "definition.principal_required");
        Assert.Empty(await harness.Store.ListHistoryAsync(valid.Key));
    }

    [Fact]
    public async Task Nested_recognized_member_duplicates_differing_only_by_case_refuse_direct_store_admission()
    {
        var harness = Harness.Create();
        var valid = RecordsDefinitionCodec.Encode(CompleteDefinition("1.0.0"), "version-one");
        var duplicate = valid with
        {
            BodyJson = valid.BodyJson.Replace(
                "\"key\":\"asset_code\"",
                "\"key\":\"asset_code\",\"KEY\":\"other\"",
                StringComparison.Ordinal),
        };

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Store
            .SaveDraftAsync(Principal, duplicate, 0, "duplicate-member").AsTask());

        Assert.Contains(error.Refusals, refusal => refusal is
            { Code: "records.source.member_duplicate", Pointer: "/body/fields/0/KEY" });
        Assert.Empty(await harness.Store.ListHistoryAsync(valid.Key));
    }

    [Fact]
    public async Task Save_draft_preserves_null_fields_until_shared_shape_admission_refuses_without_source_io()
    {
        var harness = Harness.Create();
        var invalid = CompleteDefinition("1.0.0") with
        {
            Fields = null!,
            UniqueConstraints = [],
        };
        var shared = await new RecordsIntentValidator(harness.Domains, harness.Kinds)
            .ValidateAsync(
                invalid,
                new FieldDomainScope(new TenantId("tenant-a"), Principal.Principal),
                CancellationToken.None);
        var expected = Assert.Single(shared.Refusals, refusal => refusal is
            { Code: "records.definition.null_forbidden", JsonPointer: "/fields" });

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Lifecycle
            .SaveDraftAsync(Principal, invalid, "version-one", 0, "null-fields").AsTask());

        Assert.Contains(error.Refusals, refusal =>
            refusal.Code == expected.Code && refusal.Pointer == expected.JsonPointer);
        Assert.Equal(0, harness.Domain.Opens);
        Assert.Empty(await harness.Lifecycle.ListHistoryAsync("tenant-a", invalid.Envelope.DefinitionId));
    }

    [Fact]
    public async Task Raw_numeric_enum_refuses_before_decode_normalization_or_source_io()
    {
        var harness = Harness.Create();
        var valid = RecordsDefinitionCodec.Encode(CompleteDefinition("1.0.0"), "version-one");
        var numericEnum = valid with
        {
            BodyJson = valid.BodyJson.Replace(
                "\"creation_gate\":\"form_only\"",
                "\"creation_gate\":0",
                StringComparison.Ordinal),
        };

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Store
            .SaveDraftAsync(Principal, numericEnum, 0, "numeric-enum").AsTask());

        Assert.Contains(error.Refusals, refusal => refusal is
            { Code: "records.definition.enum_invalid", Pointer: "/creation_gate" });
        Assert.Equal(0, harness.Domain.Opens);
        Assert.Empty(await harness.Store.ListHistoryAsync(valid.Key));
    }

    [Fact]
    public async Task Nested_null_collection_refuses_with_the_shared_shape_pointer()
    {
        var harness = Harness.Create();
        var definition = CompleteDefinition("1.0.0");
        var field = Assert.Single(definition.Fields);
        var invalid = definition with
        {
            Fields =
            [
                field with
                {
                    Constraints = field.Constraints! with { ReadRoleIds = null! },
                },
            ],
        };

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Lifecycle
            .SaveDraftAsync(Principal, invalid, "version-one", 0, "null-read-roles").AsTask());

        Assert.Contains(error.Refusals, refusal => refusal is
        {
            Code: "records.definition.null_forbidden",
            Pointer: "/fields/0/constraints/read_role_ids",
        });
        Assert.Equal(0, harness.Domain.Opens);
        Assert.Empty(await harness.Lifecycle.ListHistoryAsync("tenant-a", invalid.Envelope.DefinitionId));
    }

    [Fact]
    public async Task Grammar_declared_nullable_members_remain_valid_through_lifecycle_admission()
    {
        var harness = Harness.Create();
        var definition = CompleteDefinition("1.0.0");
        var field = Assert.Single(definition.Fields);
        var nullable = definition with
        {
            Policies = null,
            PermissionFloor = null,
            Fields =
            [
                field with
                {
                    Governance = null,
                    KindDefaultProvenance = null,
                    Reference = null,
                },
            ],
        };

        var saved = await harness.Lifecycle.SaveDraftAsync(
            Principal, nullable, "version-one", 0, "valid-nulls");

        Assert.Null(saved.Definition.Policies);
        Assert.Null(saved.Definition.PermissionFloor);
        Assert.Null(Assert.Single(saved.Definition.Fields).Governance);
        Assert.Single(await harness.Lifecycle.ListHistoryAsync("tenant-a", nullable.Envelope.DefinitionId));
    }

    [Fact]
    public async Task Duplicate_source_member_pointer_escapes_slash_and_tilde()
    {
        var harness = Harness.Create();
        var valid = RecordsDefinitionCodec.Encode(CompleteDefinition("1.0.0"), "version-one");
        var duplicate = valid with
        {
            BodyJson = valid.BodyJson.Replace(
                "\"package_id\":\"package-a\"",
                "\"package_id\":\"package-a\",\"x/y~z\":1,\"x/y~z\":2",
                StringComparison.Ordinal),
        };

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Store
            .SaveDraftAsync(Principal, duplicate, 0, "escaped-duplicate").AsTask());

        Assert.Contains(error.Refusals, refusal => refusal is
            { Code: "records.source.member_duplicate", Pointer: "/body/envelope/x~1y~0z" });
        Assert.Empty(await harness.Store.ListHistoryAsync(valid.Key));
    }

    [Fact]
    public async Task Exact_replay_is_stable_and_changed_replay_or_stale_fence_refuses()
    {
        var harness = Harness.Create();
        var definition = CompleteDefinition("1.0.0");
        var first = await harness.Lifecycle.SaveDraftAsync(
            Principal, definition, "version-one", 0, "request-one");
        var sourceReads = harness.Domain.Opens;

        var replay = await harness.Lifecycle.SaveDraftAsync(
            Principal, definition, "version-one", 0, "request-one");
        AssertRevisionEqual(first, replay);
        Assert.Equal(sourceReads, harness.Domain.Opens);

        var replayError = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Lifecycle
            .SaveDraftAsync(Principal, definition with { Name = "Changed" }, "version-one", 0, "request-one")
            .AsTask());
        Assert.Contains(replayError.Refusals, refusal => refusal.Code == "definition.replay_conflict");

        var staleError = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Lifecycle
            .SaveDraftAsync(Principal, definition, "version-two", 0, "stale").AsTask());
        Assert.Contains(staleError.Refusals, refusal => refusal.Code == "definition.revision_conflict");
        Assert.Single(await harness.Lifecycle.ListHistoryAsync("tenant-a", definition.Envelope.DefinitionId));
    }

    [Fact]
    public async Task Faulted_or_cancelled_domain_source_and_install_admission_do_not_mutate_catalogue()
    {
        var faulted = Harness.Create();
        faulted.Domain.OpenFault = new InvalidOperationException("source fault");
        await Assert.ThrowsAsync<InvalidOperationException>(() => faulted.Lifecycle
            .SaveDraftAsync(Principal, CompleteDefinition("1.0.0"), "version-one", 0, "faulted").AsTask());
        Assert.Empty(await faulted.Lifecycle.ListHistoryAsync("tenant-a", "definition.asset"));

        var cancelled = Harness.Create();
        cancelled.Domain.BlockUntilCancelled = true;
        using var cancellation = new CancellationTokenSource();
        var pending = cancelled.Lifecycle.SaveDraftAsync(
            Principal, CompleteDefinition("1.0.0"), "version-one", 0, "cancelled", cancellation.Token).AsTask();
        await cancelled.Domain.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(await cancelled.Lifecycle.ListHistoryAsync("tenant-a", "definition.asset"));

        var install = Harness.Create();
        var document = RecordsDefinitionCodec.Encode(CompleteDefinition("1.0.0"), "version-one");
        var admitted = await install.Admission.AdmitInstallAsync(document, Principal, CancellationToken.None);
        Assert.Equal("records.asset", admitted.RecordTypeId);
        install.Domain.OpenFault = new InvalidOperationException("install source fault");
        await Assert.ThrowsAsync<InvalidOperationException>(() => install.Admission
            .AdmitInstallAsync(document, Principal, CancellationToken.None).AsTask());
        Assert.Empty(await install.Store.ListHistoryAsync(document.Key));
    }

    [Fact]
    public async Task Registry_failure_returns_no_binding_and_preserves_published_catalogue()
    {
        var harness = Harness.Create();
        var definition = CompleteDefinition("1.0.0");
        var draft = await harness.Lifecycle.SaveDraftAsync(
            Principal, definition, "version-one", 0, "save");
        var published = await harness.Lifecycle.PublishAsync(
            Principal, "tenant-a", definition.Envelope.DefinitionId, "version-one", draft.Revision, "publish");
        var historyBefore = await harness.Lifecycle.ListHistoryAsync("tenant-a", definition.Envelope.DefinitionId);
        var failingBinder = new RecordsPublishedDefinitionBinder(
            harness.Store,
            new RecordsDefinitionCompiler(new FailingSchemaRegistry(), harness.Domains, harness.Kinds));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingBinder
            .BindPublishedHeadAsync(Principal, "tenant-a", definition.Envelope.DefinitionId).AsTask());

        var historyAfter = await harness.Lifecycle.ListHistoryAsync("tenant-a", definition.Envelope.DefinitionId);
        Assert.Equal(historyBefore.Count, historyAfter.Count);
        for (var index = 0; index < historyBefore.Count; index++)
            AssertRevisionEqual(historyBefore[index], historyAfter[index]);
        var head = await harness.Lifecycle.GetPublishedHeadAsync("tenant-a", definition.Envelope.DefinitionId);
        Assert.NotNull(head);
        AssertRevisionEqual(published, head);
    }

    [Fact]
    public async Task Published_binder_returns_the_compilers_single_admitted_binding_and_matching_schema()
    {
        var harness = Harness.Create();
        var definition = CompleteDefinition("1.0.0");
        var draft = await harness.Lifecycle.SaveDraftAsync(
            Principal, definition, "version-one", 0, "save-changing-runtime");
        await harness.Lifecycle.PublishAsync(
            Principal,
            "tenant-a",
            definition.Envelope.DefinitionId,
            "version-one",
            draft.Revision,
            "publish-changing-runtime");
        var changingKinds = new ChangingFieldKindRuntime();
        var stableTextKinds = new FieldKindRuntime(new FieldKindRegistry(
        [
            new AdmittedFieldKind("text", "1.0.0", null, FieldScalarValueShape.Text),
        ]));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: stableTextKinds);
        var binder = new RecordsPublishedDefinitionBinder(
            harness.Store,
            new RecordsDefinitionCompiler(registry, harness.Domains, changingKinds));

        var binding = await binder.BindPublishedAsync(
            Principal, "tenant-a", definition.Envelope.DefinitionId, "version-one");
        var text = await registry.ValidateAsync(
            binding.Schema.Id, Encoding.UTF8.GetBytes("{\"asset_code\":\"A-1\"}"));
        var boolean = await registry.ValidateAsync(
            binding.Schema.Id, Encoding.UTF8.GetBytes("{\"asset_code\":true}"));

        Assert.Equal(1, changingKinds.BindCalls);
        Assert.Equal(FieldScalarValueShape.Text, Assert.Single(binding.FieldKinds).Kind.ValueShape);
        Assert.True(text.IsValid);
        Assert.False(boolean.IsValid);
    }

    [Fact]
    public async Task Published_binder_refuses_draft_absent_and_unresolved_runtime_pins()
    {
        var harness = Harness.Create();
        var definition = CompleteDefinition("1.0.0");
        var draft = await harness.Lifecycle.SaveDraftAsync(
            Principal, definition, "version-one", 0, "save");

        var draftError = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Binder
            .BindPublishedAsync(Principal, "tenant-a", definition.Envelope.DefinitionId, "version-one").AsTask());
        Assert.Contains(draftError.Refusals, refusal => refusal.Code == "records.binding.published_required");

        var missingError = await Assert.ThrowsAsync<DefinitionRefusalException>(() => harness.Binder
            .BindPublishedAsync(Principal, "tenant-a", definition.Envelope.DefinitionId, "missing").AsTask());
        Assert.Contains(missingError.Refusals, refusal => refusal.Code == "records.binding.published_required");

        await harness.Lifecycle.PublishAsync(
            Principal, "tenant-a", definition.Envelope.DefinitionId, "version-one", draft.Revision, "publish");
        var unavailableKinds = new FieldKindRuntime(new FieldKindRegistry([]));
        var unresolvedBinder = new RecordsPublishedDefinitionBinder(
            harness.Store,
            new RecordsDefinitionCompiler(
                new InMemorySchemaRegistry(fieldKindRuntime: unavailableKinds),
                harness.Domains,
                unavailableKinds));

        var unresolvedError = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() => unresolvedBinder
            .BindPublishedAsync(Principal, "tenant-a", definition.Envelope.DefinitionId, "version-one").AsTask());
        Assert.Contains(unresolvedError.Refusals, refusal => refusal is
            { Code: "field.kind_unresolved", JsonPointer: "/fields/0/kind" });
        Assert.Equal(2, (await harness.Lifecycle.ListHistoryAsync(
            "tenant-a", definition.Envelope.DefinitionId)).Count);
    }

    private static RecordTypeDefinition CompleteDefinition(string version) => new()
    {
        Envelope = new RecordDefinitionEnvelope(
            "definition.asset", version, "tenant-a", "package-a", "fixture:asset",
            "tenant", "definition", false, ["package-fields"]),
        RecordTypeId = "records.asset",
        Name = "Asset",
        Key = "asset",
        ClassId = "class.master",
        RecordClass = RecordClassKind.Master,
        Classes = [new RecordClassDefinition("class.master", "Master")],
        Fields = [Field("Asset code", "asset_code")],
        UniqueConstraints = [new UniqueConstraintDefinition("asset-code", ["asset_code"])],
        Policies = new RecordTypePolicies("reason-required", "full", "tenant", "retain-seven-years"),
        CreationGate = RecordCreationGate.FormOnly,
        OfflineCaptureMode = OfflineRecordCaptureMode.CaptureThenConfirm,
        Categories = ["operations", "assets"],
        LifecycleWorkflowId = "workflow.asset",
        PermissionFloor = new PermissionFloorDefinition(["reader"], ["editor"], ["manager"]),
        Standings = [new StandingDefinition("active", new RuleExpression("rules@1", "asset_code != null"))],
        Measures = [new MeasureDefinition("count", "measure.count")],
        MergePolicyId = "merge.asset",
        EffectiveDating = true,
    };

    private static RecordFieldDefinition Field(string name, string key) => new()
    {
        Name = name,
        Key = key,
        Kind = new FieldKindReference("text", "1.0.0", new Dictionary<string, string>()),
        Governance = new FieldGovernanceDefinition(false, true, false, "internal"),
        ConflictPolicy = FieldConflictPolicy.Ask,
        IsIdentity = true,
        Constraints = new FieldConstraintDefinition(true, 1, 1, ["reader"], null),
    };

    private static void AssertRevisionEqual(
        RecordsDefinitionRevision expected,
        RecordsDefinitionRevision actual)
    {
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.RestoredFromVersionId, actual.RestoredFromVersionId);
        Assert.Equal(
            RecordsDefinitionJson.SerializeCanonical(expected.Definition),
            RecordsDefinitionJson.SerializeCanonical(actual.Definition));
    }

    private sealed record Harness(
        DomainBoundary Domain,
        IFieldDomainRuntime Domains,
        IFieldKindRuntime Kinds,
        InMemorySchemaRegistry Registry,
        RecordsDefinitionAdmission Admission,
        InMemoryVersionedDefinitionStore Store,
        RecordsDefinitionLifecycle Lifecycle,
        RecordsPublishedDefinitionBinder Binder)
    {
        internal static Harness Create()
        {
            var domain = new DomainBoundary();
            var domains = new ValueDomainRuntime(domain, domain, TimeProvider.System);
            var kinds = new FieldKindRuntime(new FieldKindRegistry(
            [
                new AdmittedFieldKind("text", "1.0.0",
                    new FieldGovernanceDefinition(true, true, false, "personal")),
                new AdmittedFieldKind("text", "2.0.0",
                    new FieldGovernanceDefinition(false, false, false, "changed-default")),
            ]));
            var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
            var admission = new RecordsDefinitionAdmission(new RecordsIntentValidator(domains, kinds));
            var store = InMemoryVersionedDefinitionStore.CreateAsync(
                new Dictionary<DefinitionKind, DefinitionAsyncAdmission>
                {
                    [DefinitionKind.Records] = admission.AdmitStoreAsync,
                });
            var lifecycle = new RecordsDefinitionLifecycle(
                store, new RecordsFieldKindDefaultMaterializer(kinds));
            var binder = new RecordsPublishedDefinitionBinder(
                store, new RecordsDefinitionCompiler(registry, domains, kinds));
            return new(domain, domains, kinds, registry, admission, store, lifecycle, binder);
        }
    }

    private sealed class DomainBoundary : IFieldDomainSource, IFieldDomainSnapshot, IFieldDomainReadAuthority
    {
        public TenantId Tenant => new("tenant-a");
        public string Revision => "domain-1";
        public bool IsComplete => true;
        internal int Opens { get; private set; }
        internal Exception? OpenFault { get; set; }
        internal bool BlockUntilCancelled { get; set; }
        internal TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(
            TenantId tenant, CancellationToken cancellationToken = default)
        {
            Opens++;
            OpenStarted.TrySetResult();
            if (OpenFault is not null) throw OpenFault;
            if (BlockUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (tenant != Tenant) throw new InvalidOperationException("cross-tenant source read");
            return this;
        }

        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme) => [];
        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId) => [];
        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
            FieldDomainMember member, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class FailingSchemaRegistry : ISchemaRegistry
    {
        public ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Schema?>(null);
        public ValueTask<Schema> RegisterAsync(string jsonSchemaText, IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("registry fault");
        public ValueTask<SchemaValidationResult> ValidateAsync(SchemaId id, ReadOnlyMemory<byte> documentBytes,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public async IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class ChangingFieldKindRuntime : IFieldKindRuntime
    {
        private readonly IFieldKindRuntime _text = new FieldKindRuntime(new FieldKindRegistry(
        [
            new AdmittedFieldKind("text", "1.0.0", null, FieldScalarValueShape.Text),
        ]));
        private readonly IFieldKindRuntime _boolean = new FieldKindRuntime(new FieldKindRegistry(
        [
            new AdmittedFieldKind("text", "1.0.0", null, FieldScalarValueShape.Boolean),
        ]));

        internal int BindCalls { get; private set; }

        public ICompiledFieldKind Bind(FieldKindReference reference, string jsonPointer)
            => (++BindCalls % 2 == 1 ? _text : _boolean).Bind(reference, jsonPointer);
    }
}
