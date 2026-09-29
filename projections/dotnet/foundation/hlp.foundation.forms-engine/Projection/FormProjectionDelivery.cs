using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Projection;

/// <inheritdoc />
public sealed record FormProjectionDeliveryResult(IReadOnlyList<FormProjectionSkip> Skips);

/// <inheritdoc />
public interface IFormProjectionSink
{
    /// <inheritdoc />
    ValueTask<FormProjectionDeliveryResult> DeliverAsync(
        FormProjectionEnvelope envelope,
        CancellationToken cancellationToken = default);
}
