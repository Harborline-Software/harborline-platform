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
    IReadOnlyDictionary<string, string> Extensions);

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
        if (!Version.TryParse(mapping.Version, out var version)
            || version.Major != TabularMappingProfile.SupportedMajorVersion)
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
