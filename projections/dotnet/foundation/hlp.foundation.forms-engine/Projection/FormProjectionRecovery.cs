using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Projection;

internal static class FormProjectionRecovery
{
    // Leases pending rows across tenants, fewest attempts first (review M2), so a batch is bounded and a row that keeps
    // failing never starves later ones. An ordinary projection of a form without a target is delivered to the sink as
    // before. A row that carries an accepted Records command is executed by runAccepted as the configured worker under the
    // store's claim; with no executing worker it is not leased at all. A row of a targeted form without a command (a role
    // target, or a row written before revision 16) is never recovered (review I-1, fail closed): delivering it outside its
    // submitter's own request would act as the submitter, which ADR-0110 rejects; only its submitter's own replay redelivers
    // it (review R2-L2). A quarantined command is never leased (review R2-M1); a run that quarantines one counts it pending.
    internal static async ValueTask<FormProjectionRecoveryResult> RecoverAsync(
        IFormSubmissionTransactionStore submissions,
        IFormProjectionSink projections,
        int maximumDeliveries,
        CancellationToken cancellationToken,
        Func<FormProjectionEnvelope, CancellationToken, ValueTask<FormAcceptedCommandRun>>? runAccepted = null)
    {
        var kinds = runAccepted is null
            ? FormProjectionLeaseKinds.Untargeted
            : FormProjectionLeaseKinds.Untargeted | FormProjectionLeaseKinds.AcceptedCommands;
        // A store without the command port holds no accepted command; its targeted rows are released untouched below.
        var rows = submissions is IFormAcceptedCommandStore commands
            ? await commands.LeasePendingAsync(maximumDeliveries, kinds, cancellationToken).ConfigureAwait(false)
            : await submissions.LeasePendingAsync(maximumDeliveries, cancellationToken).ConfigureAwait(false);
        var completed = 0;
        var settled = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            try
            {
                if (row.AcceptedCommand is null && row.RecordTypeId is not null)
                {
                    await submissions.ReleaseProjectionLeaseAsync(row.OutboxId, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }
                if (row.AcceptedCommand is not null)
                {
                    if (runAccepted is null)
                    {
                        await submissions.ReleaseProjectionLeaseAsync(row.OutboxId, CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    switch (await runAccepted(row, cancellationToken).ConfigureAwait(false))
                    {
                        case FormAcceptedCommandRun.Completed: completed++; break;
                        case FormAcceptedCommandRun.Refused or FormAcceptedCommandRun.Settled: settled++; break;
                    }
                    continue;
                }
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
        return new(rows.Count, completed, rows.Count - completed - settled) { Settled = settled };
    }
}
