// Workflow engine-substrate package-consumer vertical (hlp.blocks.workflow +
// hlp.blocks.workflow-interpreter). A clean host restores ONLY the packed
// Harborline.Blocks.Workflow.Interpreter artifact (the engine core arrives transitively) and
// drives define → admit → publish → trigger → park → human-confirm → complete through the
// packaged seams: the two-faced definition store, the fail-closed admission gate, the
// dispatcher's idempotency-guarded atomic advance over a host-supplied IWorkflowStore, the
// broker-PEP's SoD-gated CP confirm, and the declarative interpreter fallback.
//
// This proves the ENGINE SUBSTRATE composes from packages alone. It is NOT the HLF-053
// workflows capability vertical (that requires the application-derived ledger definition and the
// 13-pair client/server admission-mirror cross-check, tracked by ticket 074).

using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Harborline.Blocks.Workflow;
using Harborline.Blocks.Workflow.Durable;
using Harborline.Blocks.Workflow.Interpreter;

const string Tenant = "tenant:consumer";
const string Key = "vendor-invoice-approval.v1";
const string Version = "1.0.0";

var engineParty = Guid.Parse("e0000000-0000-0000-0000-0000000000e1");
var humanParty = Guid.Parse("40000000-0000-0000-0000-000000000001");

// ── 1. The typed state-machine runtime (the non-durable engine face) ──
var definition = new WorkflowDefinitionBuilder<DemoState, DemoTrigger, object>()
    .StartAt(DemoState.Draft)
    .Transition(DemoState.Draft, DemoTrigger.Submit, DemoState.Submitted)
    .Transition(DemoState.Submitted, DemoTrigger.Approve, DemoState.Approved)
    .Terminal(DemoState.Approved)
    .Build();
var runtime = new InMemoryWorkflowRuntime();
var typedInstance = await runtime.StartAsync(definition, new object());
typedInstance = await runtime.FireAsync<DemoState, DemoTrigger, object>(typedInstance.Id, DemoTrigger.Submit);
typedInstance = await runtime.FireAsync<DemoState, DemoTrigger, object>(typedInstance.Id, DemoTrigger.Approve);
if (!typedInstance.IsTerminal || typedInstance.CurrentState != DemoState.Approved || typedInstance.TransitionCount != 2)
{
    throw new InvalidOperationException("typed runtime did not reach the terminal state through the packaged interface.");
}

// ── 2. Compose the durable engine from the packaged seams ──
var effectStagedCount = 0;
var hostStore = new HostInMemoryWorkflowStore();
var services = new ServiceCollection();
services.AddSingleton(TimeProvider.System);
services.AddDurableWorkflowEngine();
services.AddInMemoryWorkflowDefinitionStore();
services.AddSingleton<IWorkflowStore>(hostStore);
services.AddWorkflowEffectFactory(
    "ledger.post-journal-entry", WorkflowEffectReach.Internal,
    (_, _) => new WorkflowEffect((_, _) => { effectStagedCount++; return Task.CompletedTask; }));
services.AddSingleton<IWorkflowConfirmationContext>(
    new StaticConfirmation(new WorkflowProposerIdentity(engineParty, IsHuman: false),
        new WorkflowConfirmerIdentity(humanParty, IsHuman: true)));
services.AddDeclarativeWorkflowInterpreter();
await using var provider = services.BuildServiceProvider();

// ── 3. Define + admit + publish through the packaged definition-store port ──
var authored = AuthoredDefinition();
var store = provider.GetRequiredService<IWorkflowDefinitionStore>();
var model = WorkflowDefinitionWireMapper.ToModel(authored, Tenant, Key, Version);
await store.RegisterAsync(model, authored);
await store.PublishAsync(Tenant, Key, Version);

// The fence is real through the package: an unclassified action is refused before any write.
var inadmissible = WorkflowDefinitionWireMapper.ToModel(UnclassifiedDefinition(), Tenant, "bad.v1", Version);
try
{
    await store.RegisterAsync(inadmissible, UnclassifiedDefinition());
    throw new InvalidOperationException("the packaged admission fence admitted an unclassified action.");
}
catch (WorkflowAdmissionException)
{
    // fail-closed, as required.
}

