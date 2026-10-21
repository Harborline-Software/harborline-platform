using Microsoft.Extensions.DependencyInjection;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.DependencyInjection;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// N1 (K9 F4): under dependency injection the definition store and the form engine read one declaration
/// of what a form's submissions create. A sink whose registered implementation declares is registered as the
/// <see cref="IFormRecordCreationDeclarations"/> port with the sink's lifetime, so the DI-built store refuses at publication
/// exactly what the engine would refuse at submit. An explicit port is kept, and a sink that declares nothing adds nothing.
/// Oracles are literal codes and the identity of the registered instances.
/// </summary>
public sealed class FormRecordCreationCompositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "N1: a declaring sink is the one declaration the DI-built definition store and engine read, so publication refuses a targetless declared form")]
    public async Task DeclaringSinkIsTheOneDeclarationUnderDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<IFormProjectionSink, DeclaringSink>();
        services.AddInMemoryFormDefinitionStore();
        services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Test);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var declarations = provider.GetRequiredService<IFormRecordCreationDeclarations>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form()));

        Assert.Same(provider.GetRequiredService<IFormProjectionSink>(), declarations);
        Assert.Equal(ServiceLifetime.Singleton,
            Assert.Single(services, row => row.ServiceType == typeof(IFormRecordCreationDeclarations)).Lifetime);
        Assert.Equal(("form.record_type.target_required", "/record_type_id"), (refusal.Code, refusal.Target));
        Assert.Equal([("tenant:acme", "capture")], ((DeclaringSink)declarations).Asked);
    }

    [Fact(DisplayName = "N1: an explicitly registered declaration port is kept and the sink is not registered over it")]
    public void ExplicitPortIsKept()
    {
        var services = new ServiceCollection();
        var explicitPort = new DeclaringSink();
        services.AddSingleton<IFormRecordCreationDeclarations>(explicitPort);
        services.AddSingleton<IFormProjectionSink, DeclaringSink>();
        services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Test);
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, row => row.ServiceType == typeof(IFormRecordCreationDeclarations));
        Assert.Same(explicitPort, provider.GetRequiredService<IFormRecordCreationDeclarations>());
    }

    [Fact(DisplayName = "N1: a sink that declares nothing registers no declaration port")]
    public void NonDeclaringSinkRegistersNothing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFormProjectionSink, PlainSink>();
        services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Test);

        Assert.DoesNotContain(services, row => row.ServiceType == typeof(IFormRecordCreationDeclarations));
    }

    private static FormDefinition Form()
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId("capture"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: new TenantId("tenant:acme"),
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:record-creation"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: [new FormSection("sec", InternationalizedText.FromInvariant("sec"), ["name"], access)],
                Rules: []),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now,
            SubmitGate: new SubmitGate(Role: RoleReference.Domain("inspector")),
            RecordTypeId: null);
    }

    // A host sink that creates Records records and declares the type it creates for the "capture" form.
    private sealed class DeclaringSink : IFormProjectionSink, IFormRecordCreationDeclarations
    {
        public List<(string Tenant, string FormId)> Asked { get; } = [];

        public ValueTask<IReadOnlyCollection<string>> GetCreatedRecordTypesAsync(TenantId tenant, FormDefinitionId formId, CancellationToken cancellationToken = default)
        {
            Asked.Add((tenant.Value, formId.Value));
            return ValueTask.FromResult<IReadOnlyCollection<string>>(formId.Value == "capture" ? ["notes.entry"] : []);
        }

        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormProjectionDeliveryResult([]));
    }

    private sealed class PlainSink : IFormProjectionSink
    {
        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormProjectionDeliveryResult([]));
    }

    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
