using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Foundation.DataExchange;

/// <summary>The stable identities of the Harborline Tabular Mapping Profile.</summary>
public static class TabularMappingProfile
{
    public const string Family = "hl:tabular-mapping/v1";
    public const string SchemaUri = "https://schemas.harborline.software/mapping/tabular/v1";
    public const int SupportedMajorVersion = 1;

    internal static readonly HashSet<string> ExtensionTerms = new(StringComparer.Ordinal)
    {
        "hl:operation",
        "hl:entityType",
        "hl:identity",
        "hl:target",
        "hl:role",
        "hl:matchPolicy",
        "hl:transform",
        "hl:onInvalid",
        "hl:onMissing",
        "hl:errorPolicy",
    };
}

/// <summary>A versioned canonical Records contract and root path.</summary>
public sealed record CanonicalTarget(string Contract, string RootPath);

/// <summary>One supported CSVW column with its canonical target pointer.</summary>
public sealed record MappingColumn(
    string Name,
    string Datatype,
    bool Required,
    string Target,
    string? Default = null,
    IReadOnlyList<string>? Null = null,
    string? Separator = null,
    IReadOnlyDictionary<string, string>? Extensions = null);

/// <summary>The portable JSON mapping contract admitted by Data Exchange.</summary>
public sealed record TabularMappingDocument(
    string Profile,
    string SchemaUri,
    string Version,
    CanonicalTarget Target,
    IReadOnlyList<MappingColumn> Columns,
    IReadOnlyDictionary<string, string> Extensions,
    string SourceUrl = "urn:harborline:source:bound");

/// <summary>Portable CSVW metadata syntax for the constrained Harborline mapping profile.</summary>
public static class TabularMappingJson
{
    private const string HarborlineNamespace = "https://schemas.harborline.software/ns/mapping#";
    private static readonly HashSet<string> ReservedRootTerms = new(StringComparer.Ordinal)
    {
        "hl:profile", "hl:schemaUri", "hl:version", "hl:target",
    };

    public static string Serialize(TabularMappingDocument mapping)
    {
        _ = TabularMappingAdmission.Validate(mapping);
        var columns = new JsonArray();
        foreach (var column in mapping.Columns)
        {
            var node = new JsonObject
            {
                ["name"] = column.Name,
                ["datatype"] = column.Datatype,
                ["required"] = column.Required,
                ["hl:target"] = column.Target,
            };
            if (column.Default is not null) node["default"] = column.Default;
            if (column.Null is not null) node["null"] = new JsonArray(column.Null.Select(value => JsonValue.Create(value)).ToArray());
            if (column.Separator is not null) node["separator"] = column.Separator;
            foreach (var extension in (column.Extensions ?? new Dictionary<string, string>())
                .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                node[extension.Key] = extension.Value;
            }
            columns.Add(node);
        }

        var root = new JsonObject
        {
            ["@context"] = new JsonArray(
                "http://www.w3.org/ns/csvw",
                new JsonObject { ["hl"] = HarborlineNamespace }),
            ["url"] = mapping.SourceUrl,
            ["tableSchema"] = new JsonObject { ["columns"] = columns },
            ["hl:profile"] = mapping.Profile,
            ["hl:schemaUri"] = mapping.SchemaUri,
            ["hl:version"] = mapping.Version,
            ["hl:target"] = new JsonObject
            {
                ["contract"] = mapping.Target.Contract,
                ["rootPath"] = mapping.Target.RootPath,
            },
        };
        foreach (var extension in mapping.Extensions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            root[extension.Key] = extension.Value;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    public static TabularMappingDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var target = root.GetProperty("hl:target");
        var columns = root.GetProperty("tableSchema").GetProperty("columns")
            .EnumerateArray()
            .Select(column => new MappingColumn(
                column.GetProperty("name").GetString()!,
                column.GetProperty("datatype").GetString()!,
                column.TryGetProperty("required", out var required) && required.GetBoolean(),
                column.GetProperty("hl:target").GetString()!,
                column.TryGetProperty("default", out var defaultValue) ? defaultValue.GetString() : null,
                column.TryGetProperty("null", out var nullValues)
                    ? nullValues.EnumerateArray().Select(value => value.GetString()!).ToArray()
                    : null,
                column.TryGetProperty("separator", out var separator) ? separator.GetString() : null,
                column.EnumerateObject()
                    .Where(property => property.Name.StartsWith("hl:", StringComparison.Ordinal)
                        && property.Name != "hl:target")
                    .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal)))
            .ToArray();
        var mapping = new TabularMappingDocument(
            root.GetProperty("hl:profile").GetString()!,
            root.GetProperty("hl:schemaUri").GetString()!,
            root.GetProperty("hl:version").GetString()!,
            new(target.GetProperty("contract").GetString()!, target.GetProperty("rootPath").GetString()!),
            columns,
            root.EnumerateObject()
                .Where(property => property.Name.StartsWith("hl:", StringComparison.Ordinal)
                    && !ReservedRootTerms.Contains(property.Name))
                .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal),
            root.GetProperty("url").GetString()!);
        return TabularMappingAdmission.Validate(mapping);
    }
}

