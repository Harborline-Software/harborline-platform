namespace Harborline.UIAdapters.Blazor.Components.Feedback;

/// <summary>The tone of an empty state: informational, positive, or actionable.</summary>
public enum EmptyStateVariant
{
    /// <summary>Explains that there is nothing to show, without prompting action.</summary>
    Informational,
    /// <summary>Presents the empty result as a good outcome, such as nothing left to do.</summary>
    Positive,
    /// <summary>Presents the empty state with a call to action to fill it.</summary>
    Actionable
}
