using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Foundation.Definitions.Tests;

public sealed class PackContentKindRegistryTests
{
    // harborline-api packages/foundation-packs/Model/PackEnums.cs PackContentKind and Graph/PackPillar.cs
    // PackPillarMap.ForKind at 9e7ebb1e. These values are serialized and never move (owner ruling 2026-09-29).
    public static TheoryData<string, int, string> ApiShippedKinds => new()
    {
        { "FormDefinition", 0, "Forms" },
        { "WorkflowDefinition", 1, "Automations" },
        { "StandardsCatalog", 2, "Rules" },
        { "NavWorkspaceConfig", 3, "Navigation" },
        { "CascadeDefaults", 4, "Settings" },
        { "AssetTypeDefinition", 5, "Records" },
        { "TerminologyOverride", 6, "Settings" },
        { "TemplateDefinition", 7, "Documents" },
        { "TaxonomyDefinition", 8, "Taxonomy" },
        { "ReportDefinition", 9, "Reports" },
        { "ViewDefinition", 10, "Views" },
        { "ScheduleDefinition", 11, "Scheduling" },
        { "DataExchangeDefinition", 12, "DataExchange" },
        { "StandingRuleDefinition", 13, "Rules" },
        { "RoleDefinition", 14, "Other" },
        { "AuthorizationCapabilityBinding", 15, "Other" },
        { "RecordType", 16, "Other" },
        { "Layout", 17, "Layout" },
        { "Resource", 18, "Booking" },
        { "Bookable", 19, "Booking" },
    };

    [Theory]
    [MemberData(nameof(ApiShippedKinds))]
    public void Every_kind_the_api_ships_keeps_the_api_value_and_pillar(string name, int value, string pillar)
    {
        Assert.Equal(new PackContentKindEntry(name, value, pillar, PackContentKindStatus.Shipped), PackContentKindRegistry.Kind(name));
    }

    [Fact]
    public void The_registry_ships_exactly_the_api_kinds_and_reserves_the_rest()
    {
        var shipped = ApiShippedKinds.Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            shipped.Order(StringComparer.Ordinal),
            PackContentKindRegistry.Kinds.Where(kind => kind.Status == PackContentKindStatus.Shipped).Select(kind => kind.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["AssistanceDefinition", "ReleasedNavigationDefinition"],
            PackContentKindRegistry.Kinds.Where(kind => kind.Status == PackContentKindStatus.Reserved).Select(kind => kind.Name));
    }

    [Fact]
    public void Released_navigation_takes_the_next_free_value_after_every_other_kind()
    {
        var navigation = PackContentKindRegistry.Kind("ReleasedNavigationDefinition");
        Assert.Equal(new PackContentKindEntry("ReleasedNavigationDefinition", 21, "Navigation", PackContentKindStatus.Reserved), navigation);
        Assert.Equal(PackContentKindRegistry.Kinds.Where(kind => kind != navigation).Max(kind => kind.Value) + 1, navigation.Value);
        Assert.Equal(new PackContentKindEntry("AssistanceDefinition", 20, "Other", PackContentKindStatus.Reserved), PackContentKindRegistry.Kind("AssistanceDefinition"));
    }

    [Fact]
    public void Pillars_are_the_api_pillars()
    {
        Assert.Equal(
            [("Records", 0), ("Forms", 1), ("Automations", 2), ("Navigation", 3), ("Rules", 4), ("Settings", 5), ("Documents", 6),
             ("Taxonomy", 7), ("Reports", 8), ("Views", 9), ("Scheduling", 10), ("DataExchange", 11), ("Layout", 12), ("Booking", 13), ("Other", 99)],
            PackContentKindRegistry.Pillars.Select(pillar => (pillar.Name, pillar.Value)));
    }

    [Fact]
    public void Names_and_values_are_unique_ordered_and_every_pillar_resolves()
    {
        var kinds = PackContentKindRegistry.Kinds;
        Assert.Equal(kinds.Count, kinds.Select(kind => kind.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(kinds.Select(kind => kind.Value).Order(), kinds.Select(kind => kind.Value));
        Assert.Equal(kinds.Count, kinds.Select(kind => kind.Value).Distinct().Count());
        Assert.Equal(PackContentKindRegistry.Pillars.Select(pillar => pillar.Value).Order(), PackContentKindRegistry.Pillars.Select(pillar => pillar.Value));
        Assert.All(kinds, kind => Assert.Equal(kind.Pillar, PackContentKindRegistry.PillarOf(kind.Name).Name));
        Assert.Equal(new PackPillarEntry("Booking", 13), PackContentKindRegistry.PillarOf("Bookable"));
        Assert.Equal(new PackPillarEntry("Navigation", 3), PackContentKindRegistry.PillarOf("ReleasedNavigationDefinition"));
    }

    [Fact]
    public void An_unknown_kind_is_refused_rather_than_defaulted()
    {
        Assert.Throws<KeyNotFoundException>(() => PackContentKindRegistry.Kind("GrantDefinition"));
        Assert.Throws<KeyNotFoundException>(() => PackContentKindRegistry.PillarOf("templatedefinition"));
    }

    [Fact]
    public void The_checked_in_export_is_the_registry_byte_for_byte()
    {
        Assert.True(
            PackContentKindRegistry.VerifyCheckedInExport(),
            "_shared/content-kinds/content-kinds.json is stale; regenerate it with "
            + "dotnet run --project tooling/content-kind-export -- _shared/content-kinds/content-kinds.json");
        Assert.Equal(PackContentKindRegistry.Export(), PackContentKindRegistry.LoadCheckedInExport());
    }

    [Fact]
    public void The_export_states_every_pillar_and_kind_with_its_status()
    {
        var bytes = PackContentKindRegistry.Export();
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.StartsWith("{\n  \"schemaVersion\": 1,\n  \"pillars\": [\n    {\n      \"name\": \"Records\",", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            PackContentKindRegistry.Pillars,
            root.GetProperty("pillars").EnumerateArray().Select(pillar => new PackPillarEntry(pillar.GetProperty("name").GetString()!, pillar.GetProperty("value").GetInt32())));
        Assert.Equal(
            PackContentKindRegistry.Kinds,
            root.GetProperty("kinds").EnumerateArray().Select(kind => new PackContentKindEntry(
                kind.GetProperty("name").GetString()!,
                kind.GetProperty("value").GetInt32(),
                kind.GetProperty("pillar").GetString()!,
                kind.GetProperty("status").GetString() switch
                {
                    "shipped" => PackContentKindStatus.Shipped,
                    "reserved" => PackContentKindStatus.Reserved,
                    var other => throw new InvalidDataException(other),
                })));
        Assert.Equal(4, root.GetProperty("kinds")[0].EnumerateObject().Count());
    }
}
