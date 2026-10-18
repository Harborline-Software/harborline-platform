using Harborline.Foundation.Forms.Engine.Authoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>Opt-in callable source composition. It advertises no host capability and changes no lifecycle.</summary>
public static class LayoutFormPublicationServiceCollectionExtensions
{
    /// <summary>Registers the real read-only Layout source over the host's shared definition store.</summary>
    /// <param name="services">The existing host service collection.</param>
    public static IServiceCollection AddHarborlineLayoutFormPublicationSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IFormPublishedSurfaceSource, LayoutFormPublicationImpactSource>();
        return services;
    }
}
