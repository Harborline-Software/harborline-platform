using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Harborline.Blocks.ActivityTimeline;

public static class ActivityTimelineServiceCollectionExtensions
{
    public static IServiceCollection AddActivityTimeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<InMemoryActivityEntrySource>();
        services.TryAddSingleton<IActivityEntrySource>(provider => provider.GetRequiredService<InMemoryActivityEntrySource>());
        return services;
    }
}