/// <summary>One terminal mapping-admission refusal.</summary>
public sealed record DataExchangeRefusal(string Code, string Pointer);

/// <summary>All mapping refusals produced by one terminal admission.</summary>
public sealed class DataExchangeAdmissionException(IReadOnlyList<DataExchangeRefusal> refusals)
    : Exception("The Data Exchange definition was refused.")
{
    public IReadOnlyList<DataExchangeRefusal> Refusals { get; } = refusals;
}

/// <summary>Validates the named mapping profile without reinterpreting CSVW terms.</summary>
public static class TabularMappingAdmission
{
    public static TabularMappingDocument Validate(TabularMappingDocument mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var refusals = new List<DataExchangeRefusal>();
        if (!StringComparer.Ordinal.Equals(mapping.Profile, TabularMappingProfile.Family))
        {
            refusals.Add(new("mapping.profile_unknown", "/profile"));
        }
        if (!StringComparer.Ordinal.Equals(mapping.SchemaUri, TabularMappingProfile.SchemaUri))
        {
            refusals.Add(new("mapping.schema_mismatch", "/schemaUri"));
        }
        var versionParts = mapping.Version.Split('.', StringSplitOptions.None);
        if (versionParts.Length != 3
            || !versionParts.All(part => int.TryParse(part, out _))
            || !int.TryParse(versionParts[0], out var major)
            || major != TabularMappingProfile.SupportedMajorVersion)
        {
            refusals.Add(new("mapping.major_unsupported", "/version"));
        }
        if (string.IsNullOrWhiteSpace(mapping.Target.Contract)
            || !mapping.Target.Contract.StartsWith("records.", StringComparison.Ordinal))
        {
            refusals.Add(new("mapping.target_not_canonical", "/target/contract"));
        }
        foreach (var term in mapping.Extensions.Keys
            .Concat(mapping.Columns.SelectMany(column => column.Extensions?.Keys ?? []))
            .Distinct(StringComparer.Ordinal))
        {
            if (!TabularMappingProfile.ExtensionTerms.Contains(term))
            {
                refusals.Add(new("mapping.extension_unknown", $"/extensions/{Escape(term)}"));
            }
        }
        if (mapping.Columns.Count == 0)
        {
            refusals.Add(new("mapping.columns_required", "/columns"));
        }
        if (!Uri.TryCreate(mapping.SourceUrl, UriKind.Absolute, out _))
        {
            refusals.Add(new("mapping.source_url_invalid", "/url"));
        }
        if (refusals.Count > 0)
        {
            throw new DataExchangeAdmissionException(refusals);
        }
        return mapping;
    }

    private static string Escape(string value) => value
        .Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);
}
