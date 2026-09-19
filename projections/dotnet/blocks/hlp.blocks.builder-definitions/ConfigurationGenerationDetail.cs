using System.Collections.ObjectModel;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Released read-only Form definition and bindings for both shared runtime lanes.</summary>
public static class ConfigurationGenerationDetail
{
    /// <summary>The neutral detail definition included in the platform package export.</summary>
    public static JsonElement Definition { get; } = JsonSerializer.SerializeToElement(new
    {
        formId = "platform.detail.configuration-generation",
        version = "1.0.0",
        title = Text("Effective configuration generation"),
        description = Text("Identifies the complete tenant configuration. Individual package versions are constituent references."),
        sections = new[]
        {
            new
            {
                id = "generation", title = Text("Effective generation"),
                fields = new[]
                {
                    Field("tenantKey", "Tenant"), Field("generationDigest", "Complete generation digest (SHA-256)"),
                    Field("activePackageKeys", "Active packages"), Field("packages", "Package versions, content and dependency closure"),
                    Field("ownership", "Definition ownership selections"), Field("platformContract", "Platform contract"),
                    Field("policies", "Applicable configuration policy references"),
                },
            },
        },
    });

    /// <summary>Produces only public reference values from the same immutable generation snapshot.</summary>
    public static IReadOnlyDictionary<string, string> Bind(ConfigurationGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        var references = generation.References;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenantKey"] = references.GetProperty("tenantKey").GetString()!,
            ["generationDigest"] = generation.Digest,
        };
        foreach (var key in new[] { "activePackageKeys", "packages", "ownership", "platformContract", "policies" })
            values[key] = references.GetProperty(key).GetRawText();
        return new ReadOnlyDictionary<string, string>(values);
    }

    private static object Text(string text) => new { defaultLocale = "en", values = new { en = text } };
    private static object Field(string name, string label) => new
    {
        name, label = Text(label), controlHint = "readonly", isSensitive = false, isReadable = true, readOnly = true,
    };
}
