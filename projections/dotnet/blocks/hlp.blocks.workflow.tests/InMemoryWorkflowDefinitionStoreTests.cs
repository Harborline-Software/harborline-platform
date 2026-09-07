using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Harborline.Blocks.Workflow.Durable.Tests;

/// <summary>
/// Behaviour tests for <see cref="InMemoryWorkflowDefinitionStore"/> — the Harborline
/// <see cref="IWorkflowDefinitionStore"/> at the narrowed definition-store port. These pin the
/// port's contract: authored-JSON round-trip fidelity, the fail-closed admission gate at
/// register, the "current published" query, immutable-revision conflict, tenant isolation, the
/// pack-provenance restore seam, and the Harborline DI composition of the two store faces.
/// </summary>
public sealed class InMemoryWorkflowDefinitionStoreTests
{
    private const string TenantA = "tenant:acme";
    private const string TenantB = "tenant:zenith";

    private static InMemoryWorkflowDefinitionStore NewStore() => new();

    [Fact]
    public async Task Register_then_Publish_then_GetCurrentPublished_round_trips_the_authored_definition_intact()
    {
        var store = NewStore();
        var model = AdmissibleModel(TenantA, "invoice-approval.v1", "1.0.0");
        var authored = Authored("invoice-approval.v1", "1.0.0", TenantA, title: "Invoice approval");

        await store.RegisterAsync(model, authored);
        await store.PublishAsync(TenantA, "invoice-approval.v1", "1.0.0");

        var current = await store.GetCurrentPublishedAsync(TenantA, "invoice-approval.v1");
        Assert.NotNull(current);
        Assert.Equal("1.0.0", current!.Version);
        Assert.Equal(WorkflowDefinitionStatus.Published, current.Status);

        // The authored JSON round-trips VERBATIM — including the display title the lean model drops.
        Assert.Equal("invoice-approval.v1", current.Authored.GetProperty("key").GetString());
        Assert.Equal(
            "Invoice approval",
            current.Authored.GetProperty("title").GetProperty("values").GetProperty("en").GetString());
        // And the structural definition survives: the CP action on the human-approve transition.
        var action = current.Authored.GetProperty("actions")[0];
        Assert.Equal("CP", action.GetProperty("classification").GetString());
        Assert.Equal("ledger.post-journal-entry", action.GetProperty("capabilityRef").GetString());
    }

    [Fact]
    public async Task Register_an_inadmissible_definition_is_rejected_before_any_write()
    {
        var store = NewStore();
        // An action with NO classification (Unspecified) — refused at admission (fail-closed).
        var model = UnclassifiedActionModel(TenantA, "bad.v1", "1.0.0");
        var authored = Authored("bad.v1", "1.0.0", TenantA, title: "Bad");

        var ex = await Assert.ThrowsAsync<WorkflowAdmissionException>(
            () => store.RegisterAsync(model, authored).AsTask());
        Assert.Contains(WorkflowAdmissionCodes.ActionUnclassified, ex.Result.Violations.Select(v => v.Code));

        // NOTHING was persisted — the rejected definition is not retrievable.
        var current = await store.GetCurrentPublishedAsync(TenantA, "bad.v1");
        Assert.Null(current);
        await Assert.ThrowsAsync<WorkflowDefinitionNotFoundException>(
            () => store.GetAsync(TenantA, "bad.v1", "1.0.0").AsTask());
    }

    [Fact]
    public async Task Register_a_CP_action_reachable_from_an_autonomous_trigger_is_rejected()
    {
        var store = NewStore();
        var model = CpFromAutonomousModel(TenantA, "unsafe.v1", "1.0.0");
        var authored = Authored("unsafe.v1", "1.0.0", TenantA, title: "Unsafe");

        var ex = await Assert.ThrowsAsync<WorkflowAdmissionException>(
            () => store.RegisterAsync(model, authored).AsTask());
        Assert.Contains(
            WorkflowAdmissionCodes.CpReachableWithoutHumanTask, ex.Result.Violations.Select(v => v.Code));
    }

