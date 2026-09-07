using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Projection;

internal static class FormProjectionRecovery
{
    internal static async ValueTask<FormProjectionRecoveryResult> RecoverAsync(
        IFormSubmissionTransactionStore submissions,
        IFormProjectionSink projections,
        int maximumDeliveries,
        CancellationToken cancellationToken)
    {
        var rows = await submissions.LeasePendingAsync(maximumDeliveries, cancellationToken).ConfigureAwait(false);
        var completed = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            try
            {
                var result = await projections.DeliverAsync(row, cancellationToken).ConfigureAwait(false);
                await submissions.CompleteProjectionAsync(row.OutboxId, result.Skips, cancellationToken).ConfigureAwait(false);
                completed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                for (var releaseIndex = index; releaseIndex < rows.Count; releaseIndex++)
                {
                    try { await submissions.ReleaseProjectionLeaseAsync(rows[releaseIndex].OutboxId, CancellationToken.None).ConfigureAwait(false); }
                    catch { }
                }
                throw;
            }
            catch { await submissions.RetryProjectionAsync(row.OutboxId, "form.engine.projection-pending", CancellationToken.None).ConfigureAwait(false); }
        }
        return new(rows.Count, completed, rows.Count - completed);
    }
}
