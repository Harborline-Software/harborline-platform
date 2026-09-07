using Harborline.Foundation.Scheduling;

namespace Harborline.Blocks.RelativeChains;

/// <summary>Projects a prepared rich expansion result through the unchanged source-agnostic due-occurrence port.</summary>
public sealed class RelativeChainDueOccurrenceSource : IDueOccurrenceSource
{
    private readonly RelativeChainExpansionResult result;

    /// <summary>Creates an adapter over a prepared, consistent rich result.</summary>
    /// <param name="result">The expansion result; failures remain visible and make projection fail closed.</param>
    public RelativeChainDueOccurrenceSource(RelativeChainExpansionResult result) => this.result = result ?? throw new ArgumentNullException(nameof(result));

    /// <inheritdoc />
    public IReadOnlyList<DueQueueItem> DeriveDue(IEnumerable<DueQueueCompletion> completionHistory, DateOnly windowStart, DateOnly windowEnd, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(completionHistory);
        if (windowEnd < windowStart) throw new ArgumentException("The due window is inverted.", nameof(windowEnd));
        if (result.Failures.Count != 0) throw new InvalidOperationException($"Relative-chain generation failed: {string.Join(',', result.Failures.Select(failure => failure.Code))}");
        var completions = completionHistory.ToHashSet();
        return result.CurrentOccurrences
            .Where(occurrence => occurrence.DueDate >= windowStart && occurrence.DueDate <= windowEnd)
            .Where(occurrence => !completions.Contains(new DueQueueCompletion(occurrence.SubjectRef, occurrence.OccurrenceId.ToString(), occurrence.DueDate)))
            .Select(occurrence => new DueQueueItem(occurrence.SubjectRef, occurrence.OccurrenceId.ToString(), occurrence.DueDate, Math.Max(0, asOf.DayNumber - occurrence.DueDate.DayNumber)))
            .OrderByDescending(item => item.OverdueDays).ThenBy(item => item.DueDate).ThenBy(item => item.SubjectRef, StringComparer.Ordinal).ThenBy(item => item.ScheduleRef, StringComparer.Ordinal).ToArray();
    }
}
