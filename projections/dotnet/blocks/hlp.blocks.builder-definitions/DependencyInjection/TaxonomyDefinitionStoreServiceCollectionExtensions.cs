using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Harborline.Blocks.BuilderDefinitions.DependencyInjection;

/// <summary>DI registration for <see cref="TaxonomyDefinitionStore"/> (T-493 S7).</summary>
public static class TaxonomyDefinitionStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="TaxonomyDefinitionStore"/> over the host's already-registered
    /// <see cref="IVersionedDefinitionStore"/>. Idempotent via <c>TryAdd</c>. The host builds that
    /// store with <see cref="TaxonomyDefinitionStore.Admission"/> bound to <see cref="DefinitionKind.Taxonomy"/>;
    /// without it every Taxonomy operation refuses <c>definition.registry_unknown</c>.
    /// </summary>
    public static IServiceCollection AddTaxonomyDefinitionStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(provider => new TaxonomyDefinitionStore(provider.GetRequiredService<IVersionedDefinitionStore>()));
        return services;
    }
}
