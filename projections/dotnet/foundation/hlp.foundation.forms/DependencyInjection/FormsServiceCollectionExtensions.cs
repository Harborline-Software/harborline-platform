using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Drafts;

namespace Harborline.Foundation.Forms.DependencyInjection;

public static class FormsServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryFormDefinitionStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
        return services;
    }

    public static IServiceCollection AddInMemoryReusableUnitStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IReusableUnitStore, InMemoryReusableUnitStore>();
        services.TryAddSingleton<IReuseResolver, ReuseResolver>();
        return services;
    }

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
