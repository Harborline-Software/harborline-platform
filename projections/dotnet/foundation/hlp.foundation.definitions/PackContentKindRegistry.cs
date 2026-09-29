using System.Text.Encodings.Web;
using System.Text.Json;

namespace Harborline.Foundation.Definitions;

/// <summary>Whether the api's transport already ships a content kind, or the platform only holds its value.</summary>
public enum PackContentKindStatus
{
    /// <summary>The api's <c>PackContentKind</c> ships this kind at this value. The value never moves.</summary>
    Shipped,

    /// <summary>
    /// A platform producer emits this kind but the api does not ship it yet. The value is held: no other kind
    /// takes it, and the api adds the kind at exactly this value.
    /// </summary>
    Reserved,
}

/// <summary>One customer pillar: the stable token and wire value of the api's <c>PackPillar</c>.</summary>
/// <param name="Name">The api's <c>PackPillar</c> member name.</param>
/// <param name="Value">The wire value.</param>
public sealed record PackPillarEntry(string Name, int Value);

/// <summary>One transport content kind with its wire value, its pillar and whether the api ships it.</summary>
/// <param name="Name">The api's <c>PackContentKind</c> member name, or the name a reserved kind will take.</param>
/// <param name="Value">The wire value.</param>
/// <param name="Pillar">The <see cref="PackPillarEntry.Name"/> the kind groups under.</param>
/// <param name="Status">Whether the api ships the kind or the platform holds its value.</param>
public sealed record PackContentKindEntry(string Name, int Value, string Pillar, PackContentKindStatus Status);

/// <summary>
/// The one registry of pack content kinds and pillars (T-738). Shipped values are the api's
/// (<c>packages/foundation-packs/Model/PackEnums.cs</c> and <c>Graph/PackPillar.cs</c>) and never renumber
/// (owner ruling, 2026-09-29). Every platform pack-identity constant must agree with this table; the
/// architecture drift gate enforces it. The checked-in <c>_shared/packs/content-kinds/content-kinds.export.json</c>
/// is <see cref="Export"/>'s bytes and ships in the package for the api to consume.
/// </summary>
public static class PackContentKindRegistry
{
    private const string ResourceName = "Harborline.Foundation.Definitions.content-kinds.export.json";

    /// <summary>The pillars, in wire-value order.</summary>
    public static IReadOnlyList<PackPillarEntry> Pillars { get; } =
    [
        new("Records", 0),
        new("Forms", 1),
        new("Automations", 2),
        new("Navigation", 3),
        new("Rules", 4),
        new("Settings", 5),
        new("Documents", 6),
        new("Taxonomy", 7),
        new("Reports", 8),
        new("Views", 9),
        new("Scheduling", 10),
        new("DataExchange", 11),
        new("Layout", 12),
        new("Booking", 13),
        new("Other", 99),
    ];

    /// <summary>The content kinds, in wire-value order.</summary>
    public static IReadOnlyList<PackContentKindEntry> Kinds { get; } =
    [
        new("FormDefinition", 0, "Forms", PackContentKindStatus.Shipped),
        new("WorkflowDefinition", 1, "Automations", PackContentKindStatus.Shipped),
        new("StandardsCatalog", 2, "Rules", PackContentKindStatus.Shipped),
        new("NavWorkspaceConfig", 3, "Navigation", PackContentKindStatus.Shipped),
        new("CascadeDefaults", 4, "Settings", PackContentKindStatus.Shipped),
        new("AssetTypeDefinition", 5, "Records", PackContentKindStatus.Shipped),
        new("TerminologyOverride", 6, "Settings", PackContentKindStatus.Shipped),
        new("TemplateDefinition", 7, "Documents", PackContentKindStatus.Shipped),
        new("TaxonomyDefinition", 8, "Taxonomy", PackContentKindStatus.Shipped),
        new("ReportDefinition", 9, "Reports", PackContentKindStatus.Shipped),
        new("ViewDefinition", 10, "Views", PackContentKindStatus.Shipped),
        new("ScheduleDefinition", 11, "Scheduling", PackContentKindStatus.Shipped),
        new("DataExchangeDefinition", 12, "DataExchange", PackContentKindStatus.Shipped),
        new("StandingRuleDefinition", 13, "Rules", PackContentKindStatus.Shipped),
        new("RoleDefinition", 14, "Other", PackContentKindStatus.Shipped),
        new("AuthorizationCapabilityBinding", 15, "Other", PackContentKindStatus.Shipped),
        new("RecordType", 16, "Other", PackContentKindStatus.Shipped),
        new("Layout", 17, "Layout", PackContentKindStatus.Shipped),
        new("Resource", 18, "Booking", PackContentKindStatus.Shipped),
        new("Bookable", 19, "Booking", PackContentKindStatus.Shipped),
        // DES-0026: Assistance owns no pillar of its own.
        new("AssistanceDefinition", 20, "Other", PackContentKindStatus.Reserved),
        // Owner ruling 2026-09-29: the next free value; 18 is Resource's.
        new("ReleasedNavigationDefinition", 21, "Navigation", PackContentKindStatus.Reserved),
    ];

    private static readonly Dictionary<string, PackContentKindEntry> KindsByName =
        Kinds.ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, PackPillarEntry> PillarsByName =
        Pillars.ToDictionary(pillar => pillar.Name, StringComparer.Ordinal);

    /// <summary>Returns the named content kind; an unknown name throws <see cref="KeyNotFoundException"/>.</summary>
    public static PackContentKindEntry Kind(string name) => KindsByName[name];

    /// <summary>Returns the pillar the named content kind groups under.</summary>
    public static PackPillarEntry PillarOf(string kindName) => PillarsByName[Kind(kindName).Pillar];

    /// <summary>Exports the registry as indented, newline-terminated UTF-8 JSON for the api to consume.</summary>
    public static byte[] Export()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = true,
                   NewLine = "\n",
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("pillars");
            foreach (var pillar in Pillars)
            {
                writer.WriteStartObject();
                writer.WriteString("name", pillar.Name);
                writer.WriteNumber("value", pillar.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("kinds");
            foreach (var kind in Kinds)
            {
                writer.WriteStartObject();
                writer.WriteString("name", kind.Name);
                writer.WriteNumber("value", kind.Value);
                writer.WriteString("pillar", kind.Pillar);
                writer.WriteString("status", kind.Status == PackContentKindStatus.Reserved ? "reserved" : "shipped");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    /// <summary>Loads the checked-in export embedded in the package assembly.</summary>
    public static byte[] LoadCheckedInExport()
    {
        using var stream = typeof(PackContentKindRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException(ResourceName);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Reports whether the embedded checked-in export is byte-identical to <see cref="Export"/>.</summary>
    public static bool VerifyCheckedInExport() => Export().AsSpan().SequenceEqual(LoadCheckedInExport());
}