// ── 4. Instantiate + dispatch: autonomous event → CP park on the human task ──
await hostStore.CreateInstanceAsync(new WorkflowInstanceRecord
{
    Id = "inst-1",
    TenantId = Tenant,
    DefinitionKey = Key,
    DefinitionVersion = Version,
    CurrentStep = "Draft",
    Status = WorkflowStatus.Running,
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow,
});
var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();

var parked = await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "inst-1", "Draft"));
var afterPark = await hostStore.LoadAsync("inst-1")
    ?? throw new InvalidOperationException("instance vanished after park.");
if (parked != WorkflowDispatchResult.Parked || afterPark.Status != WorkflowStatus.Parked || afterPark.CurrentStep != "PendingApproval")
{
    throw new InvalidOperationException($"autonomous dispatch did not park on the human task (got {parked} at {afterPark.CurrentStep}).");
}

// ── 5. Human approve → SoD-gated CP confirm through the broker → atomic advance ──
var approved = await dispatcher.DispatchAsync(
    WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "inst-1", "PendingApproval", "{\"decision\":\"approve\"}"));
var afterApprove = await hostStore.LoadAsync("inst-1")
    ?? throw new InvalidOperationException("instance vanished after approve.");
if (approved != WorkflowDispatchResult.Advanced || afterApprove.Status != WorkflowStatus.Completed
    || afterApprove.CurrentStep != "Posted" || effectStagedCount != 1)
{
    throw new InvalidOperationException(
        $"human approve did not complete the instance with exactly one staged effect (got {approved}, {afterApprove.Status}, effects {effectStagedCount}).");
}

// ── 6. Redelivered trigger → idempotency replay, no double effect ──
var replayed = await dispatcher.DispatchAsync(
    WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "inst-1", "PendingApproval", "{\"decision\":\"approve\"}"));
if (replayed != WorkflowDispatchResult.Terminal && replayed != WorkflowDispatchResult.ReplayedNoOp)
{
    throw new InvalidOperationException($"redelivered trigger was not a no-op (got {replayed}).");
}
if (effectStagedCount != 1)
{
    throw new InvalidOperationException("redelivery staged a second effect — the idempotency guard failed.");
}

var sink = provider.GetRequiredService<CountingWorkflowApprovalDecisionSink>();

