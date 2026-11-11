using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling;

/// <summary>Completion time, rather than the previous scheduled time, anchors the next occurrence.</summary>
public sealed class CompletionOffsetDueOccurrenceSource : IDueOccurrenceSource
{
    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant)
    {
        var series = request.Series;
        var last = request.Completions.Where(c => c.SeriesId == series.SeriesId && c.CompletedAt <= kernelInstant)
            .OrderByDescending(c => c.CompletedAt).ThenBy(c => c.CompletionId, StringComparer.Ordinal).FirstOrDefault();
        var due = last is null ? series.FirstDue ?? throw new OccurrenceRefusalException("scheduling.occurrence.first_due_required")
            : OccurrenceTime.Add(last.CompletedAt, ((CompletionOffsetBasis)series.Definition.Basis!).Offset, series.Timezone ?? "UTC");
        var key = OccurrenceTime.Key(series.SeriesId, "completion", last?.CompletionId ?? "first");
        return OccurrenceTime.InRange(request, due)
            ? [OccurrenceTime.Create(request, kernelInstant, key, due, series.Definition.Window, series.Definition.Tolerance)] : [];
    }
}
