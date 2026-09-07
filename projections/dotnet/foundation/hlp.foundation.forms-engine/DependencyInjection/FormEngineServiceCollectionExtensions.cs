using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Engine.Capabilities;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Engine.Security;

namespace Harborline.Foundation.Forms.Engine.DependencyInjection;

public enum FormEngineHostEnvironment { Development, Test, Production }

public static class FormEngineServiceCollectionExtensions
{
    private sealed class ProductionFieldSecurityAttestation;
    private sealed class CurrentRequestContextAttestation;
    private sealed class MacaroonCapabilityAttestation;

    private static readonly Type[] MandatoryProductionPorts =
    [
        typeof(IFormExecutionContextProvider),
        typeof(Harborline.Foundation.Forms.IFormDefinitionStore),
        typeof(Harborline.Foundation.Forms.IReuseResolver),
        typeof(Harborline.Kernel.SchemaValidation.ISchemaRegistry),
        typeof(IFormFieldSecurity),
        typeof(IFormFieldGovernanceResolver),
        typeof(IFormTenantProtectionKeyProvider),
        typeof(IFormDecryptCapabilityProvider),
        typeof(IFormSensitiveReadAudit),
        typeof(IFormSubmissionTransactionStore),
        typeof(IFormProjectionSink),
    ];

    /// <summary>
    /// Registers the fail-closed authoring-host publication boundary. Hosts should depend on
    /// <see cref="IFormDefinitionAuthoringPublisher"/> instead of calling the definition store's
    /// register and publish methods directly.
    /// </summary>
    public static IServiceCollection AddHarborlineFormsAuthoringPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFormFieldGovernanceResolver, DefaultFormFieldGovernanceResolver>();
        services.TryAddSingleton<IFormDefinitionPolicyAdmission, DefaultFormDefinitionPolicyAdmission>();
        services.TryAddScoped<IFormDefinitionAuthoringPublisher, FormDefinitionAuthoringPublisher>();
        return services;
    }

    public static IServiceCollection AddHarborlineFormsEngine(
        this IServiceCollection services,
        FormEngineHostEnvironment environment,
        Action<FormEngineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new FormEngineOptions();
        configure?.Invoke(options);
        options.Validate();
        if (environment == FormEngineHostEnvironment.Production)
        {
            var usesCurrentRequestContext = services.Any(row => row.ServiceType == typeof(CurrentRequestContextAttestation))
                || services.Any(row => row.ServiceType == typeof(IFormExecutionContextProvider)
                    && row.ImplementationType == typeof(CurrentFormExecutionContextProvider));
            var usesMacaroonCapabilities = services.Any(row => row.ServiceType == typeof(MacaroonCapabilityAttestation))
                || services.Any(row => row.ServiceType == typeof(IFormCapabilityVerifier)
                    && row.ImplementationType == typeof(MacaroonFormCapabilityVerifier));
            var conditionalPorts = new List<Type>();
            if (usesCurrentRequestContext)
            {
                conditionalPorts.AddRange([
                    typeof(IAuthenticatedActorContext),
                    typeof(IPrincipalPartyResolver),
                    typeof(IFormCapabilityBearerProvider),
                    typeof(IFormCapabilityVerifier),
                ]);
            }
            if (usesMacaroonCapabilities) conditionalPorts.Add(typeof(IFormCapabilityRootKeyProvider));
            var missing = MandatoryProductionPorts.Concat(conditionalPorts).Distinct()
                .Where(type => !services.Any(row => row.ServiceType == type))
                .Select(type => type.Name)
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Forms Engine production composition is missing mandatory providers: {string.Join(", ", missing)}.");
            var singletonRequestPorts = usesCurrentRequestContext ? new[]
            {
                typeof(IFormExecutionContextProvider),
                typeof(IAuthenticatedActorContext),
                typeof(IFormCapabilityBearerProvider),
            }.Where(type => services.Any(row => row.ServiceType == type && row.Lifetime == ServiceLifetime.Singleton))
             .Select(type => type.Name)
             .ToArray() : [];
            if (singletonRequestPorts.Length > 0)
                throw new InvalidOperationException($"Forms Engine request-bound providers cannot be singleton: {string.Join(", ", singletonRequestPorts)}.");
            if (!services.Any(row => row.ServiceType == typeof(ProductionFieldSecurityAttestation)))
                throw new InvalidOperationException("Forms Engine production composition requires an attested governance-enforcing field-security registration.");
        }
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IFormEngine, FormEngine>();
        return services;
    }

    public static IServiceCollection AddHarborlineFormsEngineTenantBoundFieldSecurity(
        this IServiceCollection services,
        FormFieldSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HostJurisdiction);
        if (services.Any(row => row.ServiceType == typeof(IFormFieldSecurity)))
            throw new InvalidOperationException("Forms Engine field security is already registered; production attestation cannot be layered over another adapter.");
        services.TryAddSingleton(options);
        services.TryAddSingleton<IFormFieldGovernanceResolver, DefaultFormFieldGovernanceResolver>();
        services.AddScoped<IFormFieldSecurity>(provider => new TenantBoundAesGcmFormFieldSecurity(
            provider.GetRequiredService<IFormTenantProtectionKeyProvider>(),
            provider.GetRequiredService<IFormDecryptCapabilityProvider>(),
            provider.GetRequiredService<IFormFieldGovernanceResolver>(),
            provider.GetRequiredService<FormFieldSecurityOptions>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddSingleton<ProductionFieldSecurityAttestation>();
        return services;
    }

    public static IServiceCollection AddHarborlineFormsEngineCurrentRequestContext(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IFormExecutionContextProvider, CurrentFormExecutionContextProvider>();
        services.TryAddSingleton<CurrentRequestContextAttestation>();
        return services;
    }

    public static IServiceCollection AddHarborlineFormsEngineMacaroonCapabilities(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IFormCapabilityVerifier, MacaroonFormCapabilityVerifier>();
        services.TryAddScoped<IFormCapabilityIssuer, MacaroonFormCapabilityIssuer>();
        services.TryAddSingleton<MacaroonCapabilityAttestation>();
        return services;
    }

    public static IServiceCollection AddHarborlineFormsEngineInMemorySubmissionStore(
        this IServiceCollection services,
        FormEngineHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (environment == FormEngineHostEnvironment.Production)
            throw new InvalidOperationException("The in-memory Forms Engine submission adapter is not permitted in Production.");
        services.TryAddSingleton<InMemoryFormSubmissionState>();
        services.TryAddSingleton<IFormSubmissionTransactionStore>(provider =>
            new InMemoryFormSubmissionStore(provider.GetRequiredService<InMemoryFormSubmissionState>()));
        return services;
    }

    public static IServiceCollection AddHarborlineFormsEngineFileSubmissionStore(
        this IServiceCollection services,
        Action<FileJournalFormSubmissionStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(row => row.ServiceType == typeof(IFormSubmissionTransactionStore)))
            throw new InvalidOperationException("A Forms Engine submission store is already registered.");
        var options = new FileJournalFormSubmissionStoreOptions();
        configure(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<IFormSubmissionTransactionStore, FileJournalFormSubmissionStore>();
        return services;
    }
}