// ── 7. The narrowed route seam: a loopback HTTP host composed from packages only ──
// The earlier source local-node-host route rows (definition save/load/list/versioning, cross-tenant
// isolation, 404/400/422 mapping, confirmation list + action) re-proven at the Harborline API
// surface, the Forms authoring-host precedent: one direct Harborline package reference, a real
// Kestrel loopback, real status codes, over the durable file-journal store.
var journalDirectory = Path.Combine(Path.GetTempPath(), "hlp-workflow-consumer-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(journalDirectory);
var routeEffectCount = 0;
var hostBuilder = WebApplication.CreateBuilder(args);
hostBuilder.Services.AddSingleton(TimeProvider.System);
hostBuilder.Services.AddDurableWorkflowEngine();
hostBuilder.Services.AddInMemoryWorkflowDefinitionStore();
hostBuilder.Services.AddFileJournalWorkflowStore(new FileJournalWorkflowStoreOptions
{
    JournalPath = Path.Combine(journalDirectory, "workflow.hlwj"),
});
hostBuilder.Services.AddWorkflowEffectFactory(
    "ledger.post-journal-entry", WorkflowEffectReach.Internal,
    (_, _) => new WorkflowEffect((unitOfWork, _) =>
    {
        ((FileJournalWorkflowUnitOfWork)unitOfWork).StageEffectPayload("route-je");
        routeEffectCount++;
        return Task.CompletedTask;
    }));
hostBuilder.Services.AddSingleton<IWorkflowConfirmationContext>(
    new StaticConfirmation(new WorkflowProposerIdentity(engineParty, IsHuman: false),
        new WorkflowConfirmerIdentity(humanParty, IsHuman: true)));
hostBuilder.Services.AddDeclarativeWorkflowInterpreter();
var app = hostBuilder.Build();

// Tenant is server-derived per request (the X-Tenant header stands in for the ambient
// authenticated tenant context) — never taken from the body.
static string TenantOf(HttpRequest request)
    => request.Headers.TryGetValue("X-Tenant", out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.ToString()
        : "tenant:consumer";

app.MapPut("/api/workflows/definitions/{key}", async (string key, HttpRequest request, IWorkflowDefinitionStore definitions) =>
{
    var tenant = TenantOf(request);
    using var document = await JsonDocument.ParseAsync(request.Body);
    var revisions = 0;
    await foreach (var row in definitions.ListByTenantAsync(tenant))
    {
        if (row.Key == key) revisions++;
    }
    var version = $"1.0.{revisions + 1}"; // server-minted — never trusted from the body.
    try
    {
        var model = WorkflowDefinitionWireMapper.ToModel(document.RootElement, tenant, key, version);
        await definitions.RegisterAsync(model, document.RootElement);
        await definitions.PublishAsync(tenant, key, version);
        return Results.Json(new { key, version });
    }
    catch (WorkflowAdmissionException refusal)
    {
        return Results.UnprocessableEntity(new { codes = refusal.Result.Violations.Select(v => v.Code).ToArray() });
    }
});

app.MapGet("/api/workflows/definitions/{key}", async (string key, HttpRequest request, IWorkflowDefinitionStore definitions) =>
{
    var record = await definitions.GetCurrentPublishedAsync(TenantOf(request), key);
    return record is null
        ? Results.NotFound()
        : Results.Json(new { record.Key, record.Version, Authored = record.Authored });
});

app.MapGet("/api/workflows/definitions", async (HttpRequest request, IWorkflowDefinitionStore definitions) =>
{
    var rows = new List<object>();
    await foreach (var row in definitions.ListByTenantAsync(TenantOf(request)))
        rows.Add(new { row.Key, row.Version, Status = row.Status.ToString() });
    return Results.Json(rows);
});

app.MapGet("/api/workflows/confirmations", async (HttpRequest request, FileJournalWorkflowStore workflowStore) =>
{
    var tenant = TenantOf(request);
    var rows = new List<object>();
    var parked = await workflowStore.LoadAsync("route-inst-1");
    if (parked is { Status: WorkflowStatus.Parked } && parked.TenantId == tenant)
    {
        var basis = (await workflowStore.EventsAsync(parked.Id)).LastOrDefault(e => e.EventType == "Parked")?.DataJson ?? "{}";
        rows.Add(new { parked.Id, parked.CurrentStep, Basis = JsonSerializer.Deserialize<JsonElement>(basis) });
    }
    return Results.Json(rows);
});

app.MapPost("/api/workflows/confirmations/{instanceId}/action", async (
    string instanceId, HttpRequest request, IWorkflowTriggerDispatcher routeDispatcher, FileJournalWorkflowStore workflowStore) =>
{
    using var document = await JsonDocument.ParseAsync(request.Body);
    if (!document.RootElement.TryGetProperty("decision", out var decision) || decision.ValueKind != JsonValueKind.String
        || string.IsNullOrWhiteSpace(decision.GetString()))
    {
        return Results.BadRequest(new { error = "a string 'decision' is required" });
    }
    var instance = await workflowStore.LoadAsync(instanceId);
    if (instance is null || instance.TenantId != TenantOf(request))
    {
        return Results.NotFound();
    }
    try
    {
        var result = await routeDispatcher.DispatchAsync(WorkflowTrigger.For(
            WorkflowTriggerKind.HumanAction, instanceId, instance.CurrentStep,
            $"{{\"decision\":\"{decision.GetString()}\"}}"));
        return Results.Json(new { result = result.ToString() });
    }
    catch (InvalidOperationException)
    {
        return Results.BadRequest(new { error = "the decision is not valid for this step" });
    }
});

app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();
try
{
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

    // Save → mint 1.0.1 → load round-trips; re-save mints 1.0.2; list surfaces both.
    var saved = await client.PutAsync("/api/workflows/definitions/route-wf", Content(AuthoredDefinition()));
    if (saved.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"save failed: {saved.StatusCode}");
    var loaded = await client.GetAsync("/api/workflows/definitions/route-wf");
    if (loaded.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("load after save failed");
    var resaved = await client.PutAsync("/api/workflows/definitions/route-wf", Content(AuthoredDefinition()));
    var resavedBody = JsonDocument.Parse(await resaved.Content.ReadAsStringAsync());
    if (resavedBody.RootElement.GetProperty("version").GetString() != "1.0.2")
        throw new InvalidOperationException("re-save did not mint the next server-owned version");
    var listBody = JsonDocument.Parse(await (await client.GetAsync("/api/workflows/definitions")).Content.ReadAsStringAsync());
    if (listBody.RootElement.GetArrayLength() != 2) throw new InvalidOperationException("list did not surface saved revisions");

    // Cross-tenant isolation and unsaved 404.
    using (var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/workflows/definitions/route-wf"))
    {
        foreign.Headers.Add("X-Tenant", "tenant:zenith");
        if ((await client.SendAsync(foreign)).StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException("cross-tenant read was not isolated to 404");
    }
    if ((await client.GetAsync("/api/workflows/definitions/never-saved")).StatusCode != HttpStatusCode.NotFound)
        throw new InvalidOperationException("unsaved definition did not 404");

    // Wired 422 admission refusals at the route seam.
    var unclassified = await client.PutAsync("/api/workflows/definitions/bad-unclassified", Content(UnclassifiedDefinition()));
    if (unclassified.StatusCode != HttpStatusCode.UnprocessableEntity)
        throw new InvalidOperationException($"unclassified action was not refused 422: {unclassified.StatusCode}");
    var cpAutonomous = await client.PutAsync("/api/workflows/definitions/bad-cp-autonomous", Content(CpOnAutonomousDefinition()));
    if (cpAutonomous.StatusCode != HttpStatusCode.UnprocessableEntity)
        throw new InvalidOperationException($"CP-from-autonomous was not refused 422: {cpAutonomous.StatusCode}");

    // Park an instance of the published authored definition, then drive the confirm routes.
    var routeStore = app.Services.GetRequiredService<FileJournalWorkflowStore>();
    await routeStore.CreateInstanceAsync(new WorkflowInstanceRecord
    {
        Id = "route-inst-1",
        TenantId = "tenant:consumer",
        DefinitionKey = "route-wf",
        DefinitionVersion = "1.0.1",
        CurrentStep = "Draft",
        Status = WorkflowStatus.Running,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    });
    var routeDispatcher = app.Services.GetRequiredService<IWorkflowTriggerDispatcher>();
    await routeDispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "route-inst-1", "Draft"));

    var confirmations = JsonDocument.Parse(
        await (await client.GetAsync("/api/workflows/confirmations")).Content.ReadAsStringAsync());
    if (confirmations.RootElement.GetArrayLength() != 1
        || confirmations.RootElement[0].GetProperty("basis").GetProperty("kind").GetString() != "cp-approval-basis")
        throw new InvalidOperationException("the confirmation list did not surface the parked CP task with its basis");

    if ((await client.PostAsync("/api/workflows/confirmations/unknown-inst/action", Content("{\"decision\":\"approve\"}"))).StatusCode
        != HttpStatusCode.NotFound)
        throw new InvalidOperationException("unknown-instance action did not 404");
    if ((await client.PostAsync("/api/workflows/confirmations/route-inst-1/action", Content("{\"note\":\"missing decision\"}"))).StatusCode
        != HttpStatusCode.BadRequest)
        throw new InvalidOperationException("missing decision did not 400");
    if ((await client.PostAsync("/api/workflows/confirmations/route-inst-1/action", Content("{\"decision\":\"escalate\"}"))).StatusCode
        != HttpStatusCode.BadRequest)
        throw new InvalidOperationException("invalid decision verb did not 400");

    var approve = await client.PostAsync("/api/workflows/confirmations/route-inst-1/action", Content("{\"decision\":\"approve\"}"));
    if (approve.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("approve action failed");
    var redeliveredApprove = await client.PostAsync("/api/workflows/confirmations/route-inst-1/action", Content("{\"decision\":\"approve\"}"));
    if (redeliveredApprove.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("redelivered approve failed");
    if (routeEffectCount != 1 || (await routeStore.CommittedEffectPayloadsAsync("route-inst-1")).Count != 1)
        throw new InvalidOperationException("route approve did not commit exactly one durable effect");
}
finally
{
    await app.StopAsync();
    try { Directory.Delete(journalDirectory, recursive: true); } catch { }
}

Console.WriteLine("WORKFLOW_PACKAGE_PASS:" + JsonSerializer.Serialize(new
{
    typedRuntimeTransitions = typedInstance.TransitionCount,
    definitionRegisteredAndPublished = true,
    admissionFailClosed = true,
    parkedOnHumanTask = true,
    cpConfirmedThroughBroker = sink.ConfirmedCount == 1,
    effectsStaged = effectStagedCount,
    redeliveryReplayed = true,
    routeSeam = new
    {
        saveLoadRoundTrip = true,
        serverMintedNextVersion = true,
        crossTenant404 = true,
        unsaved404 = true,
        admission422 = true,
        confirmationListWithBasis = true,
        unknownInstance404 = true,
        invalidDecision400 = true,
        approveCommitsExactlyOneDurableEffect = true,
    },
}));

// ── 8. The workflows CAPABILITY vertical (runs only under the capability fixture, which
//       copies the application-derived admission corpus + the renderer lane's verdicts in) ──
if (File.Exists("admission-mirror-cases.json") && File.Exists("client-verdicts.json"))
{
    using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync("admission-mirror-cases.json"));
    using var clientVerdicts = JsonDocument.Parse(await File.ReadAllTextAsync("client-verdicts.json"));

    // The engine fence over the corpus's pinned registry rows (identical to the canonical
    // embedded registry — asserted, not assumed).
    var registryRows = new Dictionary<string, Harborline.Blocks.Workflow.Durable.ActionClassification>(StringComparer.Ordinal);
    foreach (var row in corpus.RootElement.GetProperty("registry").EnumerateObject())
    {
        registryRows[row.Name] = Enum.Parse<Harborline.Blocks.Workflow.Durable.ActionClassification>(row.Value.GetString()!);
        if (CapabilityAuthorityRegistry.Canonical.AuthorityOf(row.Name) != registryRows[row.Name])
            throw new InvalidOperationException($"corpus registry row '{row.Name}' disagrees with the packaged canonical registry.");
    }
    var fence = new WorkflowAdmissionValidator(CapabilityAuthorityRegistry.FromMap(registryRows));

    // The 13-pair cross-lane proof: the packaged SERVER fence verdict for every case must
    // agree with the packed CLIENT mirror's recorded verdict — verdict AND expected code.
    var cases = corpus.RootElement.GetProperty("cases");
    var clientRows = clientVerdicts.RootElement;
    if (cases.GetArrayLength() != 13 || clientRows.GetArrayLength() != 13)
        throw new InvalidOperationException("the admission corpus must carry exactly the 13 ledger pairs.");
    var disagreements = 0;
    for (var index = 0; index < 13; index++)
    {
        var row = cases[index];
        var caseDefinition = row.GetProperty("definition");
        var caseModel = WorkflowDefinitionWireMapper.ToModel(
            caseDefinition,
            caseDefinition.GetProperty("tenant").GetString()!,
            caseDefinition.GetProperty("key").GetString()!,
            caseDefinition.GetProperty("version").GetString()!);
        var server = fence.Validate(caseModel);
        var serverCodes = server.Violations.Select(violation => violation.Code).ToHashSet(StringComparer.Ordinal);

        var client = clientRows[index];
        var clientValid = client.GetProperty("isValid").GetBoolean();
        var clientCodes = client.GetProperty("codes").EnumerateArray().Select(code => code.GetString()!).ToHashSet(StringComparer.Ordinal);

        var expected = row.GetProperty("expected");
        var expectedValid = expected.GetProperty("isValid").GetBoolean();
        var expectedCode = expected.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;

        var serverAgrees = server.IsValid == expectedValid && (expectedCode is null || serverCodes.Contains(expectedCode));
        var lanesAgree = server.IsValid == clientValid && (expectedCode is null || clientCodes.Contains(expectedCode) == serverCodes.Contains(expectedCode));
        if (!serverAgrees || !lanesAgree) disagreements++;
    }
    if (disagreements != 0)
        throw new InvalidOperationException($"the 13-pair client/server admission cross-check disagreed on {disagreements} pair(s).");

    // Drive the application-derived canonical seed (with its execution binding) through the packaged
    // interfaces over the durable store: define -> admit -> trigger -> transition -> complete,
    // with ledger-exact outcomes (park at PendingApproval with the CP basis; approve -> Posted
    // with exactly one JE effect; reject -> Rejected with none; redelivery replays).
    var executionDefinition = corpus.RootElement.GetProperty("executionBinding").GetProperty("definition").Clone();
    var capabilityJournal = Path.Combine(Path.GetTempPath(), "hlp-workflow-capability-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(capabilityJournal);
    var capabilityEffects = 0;
    var capabilityServices = new ServiceCollection();
    capabilityServices.AddSingleton(TimeProvider.System);
    capabilityServices.AddDurableWorkflowEngine();
    capabilityServices.AddInMemoryWorkflowDefinitionStore();
    capabilityServices.AddFileJournalWorkflowStore(new FileJournalWorkflowStoreOptions
    {
        JournalPath = Path.Combine(capabilityJournal, "workflow.hlwj"),
    });
    capabilityServices.AddWorkflowEffectFactory(
        "ledger.post-journal-entry", WorkflowEffectReach.Internal,
        (_, _) => new WorkflowEffect((unitOfWork, _) =>
        {
            ((FileJournalWorkflowUnitOfWork)unitOfWork).StageEffectPayload("ledger.post-journal-entry");
            capabilityEffects++;
            return Task.CompletedTask;
        }));
    capabilityServices.AddSingleton<IWorkflowConfirmationContext>(
        new StaticConfirmation(new WorkflowProposerIdentity(engineParty, IsHuman: false),
            new WorkflowConfirmerIdentity(humanParty, IsHuman: true)));
    capabilityServices.AddDeclarativeWorkflowInterpreter();
    await using (var capability = capabilityServices.BuildServiceProvider())
    {
        var seedTenant = executionDefinition.GetProperty("tenant").GetString()!;
        const string seedKey = "invoice-approval.v1";
        const string seedVersion = "1.0.0";

        // DEFINE + ADMIT: the seed admits through the packaged fence at register (fail-closed) and publishes.
        var definitions = capability.GetRequiredService<IWorkflowDefinitionStore>();
        await definitions.RegisterAsync(
            WorkflowDefinitionWireMapper.ToModel(executionDefinition, seedTenant, seedKey, seedVersion), executionDefinition);
        await definitions.PublishAsync(seedTenant, seedKey, seedVersion);

        var capabilityStore = capability.GetRequiredService<FileJournalWorkflowStore>();
        var capabilityDispatcher = capability.GetRequiredService<IWorkflowTriggerDispatcher>();
        async Task StartInstanceAsync(string id) => await capabilityStore.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = id, TenantId = seedTenant, DefinitionKey = seedKey, DefinitionVersion = seedVersion,
            CurrentStep = "Draft", Status = WorkflowStatus.Running,
            StateJson = "{\"amount\":9000}",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        // TRIGGER: the issued event parks on the human task with the CP basis (ledger-exact).
        await StartInstanceAsync("cap-approve");
        if (await capabilityDispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "cap-approve", "Draft"))
            != WorkflowDispatchResult.Parked)
            throw new InvalidOperationException("the seed's issued event did not park on the human task.");
        var capabilityParked = await capabilityStore.LoadAsync("cap-approve");
        if (capabilityParked!.CurrentStep != "PendingApproval" || capabilityParked.Status != WorkflowStatus.Parked)
            throw new InvalidOperationException("the seed did not park at PendingApproval.");

        // TRANSITION + COMPLETE: approve -> Posted with exactly one JE effect; redelivery replays.
        if (await capabilityDispatcher.DispatchAsync(WorkflowTrigger.For(
                WorkflowTriggerKind.HumanAction, "cap-approve", "PendingApproval", "{\"decision\":\"approve\"}"))
            != WorkflowDispatchResult.Advanced)
            throw new InvalidOperationException("the seed's approve did not advance.");
        var capabilityPosted = await capabilityStore.LoadAsync("cap-approve");
        if (capabilityPosted!.CurrentStep != "Posted" || capabilityPosted.Status != WorkflowStatus.Completed
            || (await capabilityStore.CommittedEffectPayloadsAsync("cap-approve")).Count != 1)
            throw new InvalidOperationException("approve did not complete at Posted with exactly one JE effect.");
        var capabilityReplay = await capabilityDispatcher.DispatchAsync(WorkflowTrigger.For(
            WorkflowTriggerKind.HumanAction, "cap-approve", "PendingApproval", "{\"decision\":\"approve\"}"));
        if (capabilityReplay is not (WorkflowDispatchResult.ReplayedNoOp or WorkflowDispatchResult.Terminal)
            || capabilityEffects != 1)
            throw new InvalidOperationException("the redelivered approve was not a no-op.");

        // The reject path: Rejected, no effect, one override recorded.
        await StartInstanceAsync("cap-reject");
        await capabilityDispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "cap-reject", "Draft"));
        if (await capabilityDispatcher.DispatchAsync(WorkflowTrigger.For(
                WorkflowTriggerKind.HumanAction, "cap-reject", "PendingApproval", "{\"decision\":\"reject\"}"))
            != WorkflowDispatchResult.Advanced)
            throw new InvalidOperationException("the seed's reject did not advance.");
        var capabilityRejected = await capabilityStore.LoadAsync("cap-reject");
        var capabilitySink = capability.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        if (capabilityRejected!.CurrentStep != "Rejected" || (await capabilityStore.CommittedEffectPayloadsAsync("cap-reject")).Count != 0
            || capabilitySink.ConfirmedCount != 1 || capabilitySink.OverriddenCount != 1)
            throw new InvalidOperationException("reject did not land at Rejected effect-free with one confirm and one override recorded.");
    }
    try { Directory.Delete(capabilityJournal, recursive: true); } catch { }

    Console.WriteLine("WORKFLOW_CAPABILITY_PASS:" + JsonSerializer.Serialize(new
    {
        crossLanePairs = 13,
        disagreements = 0,
        seedAdmitted = true,
        parkedAtPendingApproval = true,
        approvePostsExactlyOnce = true,
        rejectPostsNone = true,
        redeliveryReplays = true,
        confirmsRecorded = 1,
        overridesRecorded = 1,
    }));
}

static StringContent Content(object payload)
    => payload is JsonElement element
        ? new StringContent(element.GetRawText(), System.Text.Encoding.UTF8, "application/json")
        : new StringContent((string)payload, System.Text.Encoding.UTF8, "application/json");

static JsonElement CpOnAutonomousDefinition()
{
    const string json = """
    {
      "initialState": "Draft",
      "states": [
        { "id": "Draft", "kind": "Normal" },
        { "id": "Done", "kind": "Terminal" }
      ],
      "triggers": [ { "id": "nightly", "kind": "Schedule", "rrule": "FREQ=DAILY" } ],
      "transitions": [ { "id": "t1", "from": "Draft", "on": "nightly", "to": "Done" } ],
      "actions": [
        {
          "id": "a1",
          "on": { "transition": "t1" },
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

static JsonElement AuthoredDefinition()
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
        { "id": "approve", "kind": "HumanAction", "task": "vendor-invoice-approval" },
        { "id": "reject", "kind": "HumanAction", "task": "vendor-invoice-approval" }
      ],
      "transitions": [
        { "id": "t-issue", "from": "Draft", "on": "issued", "to": "PendingApproval" },
        { "id": "t-approve", "from": "PendingApproval", "on": "approve", "to": "Posted", "guard": "decision:approve" },
        { "id": "t-reject", "from": "PendingApproval", "on": "reject", "to": "Rejected", "guard": "decision:reject" }
      ],
      "guards": [
        { "id": "decision:approve" },
        { "id": "decision:reject" }
      ],
      "actions": [
        {
          "id": "a-post-je",
          "on": { "transition": "t-approve" },
          "kind": "CreateRecord",
          "capabilityRef": "ledger.post-journal-entry",
          "classification": "CP"
        }
      ]
    }
    """;
    return JsonDocument.Parse(json).RootElement.Clone();
}

static JsonElement UnclassifiedDefinition()
{
    const string json = """
    {
      "initialState": "Draft",
      "states": [
        { "id": "Draft", "kind": "Normal" },
        { "id": "Done", "kind": "Terminal" }
      ],
      "triggers": [ { "id": "go", "kind": "Event", "eventType": "Go" } ],
      "transitions": [ { "id": "t1", "from": "Draft", "on": "go", "to": "Done" } ],
      "actions": [
        { "id": "a1", "on": { "transition": "t1" }, "kind": "Notify", "capabilityRef": "notify.email" }
      ],
      "guards": []
    }
    """;
    return JsonDocument.Parse(json).RootElement.Clone();
}

enum DemoState { Draft, Submitted, Approved }

enum DemoTrigger { Submit, Approve }

sealed class StaticConfirmation(WorkflowProposerIdentity proposer, WorkflowConfirmerIdentity confirmer)
    : IWorkflowConfirmationContext
{
    public WorkflowProposerIdentity EngineProposer { get; } = proposer;

    public ValueTask<WorkflowConfirmerIdentity> ResolveConfirmerAsync(CancellationToken ct = default)
        => ValueTask.FromResult(confirmer);
}

/// <summary>
/// The host-supplied durable-store seam implementation: an in-process, lock-serialized store
/// whose AdvanceAsync stages the effect and co-commits the outcome event + idempotency row +
/// instance position under one lock (the in-memory analog of the atomic-advance contract).
/// </summary>
sealed class HostInMemoryWorkflowStore : IWorkflowStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkflowInstanceRecord> _instances = new();
    private readonly Dictionary<string, WorkflowStepIdempotencyRecord> _steps = new();
    private readonly List<WorkflowEventRecord> _events = new();

    public Task<WorkflowInstanceRecord?> LoadAsync(string instanceId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_instances.TryGetValue(instanceId, out var record) ? Clone(record) : null);
        }
    }

    public Task CreateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _instances.Add(instance.Id, Clone(instance));
        }
        return Task.CompletedTask;
    }

    public Task<WorkflowStepIdempotencyRecord?> FindStepResultAsync(WorkflowStepKey key, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_steps.TryGetValue(key.Value, out var record) ? record : null);
        }
    }

    public async Task AdvanceAsync(
        WorkflowStepKey key, WorkflowEffect? effect, string resultJson, string eventType,
        string eventDataJson, string nextStep, WorkflowStatus nextStatus, CancellationToken ct = default)
    {
        // Stage the effect BEFORE committing anything — a throwing effect aborts the whole advance.
        if (effect is not null)
        {
            await effect.StageAsync(this, ct);
        }

        lock (_gate)
        {
            var instance = _instances[key.InstanceId];
            _events.Add(new WorkflowEventRecord
            {
                InstanceId = key.InstanceId,
                Seq = _events.Count(e => e.InstanceId == key.InstanceId) + 1,
                Step = key.Step,
                EventType = eventType,
                DataJson = eventDataJson,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            _steps[key.Value] = new WorkflowStepIdempotencyRecord
            {
                Key = key.Value,
                InstanceId = key.InstanceId,
                Step = key.Step,
                Iteration = key.Iteration,
                ResultJson = resultJson,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            instance.CurrentStep = nextStep;
            instance.Status = nextStatus;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public Task ParkAsync(string instanceId, string step, string reasonJson, int iteration = 0, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var instance = _instances[instanceId];
            _events.Add(new WorkflowEventRecord
            {
                InstanceId = instanceId,
                Seq = _events.Count(e => e.InstanceId == instanceId) + 1,
                Step = step,
                EventType = "Parked",
                DataJson = reasonJson,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            instance.CurrentStep = step;
            instance.Status = WorkflowStatus.Parked;
            instance.Iteration = iteration;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    private static WorkflowInstanceRecord Clone(WorkflowInstanceRecord source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DefinitionKey = source.DefinitionKey,
        DefinitionVersion = source.DefinitionVersion,
        CurrentStep = source.CurrentStep,
        Iteration = source.Iteration,
        Status = source.Status,
        StateJson = source.StateJson,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
    };
}
