using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Identifies a schema-governed numeric Layout member.</summary>
public enum LayoutNumericMember
{
    /// <summary>A member's track span.</summary>
    Span,
    /// <summary>A member's growth weight.</summary>
    Grow,
    /// <summary>A container's column count.</summary>
    ColumnCount,
    /// <summary>A container's gap token.</summary>
    Gap,
}

/// <summary>Represents an inclusive integer range.</summary>
/// <param name="Minimum">The inclusive minimum.</param>
/// <param name="Maximum">The inclusive maximum.</param>
public readonly record struct LayoutNumericRange(int Minimum, int Maximum)
{
    /// <summary>Returns whether the value is inside the inclusive range.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is in range.</returns>
    public bool Contains(int value) => value >= Minimum && value <= Maximum;
}

/// <summary>The platform-owned Layout schema. Renderers and admission read this one range authority.</summary>
public static class LayoutDefinitionSchema
{
    private static readonly byte[] SchemaBytes = ReadSchema();
    private static readonly Dictionary<LayoutNumericMember, LayoutNumericRange> Ranges = ReadRanges();
    private static readonly IReadOnlyList<string> Tokens = Array.AsReadOnly(ReadCollapseTokens());

    private static byte[] ReadSchema()
    {
        using var stream = typeof(LayoutDefinitionSchema).Assembly.GetManifestResourceStream("Harborline.Layout.PlacementSchema")
            ?? throw new InvalidOperationException("The platform placement schema is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Gets an immutable copy of the canonical Layout schema document.</summary>
    public static byte[] CanonicalJson => (byte[])SchemaBytes.Clone();

    /// <summary>Gets the closed set of portable container collapse tokens.</summary>
    public static IReadOnlyList<string> CollapseTokens => Tokens;

    /// <summary>Gets the inclusive range governed by the Layout schema.</summary>
    /// <param name="member">The governed member.</param>
    /// <returns>The member's inclusive range.</returns>
    public static LayoutNumericRange Numeric(LayoutNumericMember member) => Ranges[member];

    private static Dictionary<LayoutNumericMember, LayoutNumericRange> ReadRanges()
    {
        using var schema = JsonDocument.Parse(SchemaBytes);
        var definitions = schema.RootElement.GetProperty("$defs");
        return new Dictionary<LayoutNumericMember, LayoutNumericRange>
        {
            [LayoutNumericMember.Span] = Read(definitions, "placement", "span"),
            [LayoutNumericMember.Grow] = Read(definitions, "placement", "grow"),
            [LayoutNumericMember.ColumnCount] = Read(definitions, "container", "column_count"),
            [LayoutNumericMember.Gap] = Read(definitions, "container", "gap"),
        };
    }

    private static string[] ReadCollapseTokens()
    {
        using var schema = JsonDocument.Parse(SchemaBytes);
        return schema.RootElement.GetProperty("$defs").GetProperty("collapse_token").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static LayoutNumericRange Read(JsonElement definitions, string definition, string property)
    {
        var value = definitions.GetProperty(definition).GetProperty("properties").GetProperty(property);
        return new(value.GetProperty("minimum").GetInt32(), value.GetProperty("maximum").GetInt32());
    }
}
