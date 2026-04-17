namespace AgentHub.Persistence;

public sealed record ConversationMessage(
    long Id,
    Guid ConversationId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
