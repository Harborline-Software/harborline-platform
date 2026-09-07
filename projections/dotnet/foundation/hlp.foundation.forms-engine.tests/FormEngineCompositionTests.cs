using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class FormEngineCompositionTests
{
    [Fact]
    public void ProductionComposition_MissingProtectionGovernanceOrAuditProvider_FailsStartup()
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Production));
        Assert.Contains(nameof(IFormExecutionContextProvider), error.Message, StringComparison.Ordinal);
        Assert.Contains("IFormFieldSecurity", error.Message, StringComparison.Ordinal);
        Assert.Contains("IFormProjectionSink", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_memory_submission_adapter_refuses_production()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddHarborlineFormsEngineInMemorySubmissionStore(FormEngineHostEnvironment.Production));
    }

    [Fact]
    public void ProductionComposition_MissingActorOrCapabilityProvider_FailsStartup()
    {
        var services = new ServiceCollection();
        services.AddHarborlineFormsEngineCurrentRequestContext();
        services.AddHarborlineFormsEngineMacaroonCapabilities();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Production));

        Assert.Contains("IAuthenticatedActorContext", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IPrincipalPartyResolver", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IFormCapabilityBearerProvider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IFormCapabilityRootKeyProvider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IFormDecryptCapabilityProvider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionComposition_JobScopedRecoveryContext_DoesNotRequireRequestBearerPorts()
    {
        var services = GovernanceEnforcementTests.ProductionPortShell();
        services.RemoveAll<IFormExecutionContextProvider>();
        services.RemoveAll<Harborline.Foundation.Authorization.IAuthenticatedActorContext>();
        services.RemoveAll<Harborline.Foundation.Authorization.IPrincipalPartyResolver>();
        services.RemoveAll<Capabilities.IFormCapabilityBearerProvider>();
        services.RemoveAll<Capabilities.IFormCapabilityRootKeyProvider>();
        services.RemoveAll<Capabilities.IFormCapabilityVerifier>();
        services.AddScoped<IFormExecutionContextProvider, JobExecutionContextProvider>();
        services.AddSingleton<Security.IFormTenantProtectionKeyProvider, FormFieldSecurityHarness.FixedTenantKeyProvider>();
        services.AddSingleton<Security.IFormDecryptCapabilityProvider>(
            new FormFieldSecurityHarness.StubDecryptCapabilityProvider(true));
        services.AddHarborlineFormsEngineTenantBoundFieldSecurity(
            new Security.FormFieldSecurityOptions { HostJurisdiction = "US" });

        services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Production);

        Assert.Contains(services, row => row.ServiceType == typeof(IFormEngine));
    }

    [Fact]
    public void ProductionComposition_SingletonRequestIdentityOrBearer_FailsStartup()
    {
        var services = GovernanceEnforcementTests.ProductionPortShell();
        services.AddHarborlineFormsEngineCurrentRequestContext();
        services.AddSingleton<Security.IFormTenantProtectionKeyProvider, FormFieldSecurityHarness.FixedTenantKeyProvider>();
        services.AddSingleton<Security.IFormDecryptCapabilityProvider>(
            new FormFieldSecurityHarness.StubDecryptCapabilityProvider(true));
        services.AddHarborlineFormsEngineTenantBoundFieldSecurity(
            new Security.FormFieldSecurityOptions { HostJurisdiction = "US" });
        services.RemoveAll<IFormExecutionContextProvider>();
        services.RemoveAll<Harborline.Foundation.Authorization.IAuthenticatedActorContext>();
        services.RemoveAll<Capabilities.IFormCapabilityBearerProvider>();
        services.AddSingleton<IFormExecutionContextProvider>(_ => throw new NotSupportedException());
        services.AddSingleton<Harborline.Foundation.Authorization.IAuthenticatedActorContext>(_ => throw new NotSupportedException());
        services.AddSingleton<Capabilities.IFormCapabilityBearerProvider>(_ => throw new NotSupportedException());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Production));

        Assert.Contains(nameof(IFormExecutionContextProvider), exception.Message, StringComparison.Ordinal);
        Assert.Contains("IAuthenticatedActorContext", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IFormCapabilityBearerProvider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_memory_submission_adapter_is_available_for_tests()
    {
        var services = new ServiceCollection();
        services.AddHarborlineFormsEngineInMemorySubmissionStore(FormEngineHostEnvironment.Test);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<Persistence.InMemoryFormSubmissionStore>(provider.GetRequiredService<Persistence.IFormSubmissionTransactionStore>());
    }

    [Fact]
    public void Current_request_context_and_macaroon_adapters_are_scoped()
    {
        var services = new ServiceCollection();
        services.AddHarborlineFormsEngineCurrentRequestContext();
        services.AddHarborlineFormsEngineMacaroonCapabilities();

        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, row => row.ServiceType == typeof(IFormExecutionContextProvider)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, row => row.ServiceType.Name == "IFormCapabilityVerifier").Lifetime);
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, row => row.ServiceType.Name == "IFormCapabilityIssuer").Lifetime);
    }

    private sealed class JobExecutionContextProvider : IFormExecutionContextProvider
    {
        public ValueTask<FormExecutionScope> GetRequiredAsync(
            FormEngineAction action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
