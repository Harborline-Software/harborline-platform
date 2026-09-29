using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Drafts;

namespace Harborline.Foundation.Forms.DependencyInjection;

/// <summary>Registers the in-memory Forms stores and services; each call keeps any registration the host already made.</summary>
public static class FormsServiceCollectionExtensions
{
    /// <summary>Adds a singleton in-memory <see cref="IFormDefinitionStore"/>; its contents do not survive the process.</summary>
    public static IServiceCollection AddInMemoryFormDefinitionStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
        return services;
    }

    /// <summary>Adds a singleton in-memory <see cref="IReusableUnitStore"/> and the <see cref="IReuseResolver"/> over it.</summary>
    public static IServiceCollection AddInMemoryReusableUnitStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IReusableUnitStore, InMemoryReusableUnitStore>();
        services.TryAddSingleton<IReuseResolver, ReuseResolver>();
        return services;
    }

    /// <summary>
    /// Adds in-memory draft and pre-auth capture stores plus the scoped draft services, which resolve the actor through
    /// <see cref="AuthenticatedFormsActorScope"/> and use the registered <see cref="TimeProvider"/> or the system clock.
    /// </summary>
    public static IServiceCollection AddInMemorySubmissionDrafts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISubmissionDraftStore, InMemorySubmissionDraftStore>();
        services.TryAddSingleton<IPreAuthCaptureBuffer, InMemoryPreAuthCaptureBuffer>();
        services.TryAddScoped<IFormsActorScope, AuthenticatedFormsActorScope>();
        services.TryAddScoped<ISubmissionDraftService>(provider => new SubmissionDraftService(
            provider.GetRequiredService<IFormsActorScope>(),
            provider.GetRequiredService<ISubmissionDraftStore>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddScoped<IPreAuthCaptureService, PreAuthCaptureService>();
        return services;
    }
}
