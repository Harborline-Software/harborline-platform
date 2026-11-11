using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling;

/// <summary>A latched threshold remains open until its exact occurrence is completed; completion re-arms it.</summary>
public sealed class ThresholdDueOccurrenceSource : IDueOccurrenceSource
{
    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant)
    {
        var series = request.Series;
        var basis = (ThresholdBasis)series.Definition.Basis!;
        var readings = request.Readings.Where(r => r.SeriesId == series.SeriesId && r.Reading == basis.Reading && r.RecordedAt <= kernelInstant)
            .OrderBy(r => r.RecordedAt).ThenBy(r => r.ReadingId, StringComparer.Ordinal).ToArray();
        if (readings.Length == 0) throw new OccurrenceRefusalException("scheduling.occurrence.reading_required");
        var completions = request.Completions.Where(c => c.SeriesId == series.SeriesId && c.CompletedAt <= kernelInstant).ToArray();
        var events = readings.Select(r => (At: r.RecordedAt, Reading: (ScheduleReading?)r, Completion: (DueQueueCompletion?)null))
            .Concat(completions.Select(c => (At: c.CompletedAt, Reading: (ScheduleReading?)null, Completion: (DueQueueCompletion?)c)))
            .OrderBy(e => e.At).ThenBy(e => e.Reading is null ? 1 : 0)
            .ThenBy(e => e.Reading?.ReadingId ?? e.Completion!.CompletionId, StringComparer.Ordinal);
        var result = new List<SchedulingOccurrence>();
        string? open = null;
        string? armedBy = null;
        ScheduleReading? current = null;
        bool Past() => current is not null && (basis.Crossing == ThresholdCrossing.Above ? current.Value > basis.Value : current.Value < basis.Value);
        foreach (var item in events)
        {
            if (item.Reading is { } reading) current = reading;
            else if (item.Completion is { } completion && completion.OccurrenceId == open)
            {
                open = null;
                armedBy = completion.CompletionId;
            }
            if (open is not null || !Past()) continue;
            open = armedBy is null ? OccurrenceTime.Key(series.SeriesId, "threshold", current!.ReadingId, "initial")
                : OccurrenceTime.Key(series.SeriesId, "threshold", current!.ReadingId, "after", armedBy);
            if (OccurrenceTime.InRange(request, item.At))
                result.Add(OccurrenceTime.Create(request, kernelInstant, open, item.At, series.Definition.Window, series.Definition.Tolerance));
        }
        return result;
    }
}
