namespace Harborline.UIAdapters.Blazor.Components.AI;

public enum ChatMessageRole { User, Assistant, System }
public enum ChatMessageStatus { Streaming, Complete, Error }

public sealed record ChatParticipant(string Name, string? AvatarUrl = null);
public sealed record ChatSuggestion(string Title, string Prompt);
public sealed record ChatMessage(
    string Id,
    ChatParticipant Author,
    ChatMessageRole Role,
    string Text,
    ChatMessageStatus? Status = null,
    DateTimeOffset? Timestamp = null);
