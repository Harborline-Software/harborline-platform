using System.Diagnostics.Metrics;

namespace Harborline.Blocks.Scheduling;

internal static class SchedulingTelemetry
{
    private static readonly Meter Meter = new("Harborline.Scheduling");
    private static readonly Counter<long> AdmissionRefusals = Meter.CreateCounter<long>("scheduling.admission.refusals");
    private static readonly Histogram<double> PlanDuration = Meter.CreateHistogram<double>("scheduling.plan.duration_ms");
    private static readonly Counter<long> PlanUnassigned = Meter.CreateCounter<long>("scheduling.plan.unassigned");
    private static readonly Counter<long> CommitStaleRefusals = Meter.CreateCounter<long>("scheduling.commit.stale_refusals");

    // DES-0013 telemetry-ck-8 counts by stage, definition and code. A planning profile names no definition.
    public static void RecordPlanRefusal(string reason) =>
        AdmissionRefusals.Add(1,
            new KeyValuePair<string, object?>("stage", "plan"),
            new KeyValuePair<string, object?>("reason", reason));

    public static void RecordAuthorRefusal(string definitionId, string reason) =>
        AdmissionRefusals.Add(1,
            new KeyValuePair<string, object?>("stage", "author"),
            new KeyValuePair<string, object?>("definition", definitionId),
            new KeyValuePair<string, object?>("reason", reason));

    public static void RecordPlanOutcome(string solverId, double durationMilliseconds, long unassigned, string reasonCode)
    {
        PlanDuration.Record(durationMilliseconds, new KeyValuePair<string, object?>("solver_id", solverId));
        PlanUnassigned.Add(
            unassigned,
            new KeyValuePair<string, object?>("solver_id", solverId),
            new KeyValuePair<string, object?>("reason_code", reasonCode));
    }

    public static void RecordStaleCommitRefusal() =>
        CommitStaleRefusals.Add(1, new KeyValuePair<string, object?>("reason", "changed_facts"));
}
