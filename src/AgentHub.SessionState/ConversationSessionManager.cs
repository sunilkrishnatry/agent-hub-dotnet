using System.Collections.Concurrent;
using AgentHub.Persistence;
using Microsoft.Extensions.Logging;

namespace AgentHub.SessionState;

public sealed class ConversationSessionManager : IConversationSessionManager
{
    private readonly IConversationHistoryRepository _historyRepository;
    private readonly ILogger<ConversationSessionManager> _logger;
    private readonly ConcurrentDictionary<Guid, object> _sessions = new();

    public ConversationSessionManager(
        IConversationHistoryRepository historyRepository,
        ILogger<ConversationSessionManager> logger)
    {
        _historyRepository = historyRepository;
        _logger = logger;
    }

    public async Task<ConversationSessionContext> GetOrCreateSessionAsync(
        Guid? conversationId,
        Func<CancellationToken, Task<object>> createSession,
        CancellationToken cancellationToken = default)
    {
        var resolvedConversationId = conversationId ?? Guid.NewGuid();

        if (_sessions.TryGetValue(resolvedConversationId, out var existingSession))
        {
            _logger.LogDebug(
                "Reusing in-memory session. ConversationId={ConversationId}",
                resolvedConversationId);

            return new ConversationSessionContext(
                resolvedConversationId,
                existingSession,
                Array.Empty<ConversationMessage>(),
                RequiresHistoryReplay: false);
        }

        IReadOnlyList<ConversationMessage> history = Array.Empty<ConversationMessage>();
        var requiresHistoryReplay = false;

        if (conversationId.HasValue)
        {
            history = await _historyRepository.GetMessagesAsync(resolvedConversationId, cancellationToken);
            requiresHistoryReplay = history.Count > 0;

            _logger.LogInformation(
                "Created new in-memory session for conversation. ConversationId={ConversationId}, HistoryCount={HistoryCount}, RequiresHistoryReplay={RequiresHistoryReplay}",
                resolvedConversationId,
                history.Count,
                requiresHistoryReplay);
        }
        else
        {
            _logger.LogInformation(
                "Created new conversation and in-memory session. ConversationId={ConversationId}",
                resolvedConversationId);
        }

        var createdSession = await createSession(cancellationToken);
        var session = _sessions.GetOrAdd(resolvedConversationId, createdSession);

        return new ConversationSessionContext(
            resolvedConversationId,
            session,
            history,
            requiresHistoryReplay);
    }

    public async Task AppendTurnAsync(
        Guid conversationId,
        string userMessage,
        string assistantMessage,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _historyRepository.AppendMessageAsync(conversationId, "user", userMessage, now, cancellationToken);
        await _historyRepository.AppendMessageAsync(conversationId, "assistant", assistantMessage, now, cancellationToken);
    }

    public Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return _historyRepository.GetMessagesAsync(conversationId, cancellationToken);
    }
}
