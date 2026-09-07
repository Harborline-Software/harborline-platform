using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Tests;

internal sealed class FormFieldSecurityHarness
{
    private FormFieldSecurityHarness(
        FormEngineOrchestrationTests.Harness engineHarness,
        RecordingFormSecurityAudit audit,
        FixedTenantKeyProvider keys,
        StubDecryptCapabilityProvider capabilities,
        IFormFieldSecurity security)
        => (EngineHarness, Audit, Keys, Capabilities, Security) = (engineHarness, audit, keys, capabilities, security);

    public FormEngineOrchestrationTests.Harness EngineHarness { get; }
    public RecordingFormSecurityAudit Audit { get; }
    public FixedTenantKeyProvider Keys { get; }
    public StubDecryptCapabilityProvider Capabilities { get; }
    public IFormFieldSecurity Security { get; }
    public FormEngine Engine => EngineHarness.Engine;
    public FormDefinition Definition => EngineHarness.Definition;
    public InMemoryFormSubmissionState State => EngineHarness.State;

    public static async Task<FormFieldSecurityHarness> CreateAsync(
        bool decryptPermission = false,
        bool capabilityIssues = true,
        InMemoryFormSubmissionState? state = null,
        FixedTenantKeyProvider? keys = null,
        IFormFieldSecurity? security = null,
        Func<string, TenantId, FormDefinition>? definitionFactory = null,
        RecordingFormSecurityAudit? audit = null,
        string? hostJurisdiction = "US",
        IReuseResolver? reuseResolver = null)
    {
        var actualKeys = keys ?? new FixedTenantKeyProvider();
        var capabilities = new StubDecryptCapabilityProvider(capabilityIssues);
        var actualAudit = audit ?? new RecordingFormSecurityAudit();
        var actualSecurity = security ?? new TenantBoundAesGcmFormFieldSecurity(
            actualKeys, capabilities, new DefaultFormFieldGovernanceResolver(),
            new FormFieldSecurityOptions { HostJurisdiction = hostJurisdiction },
            clock: new FixedClock(FormEngineOrchestrationTests.Now));
        var roles = decryptPermission
            ? new[] { "admin", FormEnginePermissions.DecryptSensitive }
            : ["admin"];
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: roles,
            state: state,
            security: actualSecurity,
            readAudit: actualAudit,
            schemaJson: """{"type":"object","additionalProperties":true}""",
            definitionFactory: definitionFactory ?? CreateDefinition,
            reuseResolver: reuseResolver);
        return new(harness, actualAudit, actualKeys, capabilities, actualSecurity);
    }

    public async Task<FormSubmitReceipt> SubmitAsync(string candidate, string key)
    {
        using var document = JsonDocument.Parse(candidate);
        return await Engine.SubmitAsync(new(Definition.Id, document, key));
    }

    public async Task<JsonDocument> StoredAsync(EntityId instanceId)
    {
        var record = await EngineHarness.Store.GetAsync(Definition.Tenant, instanceId);
        return JsonDocument.Parse(record!.ProtectedAcceptedCandidate);
    }

    internal static FormDefinition CreateDefinition(string schemaId, TenantId tenant)
    {
        var fields = new Dictionary<string, FieldOverlay>(StringComparer.Ordinal)
        {
            ["displayName"] = new(InternationalizedText.FromInvariant("Display Name")),
            ["ssn"] = new(InternationalizedText.FromInvariant("SSN"), PiiSensitivity: PiiSensitivity.Sensitive),
            ["medicalId"] = new(InternationalizedText.FromInvariant("Medical ID"), PiiSensitivity: PiiSensitivity.Sensitive),
            ["cardNumber"] = Classified("Card Number", "pci"),
            ["classifiedSsn"] = Classified("Classified SSN", "pii"),
            ["caseNote"] = Classified("Case Note", "cui", ["US"]),
            ["diagnosis"] = Classified("Diagnosis", "phi", ["US"]),
            ["tagged"] = new(
                InternationalizedText.FromInvariant("Tagged"),
                Aspects: new AspectOverlay(new ClassificationAspect([new Tag("acme/labels", "secret")]))),
        };
        return new(
            new("security-form"), new(1, 0, 0), FormDefinitionStatus.Published, tenant,
            IdentityRef.System, new(schemaId),
            new(fields, [new("main", InternationalizedText.FromInvariant("Main"), fields.Keys.ToArray(), new([Harborline.Contracts.Authorization.RoleReference.Domain("admin")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]))], []),
            null, FormEngineOrchestrationTests.Now, FormEngineOrchestrationTests.Now);
    }

    internal static FieldOverlay Classified(string label, string code, IReadOnlyList<string>? residency = null,
        string? system = null) => new(
        InternationalizedText.FromInvariant(label),
        Aspects: new AspectOverlay(
            new ClassificationAspect([new Tag(system ?? DefaultFormFieldGovernanceResolver.DataClassificationSystem, code)]),
            Lifecycle: residency is null ? null : new LifecycleAspect(Residency: new ResidencyRequirement(residency))));

    internal sealed class FixedTenantKeyProvider : IFormTenantProtectionKeyProvider
    {
        private readonly Dictionary<TenantId, FormTenantProtectionKey> _keys = new();

        public ValueTask<FormTenantProtectionKey?> GetCurrentAsync(TenantId tenant, string keyDomain, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<FormTenantProtectionKey?>(GetOrCreate(tenant));

        public ValueTask<FormTenantProtectionKey?> GetAsync(TenantId tenant, string keyDomain, string keyVersion, CancellationToken cancellationToken = default)
        {
            var key = GetOrCreate(tenant);
            return ValueTask.FromResult<FormTenantProtectionKey?>(
                string.Equals(keyDomain, TenantBoundAesGcmFormFieldSecurity.FieldEncryptionKeyDomain, StringComparison.Ordinal) &&
                string.Equals(key.KeyVersion, keyVersion, StringComparison.Ordinal) ? key : null);
        }

        private FormTenantProtectionKey GetOrCreate(TenantId tenant)
        {
            if (_keys.TryGetValue(tenant, out var key)) return key;
            var material = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"test-only:{tenant}"));
            return _keys[tenant] = new("test-key-1", material);
        }
    }

    internal sealed class StubDecryptCapabilityProvider(bool issues) : IFormDecryptCapabilityProvider
    {
        public List<string> Purposes { get; } = [];
        public ValueTask<FormDecryptCapability?> AcquireAsync(TenantId tenant, string purpose, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            Purposes.Add(purpose);
            return ValueTask.FromResult<FormDecryptCapability?>(issues
                ? new("cap:test", tenant, purpose, FormEngineOrchestrationTests.Now.Add(lifetime))
                : null);
        }
    }

    internal sealed class RecordingFormSecurityAudit : IFormSensitiveReadAudit
    {
        public List<FormSensitiveReadAudit> Appends { get; } = [];
        public ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default)
        {
            Appends.Add(audit);
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