    [Fact]
    public async Task Duplicate_version_throws_Conflict()
    {
        var store = NewStore();
        var model = AdmissibleModel(TenantA, "invoice-approval.v1", "1.0.0");
        var authored = Authored("invoice-approval.v1", "1.0.0", TenantA);
        await store.RegisterAsync(model, authored);

        var conflict = await Assert.ThrowsAsync<WorkflowDefinitionConflictException>(
            () => store.RegisterAsync(model, authored).AsTask());
        Assert.Equal("invoice-approval.v1", conflict.Key);
        Assert.Equal("1.0.0", conflict.Version);
    }

    [Fact]
    public async Task GetCurrentPublished_picks_the_highest_published_version()
    {
        var store = NewStore();
        await RegisterPublished(store, TenantA, "wf", "1.0.0");
        await RegisterPublished(store, TenantA, "wf", "2.0.0");
        // A Draft (registered, never published) higher version must NOT win.
        await store.RegisterAsync(AdmissibleModel(TenantA, "wf", "3.0.0"), Authored("wf", "3.0.0", TenantA));

        var current = await store.GetCurrentPublishedAsync(TenantA, "wf");
        Assert.NotNull(current);
        Assert.Equal("2.0.0", current!.Version);
    }

    [Fact]
    public async Task GetCurrentPublished_is_tenant_scoped()
    {
        var store = NewStore();
        await RegisterPublished(store, TenantA, "wf", "1.0.0");

        // Same key published in tenant A must not surface for tenant B.
        Assert.Null(await store.GetCurrentPublishedAsync(TenantB, "wf"));
        Assert.NotNull(await store.GetCurrentPublishedAsync(TenantA, "wf"));
    }

    [Fact]
    public async Task Pack_projection_withdrawal_blocks_execution_and_explicit_restore_republishes()
    {
        var store = NewStore();
        var model = AdmissibleModel(TenantA, "pack.wf", "1.0.0");
        var authoredNode = JsonNode.Parse(AdmissibleAuthoredFullGraph().GetRawText())!.AsObject();
        authoredNode["owner"] = new JsonObject
        {
            ["scheme"] = "system",
            ["value"] = "__harborline",
        };
        authoredNode["provenance"] = "Pack";
        using var authoredDocument = JsonDocument.Parse(authoredNode.ToJsonString());
        var authored = authoredDocument.RootElement.Clone();

        await store.RegisterAsync(model, authored);
        await store.PublishAsync(TenantA, "pack.wf", "1.0.0");
        await store.WithdrawAsync(TenantA, "pack.wf", "1.0.0");

        Assert.Null(await store.GetCurrentPublishedAsync(TenantA, "pack.wf"));
        Assert.Equal(
            WorkflowDefinitionStatus.Withdrawn,
            (await store.GetAsync(TenantA, "pack.wf", "1.0.0")).Status);
        await Assert.ThrowsAsync<WorkflowDefinitionNotFoundException>(
            () => store.GetAdmittedAsync(TenantA, "pack.wf", "1.0.0").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PublishAsync(TenantA, "pack.wf", "1.0.0").AsTask());

        await store.RestorePackProjectionAsync(TenantA, "pack.wf", "1.0.0");

        Assert.Equal(
            WorkflowDefinitionStatus.Published,
            (await store.GetAdmittedAsync(TenantA, "pack.wf", "1.0.0")).Status);
    }

