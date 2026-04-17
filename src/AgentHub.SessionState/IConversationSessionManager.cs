using AgentHub.Persistence;

namespace AgentHub.SessionState;

public interface IConversationSessionManager
{
    Task<ConversationSessionContext> GetOrCreateSessionAsync(
        Guid? conversationId,
        Func<CancellationToken, Task<object>> createSession,
        CancellationToken cancellationToken = default);

    Task AppendTurnAsync(
        Guid conversationId,
        string userMessage,
        string assistantMessage,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}
