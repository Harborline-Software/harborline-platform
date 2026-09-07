using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Capabilities;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Models;
using KernelSchemaRegistry = Harborline.Kernel.SchemaValidation.ISchemaRegistry;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class GovernanceEnforcementTests
{
    [Fact]
    public async Task Save_EncryptsClassifiedFieldValue_AtRest_NeverCleartext()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await harness.SubmitAsync(
            """{"classifiedSsn":"123-45-6789","displayName":"Alice"}""", "classified");

        using var stored = await harness.StoredAsync(receipt.InstanceId);
        Assert.Equal(1, stored.RootElement.GetProperty("classifiedSsn").GetProperty("$harborlineProtected").GetInt32());
        Assert.DoesNotContain("123-45-6789", stored.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("Alice", stored.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Read_AfterEngineRestart_RedactsClassifiedValue_ClearFieldReadable()
    {
        var writer = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await writer.SubmitAsync(
            """{"classifiedSsn":"123-45-6789","displayName":"Alice"}""", "restart");
        var reader = await FormFieldSecurityHarness.CreateAsync(
            state: writer.State,
            keys: writer.Keys);

        var view = await reader.Engine.RenderAsync(reader.Definition.Id, receipt.InstanceId);

        var fields = Assert.Single(view.Sections).Fields;
        var ssn = Assert.Single(fields, field => field.Name == "classifiedSsn");
        Assert.True(ssn.IsSensitive);
        Assert.False(ssn.IsReadable);
        Assert.False(ssn.Value.HasValue);
        var displayName = Assert.Single(fields, field => field.Name == "displayName");
        Assert.True(displayName.IsReadable);
        Assert.Equal("Alice", displayName.Value.Value.GetString());
    }

    [Fact]
    public async Task Read_ReadableEncryptedField_Withheld_NotCiphertextEnvelope()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await harness.SubmitAsync("""{"cardNumber":"4111111111111111"}""", "pci-withheld");

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var card = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "cardNumber");
        Assert.True(card.IsSensitive);
        Assert.False(card.IsReadable);
        Assert.False(card.Value.HasValue);
    }

    [Fact]
    public async Task Read_ClassifiedField_EmitsSensitiveReadAudit()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await harness.SubmitAsync("""{"classifiedSsn":"123-45-6789"}""", "pii-audit");
        harness.Audit.Appends.Clear();

        await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var decision = Assert.Single(harness.Audit.Appends).Event;
        Assert.Equal("classifiedSsn", decision.FieldName);
        Assert.Equal(FormSensitiveReadAuditOutcome.Withheld, decision.Outcome);
        Assert.Equal("policy-redacted", decision.Reason);
    }

    [Fact]
    public async Task Read_EncryptedField_WithDecryptPermission_DecryptsRendersAndAudits()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(decryptPermission: true);
        var receipt = await harness.SubmitAsync("""{"cardNumber":"4111111111111111"}""", "pci-grant");
        harness.Audit.Appends.Clear();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var card = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "cardNumber");
        Assert.True(card.IsReadable);
        Assert.Equal("4111111111111111", card.Value.Value.GetString());
        Assert.Equal(FormSensitiveReadAuditOutcome.Granted, Assert.Single(harness.Audit.Appends).Event.Outcome);
    }

    [Fact]
    public async Task Read_EncryptedField_WithoutDecryptPermission_WithheldExactlyAsToday()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await harness.SubmitAsync("""{"cardNumber":"4111111111111111"}""", "pci-denied");
        harness.Audit.Appends.Clear();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var card = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "cardNumber");
        Assert.False(card.IsReadable);
        Assert.False(card.Value.HasValue);
        Assert.Empty(harness.Audit.Appends);
    }

    [Fact]
    public async Task Read_RedactedClass_DecryptPermissionDoesNotOverrideRedaction()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(decryptPermission: true);
        var receipt = await harness.SubmitAsync("""{"classifiedSsn":"123-45-6789"}""", "redaction-wins");

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var ssn = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "classifiedSsn");
        Assert.False(ssn.IsReadable);
        Assert.False(ssn.Value.HasValue);
        Assert.DoesNotContain(harness.Capabilities.Purposes, purpose => purpose == FormEnginePermissions.DecryptOnRenderPurpose);
    }

    [Fact]
    public async Task Save_ResidencyEligibleJurisdiction_Encrypts()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(hostJurisdiction: "US");

        var receipt = await harness.SubmitAsync("""{"caseNote":"controlled"}""", "residency-us");

        using var stored = await harness.StoredAsync(receipt.InstanceId);
        Assert.Equal(1, stored.RootElement.GetProperty("caseNote").GetProperty("$harborlineProtected").GetInt32());
        Assert.DoesNotContain("controlled", stored.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_ResidencyIneligibleJurisdiction_RefusedFailClosed()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(hostJurisdiction: "EU");

        var exception = await Assert.ThrowsAsync<FormEngineGovernanceException>(async () =>
            await harness.SubmitAsync("""{"caseNote":"controlled"}""", "residency-eu"));

        Assert.Equal("caseNote", exception.Field);
        Assert.Equal(FormGovernanceRefusal.ResidencyDenied, exception.Reason);
        Assert.Equal((0, 0, 0, 0), await harness.EngineHarness.Store.CountsAsync());
    }

    [Fact]
    public async Task Save_ClassifiedFieldWithNoResolvablePolicy_RefusedFailClosed()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();

        var exception = await Assert.ThrowsAsync<FormEngineGovernanceException>(async () =>
            await harness.SubmitAsync("""{"tagged":"value"}""", "unresolved-policy"));

        Assert.Equal("tagged", exception.Field);
        Assert.Equal(FormGovernanceRefusal.PolicyUnresolved, exception.Reason);
        Assert.Equal((0, 0, 0, 0), await harness.EngineHarness.Store.CountsAsync());
    }

    [Fact]
    public async Task Save_SubjectScopedClassWithoutSubjectSeam_RefusedFailClosed()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();

        var exception = await Assert.ThrowsAsync<FormEngineGovernanceException>(async () =>
            await harness.SubmitAsync("""{"diagnosis":"redacted"}""", "missing-subject"));

        Assert.Equal("diagnosis", exception.Field);
        Assert.Equal(FormGovernanceRefusal.SubjectRequired, exception.Reason);
        Assert.Equal((0, 0, 0, 0), await harness.EngineHarness.Store.CountsAsync());
    }

    [Fact]
    public async Task Ctor_RequireGovernanceEnforcement_WithoutSeam_ThrowsAtConstruction()
    {
        var services = ProductionPortShell();
        services.AddSingleton<IFormFieldSecurity>(new FormEngineOrchestrationTests.RecordingSecurity());
        services.AddSingleton<IFormFieldGovernanceResolver, DefaultFormFieldGovernanceResolver>();
        services.AddSingleton<IFormTenantProtectionKeyProvider, FormFieldSecurityHarness.FixedTenantKeyProvider>();
        services.AddSingleton<IFormDecryptCapabilityProvider>(
            new FormFieldSecurityHarness.StubDecryptCapabilityProvider(true));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Production));
        Assert.Contains("attested", exception.Message, StringComparison.Ordinal);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Ctor_RequireGovernanceEnforcement_WithSeam_Constructs()
    {
        var services = ProductionPortShell();
        services.AddSingleton<IFormTenantProtectionKeyProvider, FormFieldSecurityHarness.FixedTenantKeyProvider>();
        services.AddSingleton<IFormDecryptCapabilityProvider>(
            new FormFieldSecurityHarness.StubDecryptCapabilityProvider(true));
        services.AddHarborlineFormsEngineTenantBoundFieldSecurity(
            new FormFieldSecurityOptions { HostJurisdiction = "US" });
        services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Production);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<TenantBoundAesGcmFormFieldSecurity>(
            scope.ServiceProvider.GetRequiredService<IFormFieldSecurity>());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IFormEngine));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Submit_ReusedFieldLackingItsWriteRole_IsDenied_NotSilentlyWritable()
    {
        var harness = await CreateReuseHarnessAsync();

        var exception = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.SubmitAsync(
                """{"title":"Widget","restrictedReused":"tamper"}""",
                "reused-denied"));

        Assert.Contains(exception.Errors, error =>
            error.JsonPointer == "/restrictedReused" &&
            error.Code.HasValue &&
            error.Code.Value == "form.engine.denied");
        Assert.Equal((0, 0, 0, 0), await harness.EngineHarness.Store.CountsAsync());
    }

    [Fact]
    public async Task Submit_ReusedResidencyClassifiedField_IsRoutedThroughStorePep_FailsClosedOffResidency()
    {
        var harness = await CreateReuseHarnessAsync(hostJurisdiction: "EU");

        var exception = await Assert.ThrowsAsync<FormEngineGovernanceException>(async () =>
            await harness.SubmitAsync(
                """{"caseNoteReused":"controlled"}""",
                "reused-residency"));

        Assert.Equal("caseNoteReused", exception.Field);
        Assert.Equal(FormGovernanceRefusal.ResidencyDenied, exception.Reason);
        Assert.Equal((0, 0, 0, 0), await harness.EngineHarness.Store.CountsAsync());
    }

    [Fact]
    public async Task Submit_ReusedPiiField_WithWriteRole_EncryptedAtRest_ViaStorePep()
    {
        var harness = await CreateReuseHarnessAsync();

        var receipt = await harness.SubmitAsync(
            """{"title":"Widget","ssnReused":"123-45-6789"}""",
            "reused-pii");

        using var stored = await harness.StoredAsync(receipt.InstanceId);
        Assert.Equal(1, stored.RootElement.GetProperty("ssnReused").GetProperty("$harborlineProtected").GetInt32());
        Assert.DoesNotContain("123-45-6789", stored.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("Widget", stored.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Submit_ReferenceBearingForm_WithNoReuseResolver_RefusedFailClosed()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(
            definitionFactory: CreateReferenceBearingDefinition);

        var exception = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.SubmitAsync("""{"title":"Widget"}""", "unresolved-reference"));

        Assert.Contains(exception.Errors, error =>
            error.Code.HasValue && error.Code.Value == "unresolved-reference");
        Assert.Equal((0, 0, 0, 0), await harness.EngineHarness.Store.CountsAsync());
    }

    [Fact]
    public async Task SaveAsync_ProtectionOrGovernanceFailure_PersistsNothing()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(hostJurisdiction: "EU");

        await Assert.ThrowsAsync<FormEngineGovernanceException>(async () =>
            await harness.SubmitAsync("""{"caseNote":"controlled"}""", "governance-failure"));

        Assert.Equal((0, 0, 0, 0), await harness.EngineHarness.Store.CountsAsync());
    }

    [Fact]
    public async Task AuditAndOutboxPayloads_NeverContainCleartextPii()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();

        await harness.SubmitAsync("""{"ssn":"123-45-6789","displayName":"Alice"}""", "payload-minimization");

        var audit = Assert.Single(harness.State.Audits.Values);
        var projection = Assert.Single(harness.State.Idempotency.Values).Projection;
        Assert.DoesNotContain("123-45-6789", System.Text.Encoding.UTF8.GetString(audit.Payload.Span), StringComparison.Ordinal);
        Assert.DoesNotContain("123-45-6789", System.Text.Encoding.UTF8.GetString(projection.ProtectedAcceptedValues.Span), StringComparison.Ordinal);
    }

    private static async Task<FormFieldSecurityHarness> CreateReuseHarnessAsync(
        string hostJurisdiction = "US")
    {
        var tenant = new TenantId("tenant-engine");
        var units = new InMemoryReusableUnitStore(
            new FormFieldSecurityHarness.FixedClock(FormEngineOrchestrationTests.Now));
        await units.RegisterAsync(CreateProfileUnit(tenant));
        await units.PublishAsync(tenant, new ReusableUnitId("profileunit"), new SemanticVersion(1, 0, 0));
        return await FormFieldSecurityHarness.CreateAsync(
            definitionFactory: CreateReferenceBearingDefinition,
            hostJurisdiction: hostJurisdiction,
            reuseResolver: new ReuseResolver(units));
    }

    internal static ServiceCollection ProductionPortShell()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFormExecutionContextProvider>(_ => throw new NotSupportedException());
        services.AddScoped<IAuthenticatedActorContext>(_ => throw new NotSupportedException());
        services.AddScoped<IPrincipalPartyResolver>(_ => throw new NotSupportedException());
        services.AddScoped<IFormCapabilityBearerProvider>(_ => throw new NotSupportedException());
        services.AddSingleton<IFormCapabilityRootKeyProvider>(_ => throw new NotSupportedException());
        services.AddScoped<IFormCapabilityVerifier>(_ => throw new NotSupportedException());
        services.AddSingleton<IFormDefinitionStore>(_ => throw new NotSupportedException());
        services.AddSingleton<IReuseResolver>(_ => throw new NotSupportedException());
        services.AddSingleton<KernelSchemaRegistry>(_ => throw new NotSupportedException());
        services.AddSingleton<IFormSensitiveReadAudit>(_ => throw new NotSupportedException());
        services.AddSingleton<IFormSubmissionTransactionStore>(_ => throw new NotSupportedException());
        services.AddSingleton<IFormProjectionSink>(_ => throw new NotSupportedException());
        return services;
    }

    // Ticket 288 slice 2 renamed the data-classification coding system. The taxonomy did not change and
    // this resolver fail-closes on any system it does not recognise, so an overlay authored under the
    // pre-rename spelling must resolve to exactly the policy it did before -- while a genuinely unknown
    // system must still be refused.
    [Fact(DisplayName = "resolves either spelling of the data-classification system to one policy")]
    public void GovernanceResolver_AcceptsThePreRenameDataClassificationSystem()
    {
        var tenant = new TenantId("tenant-classification");
        var resolver = new DefaultFormFieldGovernanceResolver();

        FormFieldGovernancePolicy Resolve(string system)
        {
            var definition = FormFieldSecurityHarness.CreateDefinition("schema-classification", tenant);
            var fields = new Dictionary<string, FieldOverlay>(definition.Overlay.Fields, StringComparer.Ordinal)
            {
                ["caseNote"] = FormFieldSecurityHarness.Classified("Case Note", "cui", ["US"], system),
            };
            return resolver.Resolve(definition with { Overlay = definition.Overlay with { Fields = fields } }, "caseNote");
        }

        var current = Resolve(DefaultFormFieldGovernanceResolver.DataClassificationSystem);
        var legacy = Resolve(DefaultFormFieldGovernanceResolver.LegacyDataClassificationSystem);
        // The record's set-valued members compare by reference, so assert the policy member by member.
        Assert.Equal(current.IsResolved, legacy.IsResolved);
        Assert.Equal(current.ProtectAtRest, legacy.ProtectAtRest);
        Assert.Equal(current.RedactOnRead, legacy.RedactOnRead);
        Assert.Equal(current.AuditOnRead, legacy.AuditOnRead);
        Assert.Equal(current.RequiresSubjectProtection, legacy.RequiresSubjectProtection);
        Assert.Equal(current.Refusal, legacy.Refusal);
        Assert.Equal(current.AllowedJurisdictions, legacy.AllowedJurisdictions);
        Assert.Equal(current.ProhibitedJurisdictions, legacy.ProhibitedJurisdictions);
        Assert.Equal(FormGovernanceRefusal.PolicyUnresolved, Resolve("someone.else/labels").Refusal);
        Assert.NotEqual(FormGovernanceRefusal.PolicyUnresolved, current.Refusal);
    }

    private static ReusableUnit CreateProfileUnit(TenantId tenant) => new(
        new ReusableUnitId("profileunit"),
        new SemanticVersion(1, 0, 0),
        FormDefinitionStatus.Draft,
        tenant,
        ReusableUnitKind.FormComponent,
        IdentityRef.System,
        new FormComponentBody(
            [
                FormItem.OfField("ssnReused"),
                FormItem.OfField("caseNoteReused"),
                FormItem.OfField("restrictedReused"),
            ],
            new Dictionary<string, FieldOverlay>(StringComparer.Ordinal)
            {
                ["ssnReused"] = new(
                    InternationalizedText.FromInvariant("SSN Reused"),
                    PiiSensitivity: PiiSensitivity.Sensitive),
                ["caseNoteReused"] = new(
                    InternationalizedText.FromInvariant("Case Note Reused"),
                    Aspects: new AspectOverlay(
                        new ClassificationAspect([
                            new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "cui"),
                        ]),
                        Lifecycle: new LifecycleAspect(
                            Residency: new ResidencyRequirement(["US"])))),
                ["restrictedReused"] = new(
                    InternationalizedText.FromInvariant("Restricted Reused"),
                    FieldWriteRoles: [Harborline.Contracts.Authorization.RoleReference.Domain("privileged")]),
            }),
        InternationalizedText.FromInvariant("Profile"),
        FormEngineOrchestrationTests.Now,
        FormEngineOrchestrationTests.Now);

    private static FormDefinition CreateReferenceBearingDefinition(string schemaId, TenantId tenant) => new(
        new FormDefinitionId("reuse-form"),
        new SemanticVersion(1, 0, 0),
        FormDefinitionStatus.Published,
        tenant,
        IdentityRef.System,
        new SchemaId(schemaId),
        new HarborlineOverlay(
            new Dictionary<string, FieldOverlay>(StringComparer.Ordinal)
            {
                ["title"] = new(InternationalizedText.FromInvariant("Title")),
            },
            [new FormSection(
                "main",
                InternationalizedText.FromInvariant("Main"),
                ["title"],
                new SectionAccess([Harborline.Contracts.Authorization.RoleReference.Domain("admin")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]),
                Items: [
                    FormItem.OfField("title"),
                    FormItem.OfReference(
                        "profile",
                        new ReusableUnitRef(
                            new ReusableUnitId("profileunit"),
                            ReusableUnitVersionSelector.LatestPublished)),
                ])],
            []),
        null,
        FormEngineOrchestrationTests.Now,
        FormEngineOrchestrationTests.Now);
}