    [Fact]
    public async Task Explicit_pack_restore_rejects_non_pack_authored_content()
    {
        var store = NewStore();
        await store.RegisterAsync(
            AdmissibleModel(TenantA, "authored.wf", "1.0.0"),
            AdmissibleAuthoredFullGraph());
        await store.PublishAsync(TenantA, "authored.wf", "1.0.0");
        await store.WithdrawAsync(TenantA, "authored.wf", "1.0.0");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RestorePackProjectionAsync(TenantA, "authored.wf", "1.0.0").AsTask());
    }

    [Fact]
    public async Task ListByTenant_returns_only_that_tenants_definitions_in_stable_order()
    {
        var store = NewStore();
        await store.RegisterAsync(AdmissibleModel(TenantA, "beta", "1.0.0"), Authored("beta", "1.0.0", TenantA));
        await store.RegisterAsync(AdmissibleModel(TenantA, "alpha", "1.0.0"), Authored("alpha", "1.0.0", TenantA));
        await store.RegisterAsync(AdmissibleModel(TenantB, "alpha", "1.0.0"), Authored("alpha", "1.0.0", TenantB));

        var listA = new List<WorkflowDefinitionRecord>();
        await foreach (var r in store.ListByTenantAsync(TenantA))
            listA.Add(r);

        Assert.Equal(2, listA.Count);
        Assert.All(listA, r => Assert.Equal(TenantA, r.Tenant));
        Assert.Equal(new[] { "alpha", "beta" }, listA.Select(r => r.Key).ToArray());
    }

    [Fact]
    public void AddInMemoryWorkflowDefinitionStore_resolves_a_singleton_store()
    {
        var sp = new ServiceCollection()
            .AddInMemoryWorkflowDefinitionStore()
            .BuildServiceProvider();

        var a = sp.GetRequiredService<IWorkflowDefinitionStore>();
        var b = sp.GetRequiredService<IWorkflowDefinitionStore>();
        Assert.Same(a, b);
        Assert.IsType<InMemoryWorkflowDefinitionStore>(a);
    }

    // ── SC2 F-2 — the execution seam: re-validation is DI-reachable via the interface ──────────────

    [Fact]
    public void The_authoring_and_execution_faces_resolve_to_the_same_backing_store()
    {
        // SC2 F-2: one concrete store, two faces. An executor injecting IWorkflowDefinitionExecutionStore and
        // an author injecting IWorkflowDefinitionStore share ONE instance, so their views never desync.
        var sp = new ServiceCollection()
            .AddInMemoryWorkflowDefinitionStore()
            .BuildServiceProvider();

        var authoring = sp.GetRequiredService<IWorkflowDefinitionStore>();
        var execution = sp.GetRequiredService<IWorkflowDefinitionExecutionStore>();
        Assert.Same(authoring, execution);
        Assert.IsType<InMemoryWorkflowDefinitionStore>(execution);
    }

    [Fact]
    public async Task Execution_face_resolved_from_DI_re_admits_a_still_admissible_definition()
    {
        // Positive path: an executor resolving the execution INTERFACE reaches the re-validating read, and a
        // definition that is still admissible loads through it.
        var sp = new ServiceCollection()
            .AddInMemoryWorkflowDefinitionStore()
            .BuildServiceProvider();

        var authoring = sp.GetRequiredService<IWorkflowDefinitionStore>();
        await authoring.RegisterAsync(
            AdmissibleModel(TenantA, "ok.v1", "1.0.0"), AdmissibleAuthoredFullGraph());
        await authoring.PublishAsync(TenantA, "ok.v1", "1.0.0");

        var execution = sp.GetRequiredService<IWorkflowDefinitionExecutionStore>();
        var loaded = await execution.GetAdmittedCurrentPublishedAsync(TenantA, "ok.v1");
        Assert.NotNull(loaded);
        Assert.Equal("1.0.0", loaded!.Version);
    }

    [Fact]
    public async Task Execution_face_rejects_an_inadmissible_persisted_definition_that_the_authoring_face_still_hands_back()
    {
        // SC2 F-2: a definition whose PERSISTED authored JSON is inadmissible (a CP capability on an
        // autonomous edge with no human task — a tampered / pre-seam / reclassified record) is REJECTED at
        // load through the execution seam, while the lenient authoring read still hands it back. This proves
        // the execution seam is the GATED path and an executor cannot reach a non-revalidated definition.
        var sp = new ServiceCollection()
            .AddInMemoryWorkflowDefinitionStore()
            .BuildServiceProvider();

        // Persist via the authoring face: an ADMISSIBLE lean model (so register's own admission passes) but an
        // INADMISSIBLE authored payload (the bytes an executor would re-derive from). Register admits the
        // model + persists the authored JSON verbatim; the divergence models a tampered/pre-seam record.
        var authoring = sp.GetRequiredService<IWorkflowDefinitionStore>();
        await authoring.RegisterAsync(
            AdmissibleModel(TenantA, "tampered.v1", "1.0.0"),
            InadmissibleAuthoredCpOnAutonomousEdge());
        await authoring.PublishAsync(TenantA, "tampered.v1", "1.0.0");

        // The lenient authoring read hands the (inadmissible) definition back as-is — no re-validation.
        var lenient = await authoring.GetCurrentPublishedAsync(TenantA, "tampered.v1");
        Assert.NotNull(lenient);

        // The execution seam re-admits at load and REFUSES it (fail-closed) — it never executes.
        var execution = sp.GetRequiredService<IWorkflowDefinitionExecutionStore>();
        var ex = await Assert.ThrowsAsync<WorkflowAdmissionException>(
            () => execution.GetAdmittedCurrentPublishedAsync(TenantA, "tampered.v1").AsTask());
        Assert.Contains(
            WorkflowAdmissionCodes.CpReachableWithoutHumanTask, ex.Result.Violations.Select(v => v.Code));

        // The exact-version execution read refuses it too.
        await Assert.ThrowsAsync<WorkflowAdmissionException>(
            () => execution.GetAdmittedAsync(TenantA, "tampered.v1", "1.0.0").AsTask());
    }

    // -------------------- helpers --------------------

    /// <summary>
    /// A COMPLETE admissible authored graph (states + triggers + transitions + a CP action on the HUMAN
    /// approve edge) — the wire-form of <see cref="AdmissibleModel"/>. Unlike the storage-only <c>Authored</c>
    /// helper (which omits triggers/transitions and so is inadmissible when re-derived), this re-admits at
    /// load, so an execution-face read returns it.
    /// </summary>
    private static JsonElement AdmissibleAuthoredFullGraph()
    {
        const string json = """
        {
          "initialState": "Draft",
          "mutability": "Locked",
          "states": [
            { "id": "Draft", "kind": "Normal" },
            { "id": "PendingApproval", "kind": "Normal" },
            { "id": "Posted", "kind": "Terminal" },
            { "id": "Rejected", "kind": "Terminal" }
          ],
          "triggers": [
            { "id": "issued", "kind": "Event", "eventType": "Issued" },
            { "id": "approve", "kind": "HumanAction", "task": "invoice-approval" },
            { "id": "reject", "kind": "HumanAction", "task": "invoice-approval" }
          ],
          "transitions": [
            { "id": "t-issue", "from": "Draft", "on": "issued", "to": "PendingApproval" },
            { "id": "t-approve", "from": "PendingApproval", "on": "approve", "to": "Posted" },
            { "id": "t-reject", "from": "PendingApproval", "on": "reject", "to": "Rejected" }
          ],
          "actions": [
            {
              "id": "a-post-je",
              "on": { "transition": "t-approve" },
              "kind": "CreateRecord",
              "capabilityRef": "ledger.post-journal-entry",
              "classification": "CP"
            }
          ],
          "guards": []
        }
        """;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// Authored JSON that is INADMISSIBLE at load: a CP capability (<c>ledger.post-journal-entry</c>, CP in the
    /// canonical registry) fires on an AUTONOMOUS Schedule edge with no interposed human task ⇒
    /// <c>CpReachableWithoutHumanTask</c>. Parseable by the canonical wire mapper (shape mirrors the
    /// load-validator test's proven-parseable JSON). Used to simulate a tampered/pre-seam persisted record.
    /// </summary>
    private static JsonElement InadmissibleAuthoredCpOnAutonomousEdge()
    {
        const string json = """
        {
          "initialState": "start",
          "mutability": "Locked",
          "states": [
            { "id": "start", "kind": "Normal" },
            { "id": "done", "kind": "Terminal" }
          ],
          "triggers": [
            { "id": "nightly", "kind": "Schedule", "rrule": "FREQ=DAILY" }
          ],
          "transitions": [
            { "id": "t1", "from": "start", "on": "nightly", "to": "done" }
          ],
          "actions": [
            {
              "id": "a1",
              "on": { "transition": "t1" },
              "kind": "InvokeService",
              "capabilityRef": "ledger.post-journal-entry",
              "classification": "CP"
            }
          ],
          "guards": []
        }
        """;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static async Task RegisterPublished(
        InMemoryWorkflowDefinitionStore store, string tenant, string key, string version)
    {
        await store.RegisterAsync(AdmissibleModel(tenant, key, version), Authored(key, version, tenant));
        await store.PublishAsync(tenant, key, version);
    }

    /// <summary>The invoice-approval graph with a caller-supplied action set (WorkflowDefinition is a class,
    /// not a record — each variant is built fresh rather than via <c>with</c>).</summary>
    private static WorkflowDefinition ModelWith(
        string tenant, string key, string version, List<WorkflowActionBindingDef> actions) => new()
        {
            Key = key,
            Version = version,
            Tenant = tenant,
            InitialState = "Draft",
            States = new List<WorkflowStateDef>
        {
            new() { Id = "Draft", Kind = WorkflowStateKind.Normal },
            new() { Id = "PendingApproval", Kind = WorkflowStateKind.Normal },
            new() { Id = "Posted", Kind = WorkflowStateKind.Terminal },
            new() { Id = "Rejected", Kind = WorkflowStateKind.Terminal },
        },
            Triggers = new List<WorkflowTriggerBindingDef>
        {
            new() { Id = "issued", Kind = WorkflowTriggerKind.Event, EventType = "Issued" },
            new() { Id = "approve", Kind = WorkflowTriggerKind.HumanAction, Task = "invoice-approval" },
            new() { Id = "reject", Kind = WorkflowTriggerKind.HumanAction, Task = "invoice-approval" },
        },
            Transitions = new List<WorkflowTransitionDef>
        {
            new() { Id = "t-issue", From = "Draft", On = "issued", To = "PendingApproval" },
            new() { Id = "t-approve", From = "PendingApproval", On = "approve", To = "Posted" },
            new() { Id = "t-reject", From = "PendingApproval", On = "reject", To = "Rejected" },
        },
            Actions = actions,
        };

    /// <summary>The invoice-approval shape: a CP post-JE action on the HUMAN approve transition (admissible).</summary>
    private static WorkflowDefinition AdmissibleModel(string tenant, string key, string version)
        => ModelWith(tenant, key, version, new List<WorkflowActionBindingDef>
        {
            new()
            {
                Id = "a-post-je",
                OnTransition = "t-approve",
                Kind = WorkflowActionKind.CreateRecord,
                CapabilityRef = "ledger.post-journal-entry",
                Classification = ActionClassification.CP,
            },
        });

    /// <summary>The admissible shape but with the action's classification left Unspecified (refused).</summary>
    private static WorkflowDefinition UnclassifiedActionModel(string tenant, string key, string version)
        => ModelWith(tenant, key, version, new List<WorkflowActionBindingDef>
        {
            new()
            {
                Id = "a-post-je",
                OnTransition = "t-approve",
                Kind = WorkflowActionKind.CreateRecord,
                CapabilityRef = "ledger.post-journal-entry",
                // Classification omitted ⇒ Unspecified ⇒ refused at admission (fail-closed).
            },
        });

    /// <summary>The admissible shape but the CP action fires on the autonomous 'issued' transition (refused).</summary>
    private static WorkflowDefinition CpFromAutonomousModel(string tenant, string key, string version)
        => ModelWith(tenant, key, version, new List<WorkflowActionBindingDef>
        {
            new()
            {
                Id = "a-post-je",
                OnTransition = "t-issue", // trigger 'issued' is an Event (autonomous) — no human gate
                Kind = WorkflowActionKind.CreateRecord,
                CapabilityRef = "ledger.post-journal-entry",
                Classification = ActionClassification.CP,
            },
        });

    private static JsonElement Authored(string key, string version, string tenant, string title = "Untitled")
        => JsonSerializer.SerializeToElement(new
        {
            key,
            version,
            status = "Published",
            tenant,
            title = new { defaultLocale = "en", values = new Dictionary<string, string> { ["en"] = title } },
            initialState = "Draft",
            states = new object[]
            {
                new { id = "Draft", label = Text("Draft"), kind = "Normal" },
                new { id = "PendingApproval", label = Text("Pending approval"), kind = "Normal" },
                new { id = "Posted", label = Text("Posted"), kind = "Terminal" },
                new { id = "Rejected", label = Text("Rejected"), kind = "Terminal" },
            },
            actions = new object[]
            {
                new
                {
                    id = "a-post-je",
                    on = new { transition = "t-approve" },
                    kind = "CreateRecord",
                    capabilityRef = "ledger.post-journal-entry",
                    classification = "CP",
                },
            },
        });

    private static object Text(string en)
        => new { defaultLocale = "en", values = new Dictionary<string, string> { ["en"] = en } };
}
