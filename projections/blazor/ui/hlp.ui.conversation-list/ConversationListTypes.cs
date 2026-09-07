namespace Harborline.UIAdapters.Blazor.Components.AI;
public sealed record ConversationSummary(string Id,string Title,string Timestamp,string? Preview=null);
public sealed record ConversationListLabels(string? Heading=null,string? NewConversation=null,string? Empty=null,string? Rename=null,string? Delete=null,string? ConfirmDelete=null,string? Cancel=null);
