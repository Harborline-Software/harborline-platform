using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Blocks.Scheduling.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.Scheduling.Models;
using Harborline.Foundation.Scheduling.Services;
using System.Text.Json;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class SchedulePlannerTests
{
    [Fact(DisplayName = "scheduling-eng-17, scheduling-bound-6: the pinned catalogue planner reads each candidate's supply only from the shared runtime")]
    public async Task PlannerReadsSharedRuntimeAndIgnoresCallerSupply()
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission,
        });
        var catalogue = new ScheduleDefinitionCatalogue(store);
        var definition = ModuleAdmissionTests.Definition() with
        {
            Envelope = new("job", "1.0.0", "t", LayoutCascadeLayer.DomainPackage,
                JsonSerializer.SerializeToElement(new { pack = "general" }), "standard", false, [], new(1, 0)),
            DueDate = "2027-01-01T00:00:00Z",
            Window = new("2027-01-01T00:00:00Z", "2027-01-02T00:00:00Z"),
        };
        await catalogue.SaveDraftAsync("t", "job", "1.0.0", definition, 0, "draft");
        await catalogue.PublishAsync("t", "job", "1.0.0", 1, "publish");
        var runtime = new Availability();
        var services = new ServiceCollection();
        services.AddSingleton(catalogue);
        services.AddSingleton<IAvailabilityRuntime>(runtime);
        services.AddHarborlineSchedulingPlanner();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var planner = provider.GetRequiredService<SchedulePlanner>();
        var facts = ModuleAdmissionTests.Profile() with
        {
            Activities = [new("a", 1), new("b", 2)],
            Resources = [new("r", new HashSet<string> { "skill" }, new HashSet<int>())],
        };
        var bindings = new Dictionary<string, ResourceCapacity> { ["r"] = ResourceCapacity.Exclusive(ParticipantRef.Asset("room")) };
        var result = await planner.PlanAsync(new("t", "job", "job@1.0.0"), facts,
            "prototype.exhaustive-proof.v2", SolverGuarantee.FeasibleProof, 100, Epoch, TimeSpan.FromHours(1), bindings);
        Assert.Equal(SolveStatus.Feasible, result.Status);
        Assert.Equal(2, result.Assignments.Single(item => item.ActivityId == "b").StartSlot);
        Assert.Equal(3, runtime.Requests.Count);
        Assert.Equal(new TimeSpan?[] { TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(2) },
            runtime.Requests.Select(request => request.ToUtc - request.FromUtc));
        Assert.All(runtime.Tenants, tenant => Assert.Equal("t", tenant.Value));
        runtime.Refuse = true;
        var refusal = await Assert.ThrowsAsync<UnsupportedProfileException>(() => planner.PlanAsync(new("t", "job", "job@1.0.0"), facts,
            "prototype.exhaustive-proof.v2", SolverGuarantee.FeasibleProof, 100, Epoch, TimeSpan.FromHours(1), bindings));
        Assert.Contains("AVAILABILITY_WINDOW_INVERTED", refusal.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "scheduling-eng-17, scheduling-bound-6: disjoint fixed windows do not materialize intervening supply slots")]
    public async Task PlannerCompilesDisjointWindowsWithoutDenseSupplyDomain()
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission,
        });
        var catalogue = new ScheduleDefinitionCatalogue(store);
        var definition = ModuleAdmissionTests.Definition() with
        {
            Envelope = new("job", "1.0.0", "t", LayoutCascadeLayer.DomainPackage,
                JsonSerializer.SerializeToElement(new { pack = "general" }), "standard", false, [], new(1, 0)),
            DueDate = "2096-01-01T00:00:00Z",
            Window = new("2027-01-01T00:00:00Z", "2096-01-01T00:00:00Z"),
        };
        await catalogue.SaveDraftAsync("t", "job", "1.0.0", definition, 0, "draft");
        await catalogue.PublishAsync("t", "job", "1.0.0", 1, "publish");
        var runtime = new Availability();
        var planner = new SchedulePlanner(catalogue, runtime, [new DeterministicExhaustiveProofSolver()]);
        var facts = ModuleAdmissionTests.Profile() with
        {
            Activities = [new("a", 10), new("b", 1)],
            TimeWindows = [new("a", 0, 0), new("b", 2_147_483_645, 2_147_483_645)],
            Resources = [new("r", new HashSet<string> { "skill" }, new HashSet<int>())],
        };
        var bindings = new Dictionary<string, ResourceCapacity> { ["r"] = ResourceCapacity.Exclusive(ParticipantRef.Asset("room")) };
        var result = await planner.PlanAsync(new("t", "job", "job@1.0.0"), facts,
            "prototype.exhaustive-proof.v2", SolverGuarantee.FeasibleProof, 100, Epoch, TimeSpan.FromSeconds(1), bindings);
        Assert.Equal(SolveStatus.Feasible, result.Status);
        Assert.Equal(new[] { 0, 2_147_483_645 }, result.Assignments.OrderBy(item => item.ActivityId).Select(item => item.StartSlot));
        Assert.Equal(2, runtime.Requests.Count);
        Assert.Equal(new DateTimeOffset?[] { Epoch, Epoch.AddSeconds(2_147_483_645) }, runtime.Requests.Select(request => request.FromUtc));
        Assert.Equal(new TimeSpan?[] { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1) },
            runtime.Requests.Select(request => request.ToUtc - request.FromUtc));
    }

    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2027-01-01T00:00:00Z");
    private sealed class Availability : IAvailabilityRuntime
    {
        public List<AvailabilityRequest> Requests { get; } = [];
        public List<TenantId> Tenants { get; } = [];
        public bool Refuse { get; set; }
        public Task<AvailabilityAnswer> Read(TenantId tenantId, AvailabilityRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            Tenants.Add(tenantId);
            if (Refuse) return Task.FromResult(new AvailabilityAnswer(new(AvailabilityRefusal.WindowInverted, "to"), []));
            return Task.FromResult(new AvailabilityAnswer(null, request.Resources.Select(capacity => new ResourceAvailabilityRead(
                capacity.Resource, [], [], request.FromUtc == Epoch.AddHours(1) ? 0 : 1,
                request.FromUtc == Epoch.AddHours(1) ? Unavailability.CapacityExhausted : Unavailability.None)).ToArray()));
        }
    }
}
