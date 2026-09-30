using System.Reflection;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.Reports.Definitions;
using Harborline.Foundation.Assistance;
using Harborline.Foundation.DataExchange;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.Documents;
using Harborline.Foundation.Taxonomy;

using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// T-738 drift gate: every platform pack-identity constant states the value the one registry
/// (<see cref="PackContentKindRegistry"/>) gives its kind or pillar. The scan finds every constant on a
/// <c>*PackIdentity</c> type and every <c>PackContentKind</c> or <c>PackPillar</c> enum in a production
/// assembly, so a new wire value cannot be minted beside the registry.
/// </summary>
public sealed class PackContentKindDriftArchitectureTests
{
    // Content-kind constants and the registry kind each one is.
    private static readonly (string Constant, int Value, string Kind)[] ContentKinds =
    [
        ("Harborline.Foundation.Documents.DocumentsPackIdentity.ContentKind", DocumentsPackIdentity.ContentKind, "TemplateDefinition"),
        ("Harborline.Foundation.Taxonomy.TaxonomyPackIdentity.ContentKind", TaxonomyPackIdentity.ContentKind, "TaxonomyDefinition"),
        ("Harborline.Foundation.DataExchange.DataExchangePackIdentity.ContentKind", DataExchangePackIdentity.ContentKind, "DataExchangeDefinition"),
        ("Harborline.Foundation.Assistance.AssistancePackIdentity.ContentKind", AssistancePackIdentity.ContentKind, "AssistanceDefinition"),
        ("Harborline.Blocks.BuilderDefinitions.LayoutPackIdentity.ContentKind", LayoutPackIdentity.ContentKind, "Layout"),
        ("Harborline.Blocks.BuilderDefinitions.ReleasedNavigationPackIdentity.ContentKind", ReleasedNavigationPackIdentity.ContentKind, "ReleasedNavigationDefinition"),
        ("Harborline.Blocks.Calendar.Booking.BookingPackIdentity.ResourceContentKind", BookingPackIdentity.ResourceContentKind, "Resource"),
        ("Harborline.Blocks.Calendar.Booking.BookingPackIdentity.BookableContentKind", BookingPackIdentity.BookableContentKind, "Bookable"),
        ("Harborline.Blocks.Reports.Definitions.ReportPackIdentity.ContentKind", ReportPackIdentity.ContentKind, "ReportDefinition"),
    ];

    // Pillar ("primitive bucket") constants and the registry kind whose pillar each one states.
    private static readonly (string Constant, int Value, string Kind)[] Pillars =
    [
        ("Harborline.Blocks.BuilderDefinitions.LayoutPackIdentity.Primitive", LayoutPackIdentity.Primitive, "Layout"),
        ("Harborline.Blocks.BuilderDefinitions.ReleasedNavigationPackIdentity.Primitive", ReleasedNavigationPackIdentity.Primitive, "ReleasedNavigationDefinition"),
        ("Harborline.Blocks.Calendar.Booking.BookingPackIdentity.Primitive", BookingPackIdentity.Primitive, "Resource"),
    ];

    [Fact]
    public void Every_content_kind_constant_states_the_registry_value()
    {
        var drift = ContentKinds
            .Where(row => row.Value != PackContentKindRegistry.Kind(row.Kind).Value)
            .Select(row => $"{row.Constant} = {row.Value}; the registry gives {row.Kind} = {PackContentKindRegistry.Kind(row.Kind).Value}")
            .ToList();

        Assert.True(drift.Count == 0, "CONTENT KIND DRIFT:\n" + string.Join("\n", drift));
    }

    [Fact]
    public void Every_pillar_constant_states_the_registry_pillar()
    {
        var drift = Pillars
            .Where(row => row.Value != PackContentKindRegistry.PillarOf(row.Kind).Value)
            .Select(row =>
            {
                var pillar = PackContentKindRegistry.PillarOf(row.Kind);
                return $"{row.Constant} = {row.Value}; the registry groups {row.Kind} under {pillar.Name} = {pillar.Value}";
            })
            .ToList();

        Assert.True(drift.Count == 0, "PILLAR DRIFT:\n" + string.Join("\n", drift));
    }

    [Fact]
    public void Every_pack_identity_constant_in_a_production_assembly_is_checked_against_the_registry()
    {
        var checkedConstants = ContentKinds.Concat(Pillars).Select(row => row.Constant).ToHashSet(StringComparer.Ordinal);
        var found = ProductionTypes()
            .Where(type => type.Name.EndsWith("PackIdentity", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(int))
                .Select(field => $"{type.FullName}.{field.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var unregistered = found.Except(checkedConstants).Order(StringComparer.Ordinal).ToList();
        var stale = checkedConstants.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            unregistered.Count == 0,
            "UNREGISTERED WIRE VALUE: add the kind to PackContentKindRegistry and the constant to this gate:\n"
            + string.Join("\n", unregistered));
        Assert.True(stale.Count == 0, "STALE GATE ROW: these constants no longer exist:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void Every_content_kind_or_pillar_enum_in_a_production_assembly_agrees_with_the_registry()
    {
        var drift = new List<string>();
        foreach (var type in ProductionTypes().Where(type => type.IsEnum))
        {
            IReadOnlyDictionary<string, int>? expected = type.Name switch
            {
                "PackContentKind" => PackContentKindRegistry.Kinds.ToDictionary(kind => kind.Name, kind => kind.Value, StringComparer.Ordinal),
                "PackPillar" => PackContentKindRegistry.Pillars.ToDictionary(pillar => pillar.Name, pillar => pillar.Value, StringComparer.Ordinal),
                _ => null,
            };
            if (expected is null) continue;

            foreach (var name in Enum.GetNames(type))
            {
                var value = Convert.ToInt32(Enum.Parse(type, name), System.Globalization.CultureInfo.InvariantCulture);
                if (!expected.TryGetValue(name, out var registered) || registered != value)
                    drift.Add($"{type.FullName}.{name} = {value}");
            }
        }

        Assert.True(drift.Count == 0, "ENUM DRIFT:\n" + string.Join("\n", drift));
    }

    private static IEnumerable<Type> ProductionTypes() =>
        TierDirectionArchitectureTests.LoadTierAssemblies().Values
            .SelectMany(tier => tier.Values)
            .SelectMany(LoadableTypes);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
