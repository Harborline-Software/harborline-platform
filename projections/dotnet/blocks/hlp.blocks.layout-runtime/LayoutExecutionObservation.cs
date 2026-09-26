using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>One step of a run's execution trace, as the execution runtime recorded it (DES-0056 execution-runtime-ck-4).</summary>
/// <param name="Ordinal">The recorded ordinal.</param>
/// <param name="Phase">The recorded phase.</param>
/// <param name="Status">The recorded status, verbatim.</param>
public sealed record LayoutExecutionTraceStep(int Ordinal, string Phase, string Status);

/// <summary>
/// DES-0056 execution-runtime-ck-3 as a Layout observe block receives it from the host's authorized read: run
/// identity, status, the applicable Access decision identity and the execution trace. Layout renders it verbatim
/// and recomputes none of it (layout-eng-30).
/// </summary>
/// <param name="RunId">The stable run identity.</param>
/// <param name="Status">The run status, verbatim from the execution runtime.</param>
/// <param name="AccessDecisionId">The applicable Access decision's stable identity; null when none was recorded.</param>
/// <param name="Trace">The execution trace, in recorded order. It is distinct from Access's four-stage trace.</param>
public sealed record LayoutExecutionReceipt(string RunId, string Status, string? AccessDecisionId, IReadOnlyList<LayoutExecutionTraceStep> Trace);

/// <summary>What following a receipt's Access decision link yields. Each is rendered distinctly.</summary>
public enum LayoutAccessEvidence
{
    /// <summary>The receipt records no Access decision, or Access refused before deciding: there is nothing to read.</summary>
    Absent,
    /// <summary>The receipt links a decision, but no trace is stored for it.</summary>
    Missing,
    /// <summary>The reader may not read the trace; even its existence is hidden.</summary>
    Forbidden,
    /// <summary>A trace is stored but is not four ordered stages at a supported version.</summary>
    Malformed,
    /// <summary>Access's four ordered, versioned stages.</summary>
    Valid,
}

/// <summary>The Access trace a Layout lane renders; the lane derives nothing from it.</summary>
/// <param name="Evidence">Which evidence the read yielded.</param>
/// <param name="Version">The stored trace version; null unless valid.</param>
/// <param name="Stages">Access's four stages in order; empty unless valid.</param>
/// <param name="DecidingGrant">The deciding grant, by its declared fact kind; null when none was recorded.</param>
public sealed record LayoutAccessTrace(LayoutAccessEvidence Evidence, int? Version, IReadOnlyList<AuthorizationTraceStep> Stages, string? DecidingGrant);

/// <summary>
/// DES-0052 layout-eng-30: follows a receipt's stable link to Access's decision and classifies what the read
/// returns. Access decides and stores; this reads, and computes neither a verdict nor execution state.
/// </summary>
public static class LayoutExecutionObservation
{
    /// <summary>The declared fact kind naming the deciding grant in Access's effective-roles stage.</summary>
    public const string DecidingGrantFact = "deciding:grant:";

    private static readonly string[] Stages = [AuthorizationDecisionEvidence.ActStage, AuthorizationDecisionEvidence.EffectiveRolesStage,
        AuthorizationDecisionEvidence.StandingsStage, AuthorizationDecisionEvidence.VerdictStage];

    /// <summary>Reads the linked Access trace. A receipt with no decision is absent and is never read.</summary>
    /// <param name="receipt">The authorized receipt.</param>
    /// <param name="read">The host's authorized Access trace read, keyed by decision identity.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<LayoutAccessTrace> ReadAccessTraceAsync(LayoutExecutionReceipt receipt,
        Func<string, CancellationToken, ValueTask<AuthorizationTraceRead>> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(read);
        return receipt.AccessDecisionId is { } decision
            ? Classify(await read(decision, cancellationToken).ConfigureAwait(false))
            : new(LayoutAccessEvidence.Absent, null, [], null);
    }

    /// <summary>Classifies one Access trace read without re-deciding it.</summary>
    /// <param name="read">The read Access returned.</param>
    public static LayoutAccessTrace Classify(AuthorizationTraceRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var evidence = read.Availability switch
        {
            AuthorizationTraceAvailability.NotAvailable => LayoutAccessEvidence.Missing,
            AuthorizationTraceAvailability.Refused => LayoutAccessEvidence.Forbidden,
            // A guard refused before the decider ran: no decision was recorded (AuthorizationTraceReader).
            AuthorizationTraceAvailability.PreDecisionRefusal => LayoutAccessEvidence.Absent,
            AuthorizationTraceAvailability.Available when WellFormed(read) => LayoutAccessEvidence.Valid,
            _ => LayoutAccessEvidence.Malformed,
        };
        if (evidence != LayoutAccessEvidence.Valid) return new(evidence, null, [], null);
        // Only the declared kind names a grant; other deciding facts (a standing, `none`, a role's marker) do not.
        var grant = read.Steps[1].Facts.FirstOrDefault(fact => fact.StartsWith(DecidingGrantFact, StringComparison.Ordinal));
        return new(evidence, read.Version, read.Steps, grant?[DecidingGrantFact.Length..]);
    }

    private static bool WellFormed(AuthorizationTraceRead read) =>
        read.Version is >= 1 and <= AuthorizationDecisionEvidence.CurrentVersion
        && read.Steps.Select(step => (step.Ordinal, step.Stage)).SequenceEqual(Stages.Select((stage, index) => (index + 1, stage)));
}
