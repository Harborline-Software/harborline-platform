using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Feedback;

namespace Harborline.UIAdapters.Blazor;

/// <summary>Registers the Blazor UI services with dependency injection.</summary>
public static class HarborlineUiServiceCollectionExtensions
{
/// <summary>Adds the Harborline Blazor UI services, such as toasts and observers, to the service collection.</summary>
    public static IServiceCollection AddHarborlineUiAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<MediaQueryObserver>();
        services.TryAddScoped<IMediaQueryObserver>(provider => provider.GetRequiredService<MediaQueryObserver>());
        services.TryAddScoped<OutsidePointerObserver>();
        services.TryAddScoped<IOutsidePointerObserver>(provider => provider.GetRequiredService<OutsidePointerObserver>());
        services.TryAddScoped<ScrollAffordanceObserver>();
        services.TryAddScoped<IScrollAffordanceObserver>(provider => provider.GetRequiredService<ScrollAffordanceObserver>());
        services.TryAddScoped<ToastService>();
        services.TryAddScoped<IToastService>(provider => provider.GetRequiredService<ToastService>());
        return services;
    }
}
