using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Persistence;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class DecryptOnRenderTests
{
    [Fact]
    public async Task Render_WithDecryptPermission_DecryptsSensitiveValue_AndAuditsTheDecrypt()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(decryptPermission: true);
        var receipt = await harness.SubmitAsync("""{"ssn":"123-45-6789","displayName":"Alice"}""", "grant");
        harness.Audit.Appends.Clear();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var ssn = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "ssn");
        Assert.True(ssn.IsSensitive);
        Assert.True(ssn.IsReadable);
        Assert.Equal("123-45-6789", ssn.Value.Value.GetString());
        var decision = Assert.Single(harness.Audit.Appends).Event;
        Assert.Equal("ssn", decision.FieldName);
        Assert.Equal(FormEnginePermissions.DecryptSensitive, decision.Permission);
        Assert.Equal(FormSensitiveReadAuditOutcome.Granted, decision.Outcome);
        Assert.Equal("cap:test", decision.DecryptCapabilityId);
    }

    [Fact]
    public async Task Render_WithoutDecryptPermission_WithholdsExactlyAsToday()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await harness.SubmitAsync("""{"ssn":"123-45-6789","displayName":"Alice"}""", "no-permission");
        harness.Audit.Appends.Clear();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var ssn = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "ssn");
        Assert.True(ssn.IsSensitive);
        Assert.False(ssn.IsReadable);
        Assert.False(ssn.Value.HasValue);
        Assert.Empty(harness.Audit.Appends);
    }

    [Fact]
    public async Task Render_PermissionButNoSeamWired_WithholdsFailClosed_NoAuditRows()
    {
        var writer = await FormFieldSecurityHarness.CreateAsync(decryptPermission: true);
        var receipt = await writer.SubmitAsync("""{"ssn":"123-45-6789","displayName":"Alice"}""", "unwired");
        var audit = new FormFieldSecurityHarness.RecordingFormSecurityAudit();
        var reader = await FormFieldSecurityHarness.CreateAsync(
            decryptPermission: true, state: writer.State, keys: writer.Keys,
            security: new WithholdingFormFieldSecurity(writer.Security), audit: audit);

        var view = await reader.Engine.RenderAsync(reader.Definition.Id, receipt.InstanceId);

        var ssn = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "ssn");
        Assert.False(ssn.IsReadable);
        Assert.False(ssn.Value.HasValue);
        Assert.Empty(audit.Appends);
    }

    [Fact]
    public async Task Render_NoPermission_TwoSensitiveFields_WritesZeroAuditRows()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await harness.SubmitAsync(
            """{"ssn":"123-45-6789","medicalId":"MED-777","displayName":"Alice"}""", "two-sensitive");
        harness.Audit.Appends.Clear();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var fields = Assert.Single(view.Sections).Fields;
        Assert.False(Assert.Single(fields, field => field.Name == "ssn").IsReadable);
        Assert.False(Assert.Single(fields, field => field.Name == "medicalId").IsReadable);
        Assert.Empty(harness.Audit.Appends);
    }

    [Fact]
    public void DecryptOnRenderPurpose_IsOnTheLiveProviderAllowlist() =>
        Assert.Contains(FormEnginePermissions.DecryptOnRenderPurpose, TenantBoundAesGcmFormFieldSecurity.AcceptedDecryptPurposes);

    [Fact]
    public async Task Render_ProviderRefusesCapability_WithholdsFailClosed()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(decryptPermission: true, capabilityIssues: false);
        var receipt = await harness.SubmitAsync("""{"ssn":"123-45-6789"}""", "refused");
        harness.Audit.Appends.Clear();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var ssn = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "ssn");
        Assert.False(ssn.IsReadable);
        var decision = Assert.Single(harness.Audit.Appends).Event;
        Assert.Equal(FormSensitiveReadAuditOutcome.Denied, decision.Outcome);
        Assert.Equal("capability-refused", decision.Reason);
        Assert.Null(decision.DecryptCapabilityId);
    }

    [Fact]
    public async Task Render_RealRecoverySubstrate_EndToEndRoundTrip()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(decryptPermission: true);
        var receipt = await harness.SubmitAsync("""{"ssn":"123-45-6789"}""", "round-trip");
        using var stored = await harness.StoredAsync(receipt.InstanceId);
        Assert.Equal(1, stored.RootElement.GetProperty("ssn").GetProperty("$harborlineProtected").GetInt32());
        Assert.DoesNotContain("123-45-6789", stored.RootElement.GetRawText(), StringComparison.Ordinal);

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        Assert.Equal("123-45-6789", Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "ssn").Value.Value.GetString());
        Assert.Contains(FormEnginePermissions.DecryptOnRenderPurpose, harness.Capabilities.Purposes);
    }

    [Fact]
    public async Task Render_NonSensitiveField_UnaffectedByPermission()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync(decryptPermission: true);
        var receipt = await harness.SubmitAsync("""{"ssn":"123-45-6789","displayName":"Alice"}""", "ordinary");

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var displayName = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "displayName");
        Assert.True(displayName.IsReadable);
        Assert.Equal("Alice", displayName.Value.Value.GetString());
    }

    [Fact]
    public async Task RenderAsync_DecryptProviderTimeoutOrFault_WithholdsAndAuditsDenial()
    {
        var keys = new FormFieldSecurityHarness.FixedTenantKeyProvider();
        var security = new TenantBoundAesGcmFormFieldSecurity(
            keys,
            new ThrowingCapabilityProvider(),
            clock: new FormFieldSecurityHarness.FixedClock(FormEngineOrchestrationTests.Now));
        var harness = await FormFieldSecurityHarness.CreateAsync(
            decryptPermission: true,
            keys: keys,
            security: security);
        var receipt = await harness.SubmitAsync("""{"ssn":"123-45-6789"}""", "provider-fault");
        harness.Audit.Appends.Clear();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        var ssn = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "ssn");
        Assert.False(ssn.IsReadable);
        var denial = Assert.Single(harness.Audit.Appends).Event;
        Assert.Equal(FormSensitiveReadAuditOutcome.Denied, denial.Outcome);
        Assert.Equal("capability-provider-unavailable", denial.Reason);
    }

    [Fact]
    public async Task ProtectionAdapter_EncryptDecryptRoundTrip_IsTenantBound()
    {
        var tenantA = new TenantId("tenant:acme");
        var tenantB = new TenantId("tenant:zenith");
        var instanceId = new EntityId("harborline", "forms", "tenant-bound");
        var keys = new FormFieldSecurityHarness.FixedTenantKeyProvider();
        var capabilities = new FormFieldSecurityHarness.StubDecryptCapabilityProvider(true);
        var security = new TenantBoundAesGcmFormFieldSecurity(
            keys,
            capabilities,
            clock: new FormFieldSecurityHarness.FixedClock(FormEngineOrchestrationTests.Now));
        var definition = FormFieldSecurityHarness.CreateDefinition("schema:test", tenantA);
        var scopeA = new FormExecutionScope(
            tenantA,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "alice",
            ["admin", FormEnginePermissions.DecryptSensitive]);
        using var candidate = System.Text.Json.JsonDocument.Parse("""{"ssn":"123-45-6789"}""");
        var protectedResult = await security.ProtectAsync(scopeA, definition, instanceId, candidate);
        var submission = new FormSubmissionRecord(
            instanceId,
            tenantA,
            scopeA.PartyId,
            scopeA.ActorId,
            definition.Id,
            definition.Version,
            "fingerprint",
            protectedResult.ProtectedCandidate,
            FormEngineOrchestrationTests.Now);

        var sameTenant = await security.ReadAsync(scopeA, definition, submission);
        Assert.Equal(FormFieldReadDisposition.DecryptGranted, Assert.Single(sameTenant.Fields).Disposition);

        var scopeB = scopeA with { Tenant = tenantB };
        var wrongTenant = await security.ReadAsync(scopeB, definition, submission);
        var denied = Assert.Single(wrongTenant.Fields);
        Assert.Equal(FormFieldReadDisposition.Withheld, denied.Disposition);
        Assert.Equal("decrypt-denied", Assert.Single(denied.RequiredAudits).Reason);
    }

    private sealed class ThrowingCapabilityProvider : IFormDecryptCapabilityProvider
    {
        public ValueTask<FormDecryptCapability?> AcquireAsync(
            TenantId tenant,
            string purpose,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<FormDecryptCapability?>(new TimeoutException("provider timed out"));
    }
}
