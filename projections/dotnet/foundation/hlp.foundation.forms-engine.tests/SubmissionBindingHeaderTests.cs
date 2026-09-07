using System.Text.Json;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class SubmissionBindingHeaderTests
{
    [Fact]
    public async Task SaveAsync_StampsBindingHeader_OnTheMintAuditEnvelope()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "binding"));

        using var payload = AuditPayload(harness);
        var binding = payload.RootElement.GetProperty("binding");
        Assert.Equal(harness.Definition.SchemaRef.Value, binding.GetProperty("schemaRef").GetString());
        Assert.Equal(harness.Definition.Id.Value, binding.GetProperty("definitionId").GetString());
        Assert.Equal("1.0.0", binding.GetProperty("definitionVersion").GetString());
        Assert.Equal("harborline-jsonlogic/v1", binding.GetProperty("engineVersion").GetString());
        Assert.Equal(["en"], Locales(binding));
        Assert.Equal(FormEngineOrchestrationTests.Now, binding.GetProperty("submittedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task SaveAsync_BindingHeaderLocaleChain_ComesFromEngineOptions()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            options: new FormEngineOptions { LocaleChain = ["ar-AE", "en"] });
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "locales"));

        using var payload = AuditPayload(harness);
        Assert.Equal(["ar-AE", "en"], Locales(payload.RootElement.GetProperty("binding")));
    }

    [Fact]
    public async Task SaveAsync_PreservesPreD3TopLevelKeys_ForBackCompat()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada","secret":"classified"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "compat"));

        using var payload = AuditPayload(harness);
        var root = payload.RootElement;
        Assert.Equal("form-instance-mint", root.GetProperty("op").GetString());
        Assert.Equal(harness.Definition.Id.Value, root.GetProperty("form").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        Assert.Contains("secret", root.GetProperty("encryptedFields").EnumerateArray().Select(row => row.GetString()));
    }

    [Fact]
    public async Task SaveAsync_OrdinarySubmission_CarriesNoSnapshot()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "ordinary"));

        using var payload = AuditPayload(harness);
        Assert.False(payload.RootElement.TryGetProperty("snapshot", out _));
    }

    // Ticket 288 slice 2 renamed the capture-policy coding system. The vocabulary did not change, so an
    // overlay authored under either spelling must still ask for the full projection snapshot.
    [Theory]
    [InlineData(FormEngine.CapturePolicySystem)]
    [InlineData(FormEngine.LegacyCapturePolicySystem)]
    public async Task SaveAsync_ComplianceGradeForm_CapturesFullProjectionSnapshot(string capturePolicySystem)
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(definitionFactory: (schemaId, tenant) =>
        {
            var definition = FormEngineOrchestrationTests.Harness.CreateDefinition(schemaId, tenant);
            return definition with
            {
                Overlay = definition.Overlay with
                {
                    Aspects = new AspectOverlay(new ClassificationAspect(
                        [new Tag(capturePolicySystem, "capture-as-shown")]))
                }
            };
        });
        using var candidate = JsonDocument.Parse("""{"name":"Ada","secret":"never-in-audit"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "compliance"));

        using var payload = AuditPayload(harness);
        var snapshot = payload.RootElement.GetProperty("snapshot");
        Assert.Equal("full-projection", snapshot.GetProperty("mode").GetString());
        Assert.Equal(FormEngineOrchestrationTests.Now, snapshot.GetProperty("capturedAt").GetDateTimeOffset());
        Assert.True(snapshot.GetProperty("projection").GetProperty("protected").GetBoolean());
        Assert.DoesNotContain("never-in-audit", payload.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    private static JsonDocument AuditPayload(FormEngineOrchestrationTests.Harness harness)
    {
        var audit = Assert.Single(harness.State.Audits.Values);
        return JsonDocument.Parse(audit.Payload);
    }

    private static string[] Locales(JsonElement binding) =>
        binding.GetProperty("localeChain").EnumerateArray().Select(row => row.GetString()!).ToArray();
}
