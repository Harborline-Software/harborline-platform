using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Durable;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.ExecutionRuntime;
using Harborline.Foundation.Scheduling.Models;
using Harborline.Foundation.Scheduling.Services;
using Harborline.Blocks.Scheduling.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class PlanRunTests
{
    [Fact(DisplayName = "execution-runtime-cc-4: scheduling registers plan-run once without a handler")]
    public void RegistersOnce()
    {
        var registry = new RunKindRegistry();
        var registration = PlanRun.Register(registry);
        Assert.Equal("plan-run", registration.Kind.Value);
        Assert.Equal("scheduling", registration.OwningEngine);
        Assert.Null(registration.Handler);
        Assert.Equal("execution.run_kind_duplicate", Assert.Throws<ExecutionRuntimeRefusedException>(() => PlanRun.Register(registry)).Code);
    }

    [Theory(DisplayName = "execution-runtime-cc-4: all four solver answers succeed (ADR-0118 table)")]
    [InlineData(SolveStatus.Feasible)]
    [InlineData(SolveStatus.ProvenInfeasible)]
    [InlineData(SolveStatus.Partial)]
    [InlineData(SolveStatus.Indeterminate)]
    public void OutcomeTable(SolveStatus status) => Assert.Equal(RunStatus.Succeeded, PlanRun.RequireSuccessfulOutcome(status));

    [Theory(DisplayName = "execution-runtime-cc-4: denial or invalid principal starts and reads nothing")]
    [InlineData("actor", "test.denied", 1)]
    [InlineData("", "access.context_invalid", 0)]
    public async Task Denial(string principal, string reason, int calls)
    {
        using var f = await Fixture.Create();
        f.Decider.Allow = false;
        var before = f.Store.JournalLength;
        var result = await f.Run(principal: principal, pin: new("t", "absent", "absent@1.0.0"));
        Assert.False(result.Check.Allowed);
        Assert.Equal(reason, result.Check.Reason);
        Assert.Null(result.Plan);
        Assert.Equal(calls, f.Decider.Requests.Count);
        Assert.Empty(f.Availability.Requests);
        Assert.Equal(0, f.Definitions.Reads);
        Assert.Equal(before, f.Store.JournalLength);
        foreach (var status in Enum.GetValues<RunStatus>()) Assert.Empty(await f.Runs.ListByStatusAsync(new("t"), status));
    }

    [Theory(DisplayName = "execution-runtime-cc-4: real planner saves feasible and unpinned-indeterminate answers after one exact gate request")]
    [InlineData(false, SolveStatus.Feasible)]
    [InlineData(true, SolveStatus.Indeterminate)]
    public async Task RealPlanner(bool denyAll, SolveStatus expected)
    {
        using var f = await Fixture.Create();
        f.Availability.DenyAll = denyAll;
        f.Decider.BeforeDecision = () => Assert.Equal(0, f.Store.JournalLength);
        var result = await f.Run();
        var request = Assert.Single(f.Decider.Requests);
        Assert.Equal("scheduling:plan", request.Operation);
        Assert.Equal("actor", request.Principal);
        Assert.Equal("t", request.Tenant);
        Assert.Equal("t", request.Record.Tenant);
        Assert.Equal("schedule", request.Record.Kind);
        Assert.Equal("job", request.Record.Id);
        Assert.Empty(request.Record.Fields);
        Assert.Equal(DateTimeOffset.Parse("2027-01-01T00:00:00Z"), request.At);
        var plan = Assert.IsType<SchedulingPlan>(result.Plan);
        Assert.Equal(expected, plan.Proposal.Status);
        if (denyAll) Assert.Equal("UNPINNED_AVAILABILITY_READS", plan.Proposal.ReasonCode);
        else Assert.Equal(2, plan.Proposal.Assignments.Single(a => a.ActivityId == "b").StartSlot);
        Assert.Equal(3, f.Availability.Requests.Count);
        Assert.Equal(1, f.Definitions.Reads);
        var run = Assert.Single(await f.Runs.ListByStatusAsync(new("t"), RunStatus.Succeeded));
        Assert.Equal(plan.RunId, run.Id);
        Assert.Equal(RetryProfileName.ImmediateOrFail, run.RetryProfile);
        Assert.NotNull(await f.Store.GetPlanAsync("t", run.Id));
    }

    [Theory(DisplayName = "execution-runtime-cc-4: unsupported inputs preserve exceptions and record only non-secret failure codes")]
    [InlineData("module", "MODULE_UNSUPPORTED")]
    [InlineData("backend", "BACKEND_UNSUPPORTED")]
    [InlineData("binding", "RESOURCE_BINDING_UNSUPPORTED")]
    [InlineData("axis", "TIME_AXIS_UNSUPPORTED")]
    [InlineData("structure", "PROFILE_UNSUPPORTED")]
    public async Task Unsupported(string invalid, string code)
    {
        using var f = await Fixture.Create(invalid == "module");
        var facts = invalid == "structure" ? Fixture.Facts() with { Activities = [new("a", 0)] } : Fixture.Facts();
        var error = await Assert.ThrowsAsync<UnsupportedProfileException>(() => f.Run(invalid: invalid, facts: facts));
        if (invalid != "structure") Assert.StartsWith(code + ":", error.Message);
        var run = Assert.Single(await f.Runs.ListByStatusAsync(new("t"), RunStatus.DeadLettered));
        Assert.Equal(DeadLetterReason.NotRetryable, run.DeadLetter!.Reason);
        Assert.Equal(code, run.DeadLetter.LastFailure.Code);
        Assert.Empty(f.Availability.Requests);
        Assert.Null(await f.Store.GetPlanAsync("t", run.Id));
    }

    [Theory(DisplayName = "execution-runtime-cc-4: caller cancellation and unexpected errors finish the run without a Plan")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failure(bool cancel)
    {
        using var f = await Fixture.Create();
        using var cts = new CancellationTokenSource();
        Exception error = cancel ? new OperationCanceledException(cts.Token) : new InvalidOperationException("private detail");
        f.Availability.OnRead = () => { if (cancel) cts.Cancel(); throw error; };
        Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => f.Run(ct: cts.Token)));
        var run = Assert.Single(await f.Runs.ListByStatusAsync(new("t"), cancel ? RunStatus.Cancelled : RunStatus.DeadLettered));
        Assert.Empty(await f.Runs.ListByStatusAsync(new("t"), RunStatus.Running));
        Assert.Equal(RetryProfileName.ImmediateOrFail, run.RetryProfile);
        if (!cancel)
        {
            Assert.Equal("PLAN_FAILED", run.DeadLetter!.LastFailure.Code);
            Assert.Equal(DeadLetterReason.RetriesExhausted, run.DeadLetter!.Reason);
        }
        Assert.Null(await f.Store.GetPlanAsync("t", run.Id));
    }

    [Fact(DisplayName = "execution-runtime-cc-4: caused_by is tenant checked and kept on both run and Plan")]
    public async Task Cause()
    {
        using var f = await Fixture.Create();
        var cause = await f.Lifecycle.StartAsync(new(new("plan-run"), new("t"), new(RetryProfileName.ImmediateOrFail, [RetryProfileName.ImmediateOrFail])));
        var result = await f.Run(cause: cause.Id);
        Assert.Equal(cause.Id, result.Plan!.CausedBy);
        Assert.Equal(cause.Id, (await f.Runs.GetAsync(new("t"), result.Plan.RunId))!.CausedBy);
        var before = f.Store.JournalLength;
        var error = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => f.Run(cause: new(new("plan-run"), Guid.Parse("00000000-0000-0000-0000-000000000001"))));
        Assert.Equal("execution.caused_by_invalid", error.Code);
        Assert.Equal(before, f.Store.JournalLength);
    }

    private sealed class Fixture : IDisposable
    {
        public FileJournalSchedulingStore Store { get; } = new(new() { JournalPath = Path.Combine(Path.GetTempPath(), "harborline", "platform527", Guid.NewGuid() + ".journal") });
        public InMemoryRunStore Runs { get; } = new();
        public Decider Decider { get; } = new();
        public Availability Availability { get; } = new();
        public RunLifecycle Lifecycle { get; }
        public CountingDefinitions Definitions { get; }
        private PlanRun Entry { get; }
        private readonly ServiceProvider provider;
        private Fixture(ScheduleDefinitionCatalogue catalogue, IPlanningSolver solver, CountingDefinitions definitions)
        {
            Definitions = definitions;
            var registry = new RunKindRegistry(); PlanRun.Register(registry);
            var clock = new Clock();
            Lifecycle = new(registry, Runs, clock);
            var services = new ServiceCollection();
            services.AddSingleton(catalogue);
            services.AddSingleton(new SchedulePlanner(catalogue, Availability, [solver]));
            services.AddSingleton(new AccessProvider(Decider));
            services.AddSingleton(Lifecycle);
            services.AddSingleton(Store);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IAvailabilityRuntime>(Availability);
            services.AddHarborlineSchedulingPlanRuns();
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
            Entry = provider.GetRequiredService<PlanRun>();
        }
        public static async Task<Fixture> Create(bool unsupportedModule = false)
        {
            var definitions = new CountingDefinitions(new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
                { [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission }));
            var catalogue = new ScheduleDefinitionCatalogue(definitions, [new DeterministicExhaustiveProofSolver()]);
            var definition = ModuleAdmissionTests.Definition(new ScheduleConstraintModule("no_overlap")) with
            {
                Envelope = new("job", "1.0.0", "t", LayoutCascadeLayer.DomainPackage, JsonSerializer.SerializeToElement(new { pack = "general" }), "standard", false, [], new(1, 0)),
                DueDate = "2027-01-01T00:00:00Z", Window = new("2027-01-01T00:00:00Z", "2027-01-02T00:00:00Z"),
            };
            await catalogue.SaveDraftAsync("t", "job", "1.0.0", definition, 0, "draft");
            await catalogue.PublishAsync("t", "job", "1.0.0", 1, "publish");
            return new(catalogue, unsupportedModule ? new UnsupportedBackend() : new DeterministicExhaustiveProofSolver(), definitions);
        }
        public static SchedulingProfile Facts() => ModuleAdmissionTests.Profile() with
        {
            Activities = [new("a", 1), new("b", 2)],
            Resources = [new("r", new HashSet<string> { "skill" }, new HashSet<int>())],
        };
        public Task<PlanRunResult> Run(string principal = "actor", ScheduleDefinitionPin? pin = null, string invalid = "",
            SchedulingProfile? facts = null, RunId? cause = null, CancellationToken ct = default) => Entry.PlanAsync(principal,
                pin ?? new("t", "job", "job@1.0.0"), facts ?? Facts(), invalid == "backend" ? "missing" : "prototype.exhaustive-proof.v2",
                SolverGuarantee.FeasibleProof, 100, DateTimeOffset.Parse("2027-01-01T00:00:00Z"), invalid == "axis" ? TimeSpan.Zero : TimeSpan.FromHours(1),
                invalid == "binding" ? new Dictionary<string, ResourceCapacity>() : new() { ["r"] = ResourceCapacity.Exclusive(ParticipantRef.Asset("room")) }, cause, ct);
        public void Dispose() { provider.Dispose(); Store.Dispose(); }
    }
    private sealed class CountingDefinitions(IVersionedDefinitionStore inner) : IVersionedDefinitionStore
    {
        public int Reads { get; private set; }
        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default)
            => inner.ListKeysAsync(tenant, kind, cancellationToken);
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision, requestId, cancellationToken);
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default)
            => inner.ListHistoryAsync(key, cancellationToken);
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default)
            => inner.GetPublishedHeadAsync(key, cancellationToken);
        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default)
        {
            Reads++;
            return inner.ResolvePublishedAsync(binding, cancellationToken);
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2027-01-01T00:00:00Z"); }
    private sealed class Decider : IAuthorizationDecider
    {
        public bool Allow { get; set; } = true;
        public Action? BeforeDecision { get; set; }
        public List<AccessRequest> Requests { get; } = [];
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            BeforeDecision?.Invoke(); Requests.Add(request);
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, Allow, "test.denied", "", [], []));
        }
    }
    private sealed class Availability : IAvailabilityRuntime
    {
        public bool DenyAll { get; set; }
        public Action? OnRead { get; set; }
        public List<AvailabilityRequest> Requests { get; } = [];
        public Task<AvailabilityAnswer> Read(TenantId tenantId, AvailabilityRequest request, CancellationToken ct = default)
        {
            OnRead?.Invoke(); Requests.Add(request);
            var unavailable = DenyAll || request.FromUtc == DateTimeOffset.Parse("2027-01-01T01:00:00Z");
            return Task.FromResult(new AvailabilityAnswer(null, request.Resources.Select(c => new ResourceAvailabilityRead(c.Resource, [], [],
                unavailable ? 0 : 1, unavailable ? Unavailability.CapacityExhausted : Unavailability.None)).ToArray()));
        }
    }
    private sealed class UnsupportedBackend : IPlanningSolver
    {
        public SolverBackend Backend => new DeterministicExhaustiveProofSolver().Backend with { Modules = new HashSet<ConstraintModuleKind>() };
        public string SolverId => "prototype.exhaustive-proof.v2";
        public SolverGuarantee Guarantee => SolverGuarantee.FeasibleProof;
        public SchedulingProposal Solve(CompiledPlanningProblem problem, int deterministicWorkBudget) => throw new InvalidOperationException();
    }
}
