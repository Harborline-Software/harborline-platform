using Harborline.Blocks.BuilderDefinitions;
using System.Text.Json;

using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Foundation.Definitions;
using Harborline.Blocks.Scheduling.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Harborline.Blocks.Scheduling.Tests;

/// <summary>
/// T-491 S1, DES-0022 ruling 8: Scheduling definitions carry semantic-version heads on T-620's shared
/// versioned-definition store (scheduling-ck-20), restore moves the head (scheduling-ck-21), and the
/// engine resolves an exact pin or refuses (scheduling-eng-1). L491's integer revisions are retired.
/// </summary>
public sealed class ScheduleDefinitionCatalogueTests
{
    private const string Tenant = "tenant-a";
    private const string Id = "inspection.quarterly";
    private static readonly ScheduleDefinition Definition = Schedule("Quarterly inspection");

    private readonly InMemoryVersionedDefinitionStore _store = new(new Dictionary<DefinitionKind, DefinitionAdmission>
    {
        [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission,
    });

    private ScheduleDefinitionCatalogue Catalogue => new(_store, [new DeterministicExhaustiveProofSolver()]);

    private static DefinitionKey Key => new(Tenant, DefinitionKind.Schedules, Id);

    [Fact(DisplayName = "scheduling-ck-26: internal catalogue construction requires an explicit solver registry")]
    public void Scheduling_ck_26_CatalogueRequiresSolverRegistry()
    {
        Assert.Empty(typeof(ScheduleDefinitionCatalogue).GetConstructors());
        var constructor = Assert.Single(typeof(ScheduleDefinitionCatalogue).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
        Assert.Equal([typeof(IVersionedDefinitionStore), typeof(IEnumerable<IPlanningSolver>)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Theory(DisplayName = "scheduling-ck-26: exact draft save replay precedes changed backend admission")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduling_ck_26_DraftReplaySurvivesRegistryChange(bool removeBackend)
    {
        var definition = Definition with { ProblemClass = ScheduleProblemClass.Project,
            Solver = new("prototype.exhaustive-proof.v2", JsonSerializer.SerializeToElement(new { }), SolverGuarantee.FeasibleProof, 100) };
        var saved = await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "save");
        var changed = new ScheduleDefinitionCatalogue(_store, removeBackend ? [] : [new LimitedSolver()]);
        var replay = await changed.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "save");
        Assert.Equal(saved.Revision, replay.Revision);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() => changed.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 1, "new-save").AsTask());
        Assert.Equal(removeBackend ? "scheduling.definition.backend_unsupported" : "scheduling.definition.work_budget_unsupported", Assert.Single(refusal.Refusals).Code);
        var conflict = await Assert.ThrowsAsync<DefinitionRefusalException>(() => changed.SaveDraftAsync(Tenant, Id, "1.0.0", definition with { Name = "changed" }, 0, "save").AsTask());
        Assert.Equal("definition.replay_conflict", Assert.Single(conflict.Refusals).Code);
        Assert.Single(await _store.ListHistoryAsync(Key));
    }

    [Theory(DisplayName = "scheduling-ck-26: catalogue refuses unsupported solver bindings on save and publish")]
    [InlineData("missing", ScheduleProblemClass.Project, SolverGuarantee.FeasibleProof, 10, "scheduling.definition.backend_unsupported", "/solver/backend_id")]
    [InlineData("prototype.exhaustive-proof.v2", ScheduleProblemClass.Workforce, SolverGuarantee.FeasibleProof, 10, "scheduling.definition.problem_class_unsupported", "/solver")]
    [InlineData("prototype.exhaustive-proof.v2", ScheduleProblemClass.None, SolverGuarantee.FeasibleProof, 10, "scheduling.definition.problem_class_unsupported", "/solver")]
    [InlineData("prototype.exhaustive-proof.v2", ScheduleProblemClass.Project, SolverGuarantee.OptimalProof, 10, "scheduling.definition.guarantee_unsupported", "/solver/guarantee")]
    [InlineData("prototype.exhaustive-proof.v2", ScheduleProblemClass.Project, SolverGuarantee.FeasibleProof, -1, "scheduling.definition.work_budget_unsupported", "/solver/work_budget")]
    [InlineData("prototype.exhaustive-proof.v2", ScheduleProblemClass.Project, SolverGuarantee.FeasibleProof, 11, "scheduling.definition.work_budget_unsupported", "/solver/work_budget")]
    public async Task Scheduling_ck_26_UnsupportedSolverRefuses(string backend, ScheduleProblemClass problem,
        SolverGuarantee guarantee, int budget, string code, string pointer)
    {
        var definition = Definition with { ProblemClass = problem, Solver = new(backend, JsonSerializer.SerializeToElement(new { }), guarantee, budget) };
        var solvers = new IPlanningSolver[] { new LimitedSolver() };
        var catalogue = new ScheduleDefinitionCatalogue(_store, solvers);
        var save = await Assert.ThrowsAsync<DefinitionRefusalException>(() => catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "save").AsTask());
        // Oracle: T-491/solver-refusal-detail acceptance 3, independent of backend production values.
        var detail = code switch
        {
            "scheduling.definition.backend_unsupported" => """{"observed":"missing","required":["prototype.exhaustive-proof.v2"]}""",
            "scheduling.definition.problem_class_unsupported" => problem == ScheduleProblemClass.None
                ? """{"observed":"None","required":["JobShop","Project"]}"""
                : """{"observed":"Workforce","required":["JobShop","Project"]}""",
            "scheduling.definition.guarantee_unsupported" => """{"observed":"optimal_proof","required":["feasible_proof"]}""",
            _ => budget == -1 ? """{"observed":-1,"minimum":0,"maximum":10}""" : """{"observed":11,"minimum":0,"maximum":10}""",
        };
        var expected = JsonSerializer.Deserialize<DefinitionRefusal>("{\"Code\":\"" + code + "\",\"Pointer\":\"" + pointer + "\",\"detail\":" + detail + "}")!;
        Assert.Equal([expected], save.Refusals);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(save.Refusals)));
        using var expectedWire = JsonDocument.Parse(detail);
        Assert.True(JsonElement.DeepEquals(expectedWire.RootElement, wire.RootElement.GetProperty("detail")));
        Assert.Empty(await _store.ListHistoryAsync(Key));
        var document = new DefinitionDocument(Key, Id + "@1.0.0", "1.0.0", ScheduleDefinitionJson.Serialize(definition with { Envelope = definition.Envelope! with { Version = null } }));
        await _store.SaveDraftAsync(document, 0, "raw");
        var publish = await Assert.ThrowsAsync<DefinitionRefusalException>(() => catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "publish").AsTask());
        Assert.Equal([expected], publish.Refusals);
        Assert.Null(await _store.GetPublishedHeadAsync(Key));
    }

    [Fact(DisplayName = "scheduling-ck-26: refusal requirements are in ordinal order")]
    public async Task Scheduling_ck_26_RefusalRequirementsAreOrdinal()
    {
        var catalogue = new ScheduleDefinitionCatalogue(_store, [new OrderedTestSolver("b-backend"), new OrderedTestSolver("Z-backend"), new OrderedTestSolver("a-backend")]);
        var definition = Definition with { ProblemClass = ScheduleProblemClass.Project,
            Solver = new("missing", JsonSerializer.SerializeToElement(new { }), SolverGuarantee.FeasibleProof, 10) };
        var backend = await Assert.ThrowsAsync<DefinitionRefusalException>(() => catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "backend").AsTask());
        Assert.Equal("scheduling.definition.backend_unsupported", Assert.Single(backend.Refusals).Code);
        using var backendJson = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(backend.Refusals)));
        Assert.Equal(new[] { "Z-backend", "a-backend", "b-backend" }, backendJson.RootElement.GetProperty("detail").GetProperty("required").EnumerateArray().Select(x => x.GetString()));

        definition = definition with { Solver = new("Z-backend", JsonSerializer.SerializeToElement(new { }), SolverGuarantee.OptimalProof, 10) };
        var guarantee = await Assert.ThrowsAsync<DefinitionRefusalException>(() => catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "guarantee").AsTask());
        Assert.Equal("scheduling.definition.guarantee_unsupported", Assert.Single(guarantee.Refusals).Code);
        using var guaranteeJson = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(guarantee.Refusals)));
        Assert.Equal(new[] { "bounded_heuristic", "feasible_proof" }, guaranteeJson.RootElement.GetProperty("detail").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

    private sealed class OrderedTestSolver(string id) : IPlanningSolver
    {
        private readonly DeterministicExhaustiveProofSolver _inner = new();
        public SolverBackend Backend => _inner.Backend with { Id = id, Guarantees = new HashSet<SolverGuarantee> { SolverGuarantee.FeasibleProof, SolverGuarantee.BoundedHeuristic } };
        public string SolverId => id;
        public SolverGuarantee Guarantee => _inner.Guarantee;
        public SchedulingProposal Solve(CompiledPlanningProblem problem, int deterministicWorkBudget) => _inner.Solve(problem, deterministicWorkBudget);
    }

    [Theory(DisplayName = "scheduling-ck-26: both backend budget boundaries are admitted")]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Scheduling_ck_26_BudgetBoundaries(int budget)
    {
        var definition = Definition with { ProblemClass = ScheduleProblemClass.Project,
            Solver = new("prototype.exhaustive-proof.v2", JsonSerializer.SerializeToElement(new { }), SolverGuarantee.FeasibleProof, budget) };
        var catalogue = new ScheduleDefinitionCatalogue(_store, [new LimitedSolver()]);
        await catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "save");
        await catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "publish");
        Assert.Equal(budget, (await catalogue.ResolveAsync(await catalogue.PinHeadAsync(Tenant, Id))).Definition.Solver!.WorkBudget);
    }

    private sealed class LimitedSolver : IPlanningSolver
    {
        private readonly DeterministicExhaustiveProofSolver _inner = new();
        public SolverBackend Backend => _inner.Backend with { MaximumWorkBudget = 10 };
        public string SolverId => _inner.SolverId;
        public SolverGuarantee Guarantee => _inner.Guarantee;
        public SchedulingProposal Solve(CompiledPlanningProblem problem, int deterministicWorkBudget)
            => _inner.Solve(problem, deterministicWorkBudget);
    }

    [Fact(DisplayName = "scheduling-ck-26: a planning problem requires a solver in static admission")]
    public void Scheduling_ck_26_PlanningRequiresSolver()
    {
        var definition = Definition with { ProblemClass = ScheduleProblemClass.Project };
        var document = new DefinitionDocument(Key, Id + "@1.0.0", "1.0.0", ScheduleDefinitionJson.Serialize(definition));
        Assert.Contains(new DefinitionRefusal("scheduling.definition.solver_required", "/solver"), ScheduleDefinitionCatalogue.Admission(document, DefinitionAdmissionPhase.Author));
    }

    [Theory(DisplayName = "scheduling-ck-26: static admission locates malformed solver members")]
    [InlineData("{}", "/solver/backend_id")]
    [InlineData("{\"backend_id\":\"id\",\"guarantee\":\"feasible_proof\",\"work_budget\":1}", "/solver/parameters")]
    [InlineData("{\"backend_id\":\"id\",\"parameters\":{},\"work_budget\":1}", "/solver/guarantee")]
    [InlineData("{\"backend_id\":\"id\",\"parameters\":{},\"guarantee\":\"feasible_proof\"}", "/solver/work_budget")]
    public void Scheduling_ck_26_SolverShape(string solver, string pointer)
    {
        var json = ScheduleDefinitionJson.Serialize(Definition with { ProblemClass = ScheduleProblemClass.Project });
        json = json.Insert(json.Length - 1, ",\"solver\":" + solver);
        var document = new DefinitionDocument(Key, Id + "@1.0.0", "1.0.0", json);
        Assert.Contains(new DefinitionRefusal("scheduling.definition.solver_invalid", pointer), ScheduleDefinitionCatalogue.Admission(document, DefinitionAdmissionPhase.Author));
    }

    [Fact(DisplayName = "scheduling-ck-26: dependency injection uses the planner's registered solvers")]
    public async Task Scheduling_ck_26_HostRegistry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVersionedDefinitionStore>(_store);
        services.AddHarborlineSchedulingPlanner();
        await using var provider = services.BuildServiceProvider();
        var catalogue = provider.GetRequiredService<ScheduleDefinitionCatalogue>();
        var definition = Definition with { ProblemClass = ScheduleProblemClass.Project,
            Solver = new("prototype.exhaustive-proof.v2", JsonSerializer.SerializeToElement(new { }), SolverGuarantee.FeasibleProof, 10) };
        await catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "save");
        await catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "publish");
        Assert.Equal("prototype.exhaustive-proof.v2", (await catalogue.ResolveAsync(await catalogue.PinHeadAsync(Tenant, Id))).Definition.Solver!.BackendId);
    }

    [Fact(DisplayName = "scheduling-ck-26: a pin keeps its model and solver when the head changes")]
    public async Task Scheduling_ck_26_PinKeepsModelAndSolver()
    {
        var catalogue = new ScheduleDefinitionCatalogue(_store, [new DeterministicExhaustiveProofSolver(), new GreedyFirstFitSolver()]);
        var first = Definition with { ProblemClass = ScheduleProblemClass.Project,
            Solver = new("prototype.exhaustive-proof.v2", JsonSerializer.SerializeToElement(new { value = 1 }), SolverGuarantee.FeasibleProof, 10) };
        await catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", first, 0, "first-save");
        await catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "first-publish");
        var pin = await catalogue.PinHeadAsync(Tenant, Id);
        var second = first with { ProblemClass = ScheduleProblemClass.JobShop,
            Solver = new("prototype.greedy-first-fit.v1", JsonSerializer.SerializeToElement(new { value = 2 }), SolverGuarantee.BoundedHeuristic, 20) };
        await catalogue.SaveDraftAsync(Tenant, Id, "2.0.0", second with { Envelope = second.Envelope! with { Version = "2.0.0" } }, 2, "second-save");
        await catalogue.PublishAsync(Tenant, Id, "2.0.0", 3, "second-publish");
        var pinned = (await catalogue.ResolveAsync(pin)).Definition;
        var head = (await catalogue.ResolveAsync(await catalogue.PinHeadAsync(Tenant, Id))).Definition;
        Assert.Equal(ScheduleProblemClass.Project, pinned.ProblemClass);
        Assert.Equal("prototype.exhaustive-proof.v2", pinned.Solver!.BackendId);
        Assert.Equal(SolverGuarantee.FeasibleProof, pinned.Solver.Guarantee);
        Assert.Equal(1, pinned.Solver!.Parameters.GetProperty("value").GetInt32());
        Assert.Equal(10, pinned.Solver.WorkBudget);
        Assert.Equal(ScheduleProblemClass.JobShop, head.ProblemClass);
        Assert.Equal("prototype.greedy-first-fit.v1", head.Solver!.BackendId);
        Assert.Equal(SolverGuarantee.BoundedHeuristic, head.Solver.Guarantee);
        Assert.Equal(2, head.Solver!.Parameters.GetProperty("value").GetInt32());
        Assert.Equal(20, head.Solver.WorkBudget);
    }


    [Fact(DisplayName = "scheduling-ck-26: a solver choice survives definition serialization")]
    public async Task Scheduling_ck_26_SolverChoiceSurvivesSerialization()
    {
        var json = ScheduleDefinitionJson.Serialize(Definition);
        json = json.Insert(json.Length - 1, ",\"solver\":{\"backend_id\":\"prototype.exhaustive-proof.v2\",\"parameters\":{\"limit\":4},\"guarantee\":\"feasible_proof\",\"work_budget\":10}");
        var definition = ScheduleDefinitionJson.Deserialize(json) with { ProblemClass = ScheduleProblemClass.Project };
        using var roundTrip = JsonDocument.Parse(ScheduleDefinitionJson.Serialize(definition));
        var solver = roundTrip.RootElement.GetProperty("solver");
        Assert.Equal("prototype.exhaustive-proof.v2", solver.GetProperty("backend_id").GetString());
        Assert.Equal(4, solver.GetProperty("parameters").GetProperty("limit").GetInt32());
        Assert.Equal("feasible_proof", solver.GetProperty("guarantee").GetString());
        Assert.Equal(10, solver.GetProperty("work_budget").GetInt32());
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "solver-draft");
        await Catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "solver-publish");
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        await Catalogue.RestoreAsDraftAsync(Tenant, Id, "1.0.0", "2.0.0", 2, "solver-restore");
        await Catalogue.PublishAsync(Tenant, Id, "2.0.0", 3, "solver-publish-restored");
        foreach (var version in new[] { pin, await Catalogue.PinHeadAsync(Tenant, Id) })
        {
            var resolved = await Catalogue.ResolveAsync(version);
            using var body = JsonDocument.Parse(ScheduleDefinitionJson.Serialize(resolved.Definition));
            Assert.Equal("prototype.exhaustive-proof.v2", body.RootElement.GetProperty("solver").GetProperty("backend_id").GetString());
            Assert.Equal(4, body.RootElement.GetProperty("solver").GetProperty("parameters").GetProperty("limit").GetInt32());
            Assert.Equal("feasible_proof", body.RootElement.GetProperty("solver").GetProperty("guarantee").GetString());
            Assert.Equal(10, body.RootElement.GetProperty("solver").GetProperty("work_budget").GetInt32());
        }
    }

    [Fact(DisplayName = "scheduling-auth-18: an unadmitted module refuses at authoring")]
    public async Task UnadmittedModuleRefusesAtAuthoring()
    {
        var definition = Definition with
        {
            ProblemClass = ScheduleProblemClass.Workforce,
            Solver = new("unused", JsonSerializer.SerializeToElement(new { }), SolverGuarantee.FeasibleProof, 1),
            Modules = [new("unknown-module")],
        };
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "draft").AsTask());
        var item = Assert.Single(refusal.Refusals);
        Assert.Equal("scheduling.definition.module_unsupported", item.Code);
        Assert.Equal("/modules/0/module", item.Pointer);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Fact(DisplayName = "scheduling-auth-18: duplicate module names refuse at the duplicate authoring pointer")]
    public async Task DuplicateModulesRefuseAtAuthoring()
    {
        var definition = Definition with
        {
            ProblemClass = ScheduleProblemClass.JobShop,
            Solver = new("unused", JsonSerializer.SerializeToElement(new { }), SolverGuarantee.FeasibleProof, 1),
            Modules = [new("no_overlap"), new("no_overlap")],
        };
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "draft").AsTask());
        var item = Assert.Single(refusal.Refusals);
        Assert.Equal("scheduling.definition.module_unsupported", item.Code);
        Assert.Equal("/modules/1/module", item.Pointer);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Fact(DisplayName = "scheduling-ck-20: a schedule binds to the shared store's Schedules registry, pinned by id and semantic version")]
    public async Task ScheduleBindsToTheSharedStoreSchedulesRegistry()
    {
        await PublishAsync("1.0.0", 0);
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        Assert.Equal(new ScheduleDefinitionPin(Tenant, Id, "inspection.quarterly@1.0.0"), pin);
        var resolved = await Catalogue.ResolveAsync(pin);
        Assert.Equal(Key, resolved.Revision.Document.Key);
        Assert.Equal("1.0.0", resolved.Revision.Document.Version);
        Assert.Equal(DefinitionStatus.Published, resolved.Revision.Status);
        Assert.Same(resolved.Revision, await _store.ResolvePublishedAsync(new(Key, pin.VersionId!)));
    }

    [Fact(DisplayName = "scheduling-ck-20: the head is the highest semantic version, not the latest publication")]
    public async Task HeadIsTheHighestSemanticVersion()
    {
        await PublishAsync("1.10.0", 0);
        await PublishAsync("1.9.0", 2);
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        Assert.Equal("inspection.quarterly@1.10.0", pin.VersionId);
    }

    [Fact(DisplayName = "scheduling-eng-1: a pinned version resolves its own body after the head advances")]
    public async Task PinnedVersionResolvesAfterTheHeadAdvances()
    {
        await PublishAsync("1.0.0", 0);
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        await PublishAsync("2.0.0", 2, Schedule("Renamed"));
        var resolved = await Catalogue.ResolveAsync(pin);
        Assert.Equal("1.0.0", resolved.Revision.Document.Version);
        Assert.Equal("Quarterly inspection", resolved.Definition.Name);
        Assert.Equal("inspection.quarterly@2.0.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);
    }

    [Theory(DisplayName = "scheduling-eng-1: an unpinned schedule refuses rather than floating to the head")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task UnpinnedResolutionRefuses(string? versionId)
    {
        await PublishAsync("1.0.0", 0);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.ResolveAsync(new(Tenant, Id, versionId)).AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.Unpinned, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-eng-1: a pin to a version that was never published refuses")]
    public async Task MissingVersionRefuses()
    {
        await PublishAsync("1.0.0", 0);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@9.9.9")).AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.NotFound, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-eng-1: a pin to a draft refuses; drafts never resolve")]
    public async Task DraftPinRefuses()
    {
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Definition, 0, "draft");
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0")).AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.NotFound, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-eng-1: pinning a schedule with no published head refuses")]
    public async Task PinningWithoutAPublishedHeadRefuses()
    {
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Definition, 0, "draft");
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.PinHeadAsync(Tenant, Id).AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.NoPublishedHead, "/definitionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-20: a published version is immutable and its bytes survive an attempted edit")]
    public async Task PublishedVersionIsImmutable()
    {
        await PublishAsync("1.0.0", 0);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Schedule("Edited"), 2, "edit").AsTask());
        Assert.Equal([new DefinitionRefusal("definition.version_immutable", "/versionId")], refusal.Refusals);
        Assert.Equal(2, (await _store.ListHistoryAsync(Key)).Count);
        var resolved = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        Assert.Equal("Quarterly inspection", resolved.Definition.Name);
    }

    [Fact(DisplayName = "scheduling-ck-21: restore registers a new draft, publishing it moves the head, and history is untouched")]
    public async Task RestoreMovesTheHeadThroughANewDraft()
    {
        await PublishAsync("1.0.0", 0);
        var original = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        var restored = await Catalogue.RestoreAsDraftAsync(Tenant, Id, "1.0.0", "1.1.0", 2, "restore");
        Assert.Equal(DefinitionStatus.Draft, restored.Revision.Status);
        Assert.Equal("inspection.quarterly@1.0.0", restored.Revision.RestoredFromVersionId);
        Assert.Equal("inspection.quarterly@1.1.0", restored.Revision.Document.VersionId);
        Assert.Equal("1.1.0", restored.Definition.Envelope!.Version);
        Assert.Equal("inspection.quarterly@1.0.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);

        await Catalogue.PublishAsync(Tenant, Id, "1.1.0", 3, "publish-restored");
        Assert.Equal("inspection.quarterly@1.1.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);
        var after = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        Assert.Equal(original.Revision.Document, after.Revision.Document);
        Assert.Equal(original.Revision.Digest, after.Revision.Digest);
        Assert.Equal("1.0.0", after.Definition.Envelope!.Version);
    }

    [Theory(DisplayName = "DES-0022 ruling 8: an integer revision is not a version and refuses before anything is written")]
    [InlineData("3")]
    [InlineData("3.0")]
    [InlineData("v1.0.0")]
    public async Task IntegerRevisionVersionRefuses(string version)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, version, Definition, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal("definition.version_invalid", "/version")], refusal.Refusals);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Theory(DisplayName = "DES-0022 ruling 8: a body that still carries an integer revision refuses where it is read")]
    [InlineData("{\"revision\":3}", "/revision")]
    [InlineData("{\"revision\":\"3\"}", "/revision")]
    [InlineData("{\"version\":3}", "/version")]
    [InlineData("{\"envelope\":{\"revision\":3}}", "/envelope/revision")]
    [InlineData("{\"envelope\":{\"version\":3}}", "/envelope/version")]
    public async Task BodyIntegerRevisionRefuses(string body, string pointer)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => SaveRawAsync(body));
        Assert.Equal(DefinitionAdmissionPhase.Author, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.IntegerRevisionRetired, pointer)], refusal.Refusals);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Theory(DisplayName = "scheduling-ck-20: a body version that disagrees with the store's semantic version refuses")]
    [InlineData("{\"version\":\"2.0.0\"}", "/version")]
    [InlineData("{\"envelope\":{\"version\":\"1.0.1\"}}", "/envelope/version")]
    public async Task BodyVersionMismatchRefuses(string body, string pointer)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => SaveRawAsync(body));
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.VersionMismatch, pointer)], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-20: a body version equal to the store's semantic version is admitted")]
    public async Task MatchingBodyVersionIsAdmitted()
    {
        var saved = await _store.SaveDraftAsync(new DefinitionDocument(Key, "inspection.quarterly@1.0.0", "1.0.0",
            ScheduleDefinitionJson.Serialize(Definition)), 0, "draft");
        Assert.Contains("\"version\":\"1.0.0\"", saved.Document.BodyJson, StringComparison.Ordinal);
        Assert.Equal(DefinitionStatus.Draft, saved.Status);
        Assert.Equal(1, saved.Revision);
    }

    [Theory(DisplayName = "scheduling-ck-20: a body that is not a single JSON object refuses")]
    [InlineData("[]")]
    [InlineData("3")]
    [InlineData("\"schedule\"")]
    [InlineData("null")]
    [InlineData("{\"revision\":1,\"revision\":2}")]
    public async Task NonObjectBodyRefuses(string body)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => SaveRawAsync(body));
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")], refusal.Refusals);
    }

    [Theory(DisplayName = "scheduling-ck-20: a version id not derived from the semantic version refuses, so a bare revision cannot pose as one")]
    [InlineData("3")]
    [InlineData("inspection.quarterly@2.0.0")]
    [InlineData("other@1.0.0")]
    public async Task VersionIdNotDerivedFromTheSemanticVersionRefuses(string versionId)
    {
        var document = new DefinitionDocument(Key, versionId, "1.0.0", ScheduleDefinitionJson.Serialize(Definition));
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => _store.SaveDraftAsync(document, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.VersionIdMismatch, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-20: admission runs again at publish against the same snapshot")]
    public async Task AdmissionRunsAtPublish()
    {
        var phases = new List<DefinitionAdmissionPhase>();
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Schedules] = (document, phase) =>
            {
                phases.Add(phase);
                return ScheduleDefinitionCatalogue.Admission(document, phase);
            },
        });
        var catalogue = new ScheduleDefinitionCatalogue(store, []);
        await catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Definition, 0, "draft");
        await catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "publish");
        Assert.Equal([DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish], phases);
    }

    [Fact(DisplayName = "the version id is the definition id and semantic version")]
    public void VersionIdIsDerived()
        => Assert.Equal("inspection.quarterly@1.2.3-rc.1", ScheduleDefinitionCatalogue.VersionIdFor(Id, "1.2.3-rc.1"));

    [Fact(DisplayName = "the catalogue requires a store")]
    public void CatalogueRequiresAStore()
        => Assert.Throws<ArgumentNullException>(() => new ScheduleDefinitionCatalogue(null!, []));

    [Fact(DisplayName = "resolution requires a pin")]
    public async Task ResolutionRequiresAPin()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => Catalogue.ResolveAsync(null!).AsTask());

    [Theory(DisplayName = "scheduling-ck-20: a member spelled in another case refuses, so it cannot carry a version past the version check")]
    [InlineData("\"envelope\":{", "\"envelope\":{\"Version\":\"9.9.9\",")]
    [InlineData("\"name\":", "\"Name\":")]
    public async Task MemberInAnotherCaseRefuses(string member, string spelled)
    {
        var body = ScheduleDefinitionJson.Serialize(Definition with { Envelope = Definition.Envelope! with { Version = null } });
        Assert.Contains(member, body, StringComparison.Ordinal);
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => SaveRawAsync(body.Replace(member, spelled, StringComparison.Ordinal)));
        Assert.Equal([new DefinitionRefusal("scheduling.definition.body_invalid", "")], refused.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-8: a definition that cannot serialize refuses instead of throwing")]
    public async Task UndefinedEnumValueRefusesAtSave()
    {
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0",
            Definition with { MissedBehaviour = (ScheduleMissedBehaviour)99 }, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal("scheduling.definition.body_invalid", "")], refused.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-19: an undefined provenance element refuses instead of throwing")]
    public async Task UndefinedProvenanceRefusesAtSave()
    {
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0",
            Definition with { Envelope = Definition.Envelope! with { Provenance = default(JsonElement) } }, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal("scheduling.definition.body_invalid", "")], refused.Refusals);
    }

    private static ScheduleDefinition Schedule(string name) => new(
        new ScheduleDefinitionEnvelope(Id, "1.0.0", Tenant, Harborline.Blocks.BuilderDefinitions.LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { pack = "general" }), "standard", false, [], new DefinitionContractVersion(1, 0)),
        name,
        new ScheduleStarts(ScheduleStartKind.Form, "forms.inspection"),
        DueDate: "2027-01-01T00:00:00Z", Window: new("2027-01-01T00:00:00Z", "2027-01-31T00:00:00Z"));

    private Task<DefinitionRevision> SaveRawAsync(string body)
        => _store.SaveDraftAsync(new DefinitionDocument(Key, "inspection.quarterly@1.0.0", "1.0.0", body), 0, "draft").AsTask();

    private async Task PublishAsync(string version, long expectedRevision, ScheduleDefinition? definition = null)
    {
        var body = definition ?? Definition;
        await Catalogue.SaveDraftAsync(Tenant, Id, version, body with { Envelope = body.Envelope! with { Version = version } },
            expectedRevision, "draft-" + version);
        await Catalogue.PublishAsync(Tenant, Id, version, expectedRevision + 1, "publish-" + version);
    }
}
