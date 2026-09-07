namespace Harborline.Blocks.EntityViews;

public interface IConditionHistoryStore
{
    /// <summary>Unknown entities have an empty history rather than typed absence.</summary>
    ValueTask<ConditionHistory> GetConditionHistoryAsync(string entityId, string? asOf);
    /// <summary>Unknown entities have an empty submission list.</summary>
    ValueTask<SubmissionList> GetSubmissionsAsync(string entityId);
}
