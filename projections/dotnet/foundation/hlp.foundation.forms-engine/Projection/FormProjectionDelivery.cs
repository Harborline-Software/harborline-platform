using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Projection;

public sealed record FormProjectionDeliveryResult(IReadOnlyList<FormProjectionSkip> Skips);

public interface IFormProjectionSink
{
    ValueTask<FormProjectionDeliveryResult> DeliverAsync(
        FormProjectionEnvelope envelope,
        CancellationToken cancellationToken = default);
}
