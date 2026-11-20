using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.UIAdapters.Blazor.Components.ReportAuthoring;

/// <summary>Serializes a host-owned draft in the shared Reports wire shape.</summary>
public static class ReportDraftCodec
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Serializes the draft without taking a dependency on the Reports producer.</summary>
    /// <param name="draft">The authored draft.</param>
    /// <returns>The JSON definition.</returns>
    public static string Serialize(ReportDraft draft) => JsonSerializer.Serialize(draft, Options);
}
