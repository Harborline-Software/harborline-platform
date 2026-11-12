using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling.Models;
using Harborline.Foundation.Scheduling.Services;

namespace Harborline.Blocks.Scheduling.Planning;

/// <summary>Resolves a published schedule, admits its modules, and reads candidate supply through the shared runtime.</summary>
public sealed class SchedulePlanner
{
    private readonly ScheduleDefinitionCatalogue _catalogue;
    private readonly IAvailabilityRuntime _availability;
    private readonly IReadOnlyDictionary<string, IPlanningSolver> _backends;

    /// <summary>The host supplies the catalogue, shared availability runtime, and registered solver backends.</summary>
    public SchedulePlanner(ScheduleDefinitionCatalogue catalogue, IAvailabilityRuntime availability, IEnumerable<IPlanningSolver> backends)
    {
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _backends = (backends ?? throw new ArgumentNullException(nameof(backends))).ToDictionary(solver => solver.Backend.Id, StringComparer.Ordinal);
    }

    /// <summary>Plans on an explicit time axis. Runtime reads replace caller-supplied resource availability.</summary>
    public async Task<SchedulingProposal> PlanAsync(ScheduleDefinitionPin pin, SchedulingProfile facts,
        string backendId, SolverGuarantee guarantee, int deterministicWorkBudget, DateTimeOffset epoch,
        TimeSpan slotDuration, IReadOnlyDictionary<string, ResourceCapacity> resources, CancellationToken cancellationToken = default)
    {
        var definition = (await _catalogue.ResolveAsync(pin, cancellationToken).ConfigureAwait(false)).Definition;
        if (!_backends.TryGetValue(backendId, out var backend))
            throw new UnsupportedProfileException($"BACKEND_UNSUPPORTED: {backendId}; required: a registered backend.");
        var modules = FiniteCandidateCompiler.AdmitModules(definition, backend.Backend, guarantee, deterministicWorkBudget);
        if (slotDuration <= TimeSpan.Zero)
            throw new UnsupportedProfileException("TIME_AXIS_UNSUPPORTED: required: a positive slot duration.");
        if (facts.Resources.Any(resource => !resources.TryGetValue(resource.Id, out var capacity)
                || capacity.Kind != CapacityKind.Exclusive || !capacity.Buffer.IsNone)
            || resources.Count != facts.Resources.Count || resources.Values.Select(capacity => capacity.Resource).Distinct().Count() != resources.Count)
            throw new UnsupportedProfileException("RESOURCE_BINDING_UNSUPPORTED: required: one distinct exclusive resource without buffers for every resource id.");

        // The shared runtime filters candidate intervals; caller supply must not prune them here.
        var problem = new FiniteCandidateCompiler().Compile(facts, modules, useCallerAvailability: false);
        var candidates = new Dictionary<string, IReadOnlyList<AssignmentCandidate>>(StringComparer.Ordinal);
        foreach (var (activityId, placements) in problem.CandidatesByActivity)
        {
            var available = new List<AssignmentCandidate>();
            foreach (var candidate in placements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capacities = candidate.ResourceIds.Select(id => resources[id]).ToArray();
                var request = new AvailabilityRequest(epoch.AddTicks(checked(slotDuration.Ticks * candidate.StartSlot)),
                    epoch.AddTicks(checked(slotDuration.Ticks * candidate.EndSlotExclusive)), capacities);
                var answer = await _availability.Read(new TenantId(pin.Tenant), request, cancellationToken).ConfigureAwait(false);
                if (answer.Refusal is { } refusal)
                    throw new UnsupportedProfileException($"AVAILABILITY_UNSUPPORTED: {refusal.Code} at {refusal.Pointer}.");
                if (answer.Resources.Count != capacities.Length || !answer.Resources.Select(read => read.Resource).ToHashSet()
                        .SetEquals(capacities.Select(capacity => capacity.Resource)))
                    throw new UnsupportedProfileException("AVAILABILITY_UNSUPPORTED: required: exactly one answer per requested resource.");
                if (answer.Available) available.Add(candidate);
            }
            candidates.Add(activityId, available);
        }
        return backend.Solve(problem with { CandidatesByActivity = candidates }, deterministicWorkBudget);
    }
}
