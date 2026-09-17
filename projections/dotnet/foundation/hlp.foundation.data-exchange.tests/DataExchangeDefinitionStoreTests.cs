using Harborline.Foundation.DataExchange;
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

        Assert.Equal(
            ["definition.credential_forbidden", "definition.cursor_forbidden", "definition.retention_forbidden"],
            exception.Refusals.Select(refusal => refusal.Code).Order(StringComparer.Ordinal));
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
        "schedule.weekly");
}
