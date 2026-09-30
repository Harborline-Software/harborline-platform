namespace Harborline.UIAdapters.Blazor.Components.AI;

/// <summary>Who authored a chat message, which decides its alignment and styling.</summary>
public enum ChatMessageRole
{
    /// <summary>Marks the message as written by the person using the chat.</summary>
    User,
    /// <summary>Marks the message as written by the assistant.</summary>
    Assistant,
    /// <summary>Marks the message as a system notice rather than conversation.</summary>
    System
}
/// <summary>The delivery state of a chat message: streaming, complete, or failed.</summary>
public enum ChatMessageStatus
{
    /// <summary>The message is still arriving, so it is not announced to assistive tech yet.</summary>
    Streaming,
    /// <summary>The message has fully arrived.</summary>
    Complete,
    /// <summary>The message failed to arrive and is flagged as an error.</summary>
    Error
}

/// <summary>A chat participant: display name and optional avatar image address.</summary>
public sealed record ChatParticipant(string Name, string? AvatarUrl = null);
/// <summary>A suggested prompt offered in a chat: a title and the prompt text it sends.</summary>
public sealed record ChatSuggestion(string Title, string Prompt);
/// <summary>One chat message: author, role, text, delivery status and optional timestamp.</summary>
public sealed record ChatMessage(
    string Id,
    ChatParticipant Author,
    ChatMessageRole Role,
    string Text,
    ChatMessageStatus? Status = null,
    DateTimeOffset? Timestamp = null);
