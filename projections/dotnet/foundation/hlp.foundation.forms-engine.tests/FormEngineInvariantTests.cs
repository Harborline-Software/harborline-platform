using System.Text.Json;
using Contract = Harborline.Contracts.Forms;
using Persistence = Harborline.Foundation.Forms.Engine.Persistence;
using State = Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Security;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class FormEngineInvariantTests
{
    private static readonly TenantId TenantA = new("tenant:acme");
    private static readonly TenantId TenantB = new("tenant:zenith");

    [Fact]
    public async Task RenderAsync_CrossTenantInstance_ThrowsFormInstanceNotFound()
    {
        var owner = await FormEngineOrchestrationTests.Harness.CreateAsync(scopeTenant: TenantA);
        using var candidate = JsonDocument.Parse("""{"name":"Alice"}""");
        var receipt = await owner.Engine.SubmitAsync(new(owner.Definition.Id, candidate, "tenant-a-instance"));
        var foreign = await FormEngineOrchestrationTests.Harness.CreateAsync(scopeTenant: TenantB, state: owner.State);

        var exception = await Assert.ThrowsAsync<FormEngineNotFoundException>(async () =>
            await foreign.Engine.RenderAsync(foreign.Definition.Id, receipt.InstanceId));

        Assert.Equal("form.engine.not-found", exception.Code);
        Assert.DoesNotContain("Alice", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("acme", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderAsync_NonexistentInstance_ThrowsSameFormInstanceNotFound()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(scopeTenant: TenantB);
        var missing = new EntityId("harborline", "forms", "missing");

        var exception = await Assert.ThrowsAsync<FormEngineNotFoundException>(async () =>
            await harness.Engine.RenderAsync(harness.Definition.Id, missing));

        Assert.Equal("form.engine.not-found", exception.Code);
        Assert.Equal("The requested form resource was not found.", exception.Message);
    }

    [Fact]
    public async Task RenderAsync_OwnTenantInstance_BindsMacaroonTenantAndReadsNonPiiValue()
    {
        using var readable = JsonDocument.Parse("""{"name":"Alice","secret":"withheld"}""");
        var security = new WithholdingSecurity(readable);
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(scopeTenant: TenantA, security: security);
        using var candidate = JsonDocument.Parse("""{"name":"Alice","secret":"cleartext"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "own-tenant-instance"));

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);
        var fields = Assert.Single(view.Sections).Fields;
        var name = Assert.Single(fields, field => field.Name == "name");
        Assert.True(name.IsReadable);
        Assert.True(name.Value.HasValue);
        Assert.Equal("Alice", name.Value.Value.GetString());
        var secret = Assert.Single(fields, field => field.Name == "secret");
        Assert.True(secret.IsSensitive);
        Assert.False(secret.IsReadable);
        Assert.False(secret.Value.HasValue);
    }

    [Fact]
    public async Task ValidateAsync_CandidateOverByteCap_IsResourceBound()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            options: new FormEngineOptions { MaximumCandidateBytes = 16 });
        using var candidate = JsonDocument.Parse("""{"name":"a string that is clearly larger than sixteen bytes"}""");

        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Contract.ValidationErrorKind.ResourceBound, error.Kind);
        Assert.True(error.Code.HasValue);
        Assert.Equal("candidate-too-large", error.Code.Value);
        var parameters = Params(error);
        Assert.Equal("16", parameters["limit"]);
        Assert.True(int.Parse(parameters["bytes"]) > 16);
    }

    [Fact]
    public async Task ValidateAsync_SchemaFailures_CarryStableCodesAndParams()
    {
        var harness = await ConstrainedHarnessAsync();
        using var candidate = JsonDocument.Parse("""{"assetType":"bus","conditionRating":99}""");

        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);

        Assert.False(result.IsValid);
        var required = Assert.Single(result.Errors, error => Code(error) == "required" && error.JsonPointer == "/assetId");
        Assert.Equal("assetId", Params(required)["field"]);
        var maximum = Assert.Single(result.Errors, error => Code(error) == "maximum" && error.JsonPointer == "/conditionRating");
        Assert.Equal("5", Params(maximum)["max"]);
        var enumeration = Assert.Single(result.Errors, error => Code(error) == "enum" && error.JsonPointer == "/assetType");
        Assert.Contains("metro-car", Params(enumeration)["allowed"], StringComparison.Ordinal);
        Assert.All(result.Errors, error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
    }

    [Fact]
    public async Task ValidateAsync_ValidCandidate_AgainstConstrainedSchema_IsValid()
    {
        var harness = await ConstrainedHarnessAsync();
        using var candidate = JsonDocument.Parse("""{"assetId":"TRK-1","assetType":"tram","conditionRating":4}""");

        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task SaveAsync_EmitsExactlyOneMint_NamingEncryptedFields()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(scopeTenant: TenantA);
        using var candidate = JsonDocument.Parse("""{"name":"Alice","secret":"classified"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "mint-audit"));

        var audit = Assert.Single(harness.State.Audits.Values);
        Assert.Equal(receipt.InstanceId, audit.InstanceId);
        Assert.Equal(TenantA, audit.Tenant);
        using var payload = JsonDocument.Parse(audit.Payload);
        Assert.Equal("form-instance-mint", payload.RootElement.GetProperty("op").GetString());
        Assert.Equal(harness.Definition.Id.Value, payload.RootElement.GetProperty("form").GetString());
        Assert.Contains("secret", payload.RootElement.GetProperty("encryptedFields").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain("name", payload.RootElement.GetProperty("encryptedFields").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task SaveAsync_EncryptsSensitiveAndUnoverlaidFields_KeepsNoneCleartext()
    {
        var harness = await FormFieldSecurityHarness.CreateAsync();
        var receipt = await harness.SubmitAsync(
            """{"ssn":"123-45-6789","displayName":"Alice","secretNote":"top-secret"}""", "default-secure");

        using var stored = await harness.StoredAsync(receipt.InstanceId);
        var raw = stored.RootElement.GetRawText();
        Assert.Equal(1, stored.RootElement.GetProperty("ssn").GetProperty("$harborlineProtected").GetInt32());
        Assert.Equal(1, stored.RootElement.GetProperty("secretNote").GetProperty("$harborlineProtected").GetInt32());
        Assert.Equal("Alice", stored.RootElement.GetProperty("displayName").GetString());
        Assert.DoesNotContain("123-45-6789", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", raw, StringComparison.Ordinal);
    }

    private static Task<FormEngineOrchestrationTests.Harness> ConstrainedHarnessAsync() =>
        FormEngineOrchestrationTests.Harness.CreateAsync(
            scopeTenant: TenantA,
            schemaJson: """
                {
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "type": "object",
                  "properties": {
                    "assetId": { "type": "string", "minLength": 1 },
                    "assetType": { "type": "string", "enum": ["metro-car", "tram", "station-unit"] },
                    "conditionRating": { "type": "integer", "minimum": 1, "maximum": 5 }
                  },
                  "required": ["assetId", "conditionRating"],
                  "additionalProperties": false
                }
                """,
            definitionFactory: ConstrainedDefinition);

    private static State.FormDefinition ConstrainedDefinition(string schemaId, TenantId tenant)
    {
        var fields = new[] { "assetId", "assetType", "conditionRating" }.ToDictionary(
            name => name,
            name => new State.FieldOverlay(State.InternationalizedText.FromInvariant(name)),
            StringComparer.Ordinal);
        var now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
        return new(
            new("property"),
            new(1, 0, 0),
            State.FormDefinitionStatus.Published,
            tenant,
            State.IdentityRef.System,
            new(schemaId),
            new(fields, [new("main", State.InternationalizedText.FromInvariant("Main"), fields.Keys.ToArray(), new([Harborline.Contracts.Authorization.RoleReference.Domain("admin")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]))], []),
            null,
            now,
            now);
    }

    private static string Code(Contract.ValidationError error) => error.Code.HasValue ? error.Code.Value! : string.Empty;

    private static IReadOnlyDictionary<string, string> Params(Contract.ValidationError error)
    {
        Assert.True(error.Params.HasValue);
        Assert.NotNull(error.Params.Value);
        return error.Params.Value;
    }

    private sealed class WithholdingSecurity(JsonDocument readable) : IFormFieldSecurity
    {
        public ValueTask<FormProtectionResult> ProtectAsync(
            FormExecutionScope scope,
            State.FormDefinition definition,
            Harborline.Foundation.Assets.Common.EntityId instanceId,
            JsonDocument acceptedCandidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormProtectionResult(
                "{\"protected\":true}"u8.ToArray(),
                new HashSet<string>(["secret"], StringComparer.Ordinal)));

        public ValueTask<FormReadableCandidate> ReadAsync(
            FormExecutionScope scope,
            State.FormDefinition definition,
            Persistence.FormSubmissionRecord submission,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormReadableCandidate(
                readable.RootElement.EnumerateObject().Select(property => new FormFieldReadDecision(
                    property.Name,
                    property.Name == "secret" ? FormFieldReadDisposition.Withheld : FormFieldReadDisposition.Plaintext,
                    property.Name == "secret" ? null : property.Value.Clone(),
                    property.Name == "secret",
                    [])).ToArray()));
    }
}
