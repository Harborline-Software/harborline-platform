using Harborline.Foundation.DataExchange;
using System.Text.Json;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class DataExchangeDefinitionStoreTests
{
    [Fact]
    public async Task Published_definition_is_immutable_restore_creates_a_new_draft_and_pack_exports_only_definition()
    {
        var store = new InMemoryDataExchangeDefinitionStore();
        var definition = Definition("1.0.0");

        await store.CreateDraftAsync(definition);
        await store.PublishAsync("tenant-a", "exchange.customers", "1.0.0");
        var restored = await store.RestoreAsDraftAsync(
            "tenant-a",
            "exchange.customers",
            "1.0.0",
            "1.1.0");

        Assert.Equal(DataExchangeDefinitionStatus.Draft, restored.Status);
        Assert.Equal("1.0.0", restored.RestoredFromVersion);
        Assert.Equal("1.1.0", restored.Definition.Version);
        var history = await store.ListHistoryAsync("tenant-a", "exchange.customers");
        Assert.Equal(2, history.Count);
        Assert.Equal(DataExchangeDefinitionStatus.Published, history[0].Status);
        var package = DataExchangeDefinitionPackExporter.Export(history);
        Assert.Single(package);
        Assert.Equal("exchange.customers", package[0].Definition.Key);
        Assert.Equal(1, package[0].Definition.SchemaVersion);
        Assert.Equal("exchange.customers", package[0].Definition.Envelope?.Identity);
        Assert.Equal("csv", package[0].Definition.Source.FormatId);
    }

    [Fact]
    public async Task Definition_cannot_choose_run_retention_or_carry_cursor_or_credentials()
    {
        var definition = Definition("1.0.0") with
        {
            Source = Definition("1.0.0").Source with
            {
                Parameters = new Dictionary<string, string>
                {
                    ["retentionDays"] = "1",
                    ["cursor"] = "secret-cursor",
                    ["password"] = "secret",
                },
            },
        };
        var store = new InMemoryDataExchangeDefinitionStore();

        var exception = await Assert.ThrowsAsync<DataExchangeAdmissionException>(
            () => store.CreateDraftAsync(definition).AsTask());

        Assert.Contains(exception.Refusals, refusal => refusal.Code == "definition.credential_forbidden");
        Assert.Contains(exception.Refusals, refusal => refusal.Code == "definition.cursor_forbidden");
        Assert.Contains(exception.Refusals, refusal => refusal.Code == "definition.retention_forbidden");
        Assert.Equal(3, exception.Refusals.Count(refusal => refusal.Code == "definition.source_parameter_undeclared"));
    }

    [Fact]
    public async Task External_keys_must_name_columns_in_the_admitted_mapping()
    {
        var definition = Definition("1.0.0") with { ExternalKeyColumns = ["UnknownColumn"] };
        var store = new InMemoryDataExchangeDefinitionStore();

        var exception = await Assert.ThrowsAsync<DataExchangeAdmissionException>(
            () => store.CreateDraftAsync(definition).AsTask());

        Assert.Contains(exception.Refusals, refusal => refusal.Code == "definition.external_key_unknown");
    }

    [Fact]
    public async Task Published_head_resolves_the_latest_published_semver_and_ignores_newer_drafts()
    {
        var store = new InMemoryDataExchangeDefinitionStore();
        await store.CreateDraftAsync(Definition("1.0.0"));
        await store.PublishAsync("tenant-a", "exchange.customers", "1.0.0");
        await store.CreateDraftAsync(Definition("1.1.0"));
        await store.PublishAsync("tenant-a", "exchange.customers", "1.1.0");
        await store.CreateDraftAsync(Definition("2.0.0"));

        var head = await store.GetPublishedHeadAsync("tenant-a", "exchange.customers");

        Assert.NotNull(head);
        Assert.Equal("1.1.0", head.Definition.Version);
        Assert.Equal(DataExchangeDefinitionStatus.Published, head.Status);
    }

    [Theory]
    [InlineData("apiKey")]
    [InlineData("accessToken")]
    [InlineData("clientSecret")]
    [InlineData("pass.word")]
    public async Task Credential_aliases_are_refused_even_when_a_capability_schema_declares_them(string parameter)
    {
        var store = new InMemoryDataExchangeDefinitionStore(new OneParameterSchema(parameter));
        var definition = Definition("1.0.0") with
        {
            Source = Definition("1.0.0").Source with
            {
                Parameters = new Dictionary<string, string> { [parameter] = "must-not-export" },
            },
        };

        var exception = await Assert.ThrowsAsync<DataExchangeAdmissionException>(
            () => store.CreateDraftAsync(definition).AsTask());

        Assert.Contains(exception.Refusals, refusal => refusal.Code == "definition.credential_forbidden");
    }

    [Theory]
    [InlineData("plain-text-secret")]
    [InlineData("secret://")]
    [InlineData("secretref:bad reference")]
    [InlineData("https://vault.example/secret")]
    public async Task Secret_reference_must_use_the_opaque_reference_grammar(string secretReference)
    {
        var store = new InMemoryDataExchangeDefinitionStore();
        var definition = Definition("1.0.0") with
        {
            Source = Definition("1.0.0").Source with { SecretReference = secretReference },
        };

        var exception = await Assert.ThrowsAsync<DataExchangeAdmissionException>(
            () => store.CreateDraftAsync(definition).AsTask());

        Assert.Contains(exception.Refusals, refusal => refusal.Code == "definition.secret_reference_invalid");
    }

    private static DataExchangeDefinition Definition(string version) => new(
        "tenant-a",
        "exchange.customers",
        version,
        "Customer opening load",
        new ExchangeSourceBinding(
            "erpnext",
            "4.1.0",
            "secret://erpnext/customer-import",
            new Dictionary<string, string>()),
        Fixtures.Mapping(),
        ReplayPolicy.AppendDeduplicate,
        ["CustomerNumber"],
        "schedule.weekly",
        Envelope: new(
            "exchange.customers",
            version,
            "tenant-a",
            DataExchangeCascadeLayer.Tenant,
            JsonSerializer.SerializeToElement(new { kind = "tenant" }),
            [new("records.customer", "1.0.0")]));
}

internal sealed class OneParameterSchema(string parameter) : ISourceParameterSchemaRegistry
{
    public SourceParameterSchema Resolve(string capabilityId, string connectorVersion)
        => new(new HashSet<string>([parameter], StringComparer.Ordinal));
}
