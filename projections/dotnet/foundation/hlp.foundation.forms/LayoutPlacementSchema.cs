using System.Text.Json;

namespace Harborline.Foundation.Forms;

/// <summary>Forms admission adapter over the platform-owned Layout placement schema.</summary>
public static class LayoutPlacementSchema
{
    private static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> Ranges = ReadRanges();

    /// <summary>Reads a numeric member's inclusive range from the shared platform schema.</summary>
    public static (int Minimum, int Maximum) Range(string member) => Ranges[member];

    internal static bool Contains(string member, int value)
    {
        var range = Range(member);
        return value >= range.Minimum && value <= range.Maximum;
    }

    private static Dictionary<string, (int Minimum, int Maximum)> ReadRanges()
    {
        using var stream = typeof(LayoutPlacementSchema).Assembly.GetManifestResourceStream("Harborline.Layout.PlacementSchema")
            ?? throw new InvalidOperationException("The platform placement schema is missing.");
        using var document = JsonDocument.Parse(stream);
        var ranges = new Dictionary<string, (int Minimum, int Maximum)>(StringComparer.Ordinal);
        foreach (var kind in new[] { "placement", "container" })
        foreach (var property in document.RootElement.GetProperty("$defs").GetProperty(kind).GetProperty("properties").EnumerateObject())
            ranges.Add(property.Name, (property.Value.GetProperty("minimum").GetInt32(), property.Value.GetProperty("maximum").GetInt32()));
        return ranges;
    }
}
