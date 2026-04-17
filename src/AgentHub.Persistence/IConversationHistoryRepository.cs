namespace AgentHub.Persistence;

public interface IConversationHistoryRepository
{
    Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task AppendMessageAsync(
        Guid conversationId,
        string role,
        string content,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);
}
