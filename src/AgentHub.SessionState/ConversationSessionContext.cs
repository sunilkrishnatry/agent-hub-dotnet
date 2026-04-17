namespace AgentHub.SessionState;

using AgentHub.Persistence;

public sealed record ConversationSessionContext(
	Guid ConversationId,
	object Session,
	IReadOnlyList<ConversationMessage> History,
	bool RequiresHistoryReplay);
