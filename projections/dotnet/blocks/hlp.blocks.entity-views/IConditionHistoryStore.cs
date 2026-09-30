namespace Harborline.Blocks.EntityViews;

/// <summary>Defines the IConditionHistoryStore contract used by entity views.</summary>
public interface IConditionHistoryStore
{
    /// <summary>Unknown entities have an empty history rather than typed absence.</summary>
    ValueTask<ConditionHistory> GetConditionHistoryAsync(string entityId, string? asOf);
    /// <summary>Unknown entities have an empty submission list.</summary>
    ValueTask<SubmissionList> GetSubmissionsAsync(string entityId);
}
