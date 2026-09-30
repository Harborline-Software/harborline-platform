namespace Harborline.UIAdapters.Blazor.Components.AI;
/// <summary>A conversation in the list: id, title, timestamp and an optional preview line.</summary>
public sealed record ConversationSummary(string Id,string Title,string Timestamp,string? Preview=null);
/// <summary>Optional text overrides for the conversation list: heading, new, empty, rename, delete, confirm and cancel.</summary>
public sealed record ConversationListLabels(string? Heading=null,string? NewConversation=null,string? Empty=null,string? Rename=null,string? Delete=null,string? ConfirmDelete=null,string? Cancel=null);
