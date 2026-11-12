using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Foundation.Assistance;

namespace Harborline.Foundation.Assistance.Tests;

internal sealed class FixtureCommandCatalogueRegistry : ICommandCatalogueRegistry
{
    private readonly Dictionary<string, CommandCatalogueEntry> entries;

    public FixtureCommandCatalogueRegistry()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        entries = JsonSerializer.Deserialize<CommandCatalogueEntry[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue-fixture.json")), options)!
            .ToDictionary(entry => entry.CommandId, StringComparer.Ordinal);
    }

    public CommandCatalogueEntry? Resolve(string commandId) => entries.GetValueOrDefault(commandId);
    public void Replace(CommandCatalogueEntry entry) => entries[entry.CommandId] = entry;
}
