using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Projection;

/// <summary>Represents the form projection delivery result contract used by this package.</summary>
/// <param name="Skips">The Skips value.</param>
public sealed record FormProjectionDeliveryResult(IReadOnlyList<FormProjectionSkip> Skips);

/// <summary>Represents the iform projection sink contract used by this package.</summary>
public interface IFormProjectionSink
{
    /// <summary>Delivers a projection envelope and reports fields that could not be projected.</summary>
    ValueTask<FormProjectionDeliveryResult> DeliverAsync(
        FormProjectionEnvelope envelope,
        CancellationToken cancellationToken = default);
}
