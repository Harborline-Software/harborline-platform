using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
}
