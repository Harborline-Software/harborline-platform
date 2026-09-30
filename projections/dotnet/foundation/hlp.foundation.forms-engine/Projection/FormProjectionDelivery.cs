using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Projection;

/// <summary>Reports projection fields that were skipped during delivery.</summary>
public sealed record FormProjectionDeliveryResult(IReadOnlyList<FormProjectionSkip> Skips);

/// <summary>Delivers a committed form projection to the host's projection sink.</summary>
public interface IFormProjectionSink
{
    /// <summary>Delivers the projection to the host and returns the fields it skipped.</summary>
    ValueTask<FormProjectionDeliveryResult> DeliverAsync(
        FormProjectionEnvelope envelope,
        CancellationToken cancellationToken = default);
}
