using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Harborline.Blocks.ActivityTimeline;

/// <summary>Dependency-injection registration for the activity timeline source.</summary>
public static class ActivityTimelineServiceCollectionExtensions
{
    /// <summary>Registers the in-memory source as both its concrete and interface service.</summary>
    public static IServiceCollection AddActivityTimeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<InMemoryActivityEntrySource>();
        services.TryAddSingleton<IActivityEntrySource>(provider => provider.GetRequiredService<InMemoryActivityEntrySource>());
        return services;
    }
}
