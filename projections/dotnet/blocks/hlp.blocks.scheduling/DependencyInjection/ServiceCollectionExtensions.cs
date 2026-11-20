using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling.DependencyInjection;

/// <summary>Dependency-injection registrations for scheduling reservation coordination.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the reservation coordinator as a singleton.</summary>
    public static IServiceCollection AddHarborlineBlocksScheduling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IScheduleReservationCoordinator, ScheduleReservationCoordinator>();
        return services;
    }

    /// <summary>Registers planning. The host supplies IVersionedDefinitionStore and IAvailabilityRuntime.</summary>
    public static IServiceCollection AddHarborlineSchedulingPlanner(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPlanningSolver, DeterministicExhaustiveProofSolver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPlanningSolver, GreedyFirstFitSolver>());
        services.TryAddSingleton<ScheduleDefinitionCatalogue>();
        services.TryAddSingleton<SchedulePlanner>();
        return services;
    }

    /// <summary>Registers gated plan runs. The host supplies AccessProvider, RunLifecycle, FileJournalSchedulingStore and TimeProvider, and calls PlanRun.Register once on its registry.</summary>
    public static IServiceCollection AddHarborlineSchedulingPlanRuns(this IServiceCollection services)
    {
        services.AddHarborlineSchedulingPlanner();
        services.TryAddSingleton<PlanRun>();
        return services;
    }

    /// <summary>Registers due queries. The host must supply IRruleExpansionService and its authoritative KernelClock.</summary>
    public static IServiceCollection AddHarborlineSchedulingDueQueries(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDueQueueQueryService, RruleDueQueueQueryService>();
        return services;
    }
}
