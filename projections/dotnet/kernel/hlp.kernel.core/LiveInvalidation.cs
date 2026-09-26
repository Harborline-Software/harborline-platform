using System.Text.Json;

namespace Harborline.Kernel.Core;

/// <summary>Identifies a resource invalidated on a live channel.</summary>
public sealed record LiveInvalidation(string ResourceId, long Version);

/// <summary>Serializes live invalidations for future runtime-channel producers.</summary>
public static class LiveInvalidationWire
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static byte[] Serialize(LiveInvalidation invalidation)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidation.ResourceId);
        return JsonSerializer.SerializeToUtf8Bytes(invalidation, Options);
    }
}
