using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Projection;

/// <summary>Reports projection fields that were skipped during delivery.</summary>
public sealed record FormProjectionDeliveryResult(IReadOnlyList<FormProjectionSkip> Skips)
{
    /// <summary>
    /// For an accepted command only: the stable code of a terminal refusal of the command by its execution (its binding,
    /// its evidence, its payload under the original operation, or an execution-time invariant; ADR-0110 decision 2). On the
    /// run that made the claim the engine settles the command <see cref="FormAcceptedCommandState.Refused"/> and never
    /// retries it; after a resumed claim, whose interrupted run may have committed the effect, it quarantines the command
    /// instead (<see cref="FormAcceptedCommandState.Quarantined"/>, review R2-M1). Null when the command completed or
    /// replayed. A failure that says nothing about the command (an infrastructure fault, the worker's own
    /// authorization) is thrown instead, and the command stays pending.
    /// </summary>
    public string? TerminalRefusal { get; init; }
}

/// <summary>
/// One durably accepted Records command handed to the host for execution (ADR-0110 decision 4, plan W7): the configured
/// worker identity that executes it, the delivery row, and the command as claimed. The submitter is the row's
/// <see cref="FormProjectionEnvelope.ActorId"/>, provenance only; nothing here authorizes acting as the submitter.
/// </summary>
/// <param name="WorkerId">The configured worker identity, the actual executor; never the submitter.</param>
/// <param name="Envelope">The delivery row with its protected accepted values and bound context.</param>
/// <param name="Command">The command, claimed for execution by <paramref name="WorkerId"/>.</param>
public sealed record FormAcceptedCommandDelivery(string WorkerId, FormProjectionEnvelope Envelope, FormAcceptedRecordCommand Command);

/// <summary>Delivers a committed form projection to the host's projection sink.</summary>
public interface IFormProjectionSink
{
    /// <summary>Delivers the projection to the host and returns the fields it skipped.</summary>
    ValueTask<FormProjectionDeliveryResult> DeliverAsync(
        FormProjectionEnvelope envelope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Review M2: true only for a sink that implements <see cref="DeliverAcceptedAsync"/>. The engine claims an accepted
    /// command only for such a sink, so a sink that cannot execute one never claims it and the command stays pending and
    /// cancellable. The default is false.
    /// </summary>
    bool ExecutesAcceptedCommands => false;

    /// <summary>
    /// Executes one claimed accepted Records command as <see cref="FormAcceptedCommandDelivery.WorkerId"/> (plan W7/W10):
    /// a conforming host maps the protected accepted values to the record body and calls the Records completion entry
    /// (<c>RecordWriteOperation.CompleteAcceptedAsync</c>) with the worker as executor, the submitter as provenance and the
    /// command's original operation id, and reports a terminal refusal through
    /// <see cref="FormProjectionDeliveryResult.TerminalRefusal"/>. It must be idempotent on the original operation, because
    /// recovery executes a claimed command again after an interruption. The default refuses to execute, so a sink that was
    /// not built for accepted commands leaves every one pending; it never falls back to <see cref="DeliverAsync"/>.
    /// </summary>
    ValueTask<FormProjectionDeliveryResult> DeliverAcceptedAsync(
        FormAcceptedCommandDelivery delivery,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This projection sink does not execute accepted Records commands.");
}
