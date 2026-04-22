using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace AgentHub.API.Agents;

/// <summary>
/// In-memory store for Foundry agent sessions used by the foundryMemoryAgent endpoint.
/// Foundry manages conversation history server-side via its thread model; this store
/// only tracks the AgentSession handle per conversation so requests can resume the
/// correct Foundry thread. It does not interact with the PostgreSQL memory pipeline.
/// Sessions are lost on restart — clients must start a new conversation after a restart.
/// </summary>
public sealed class FoundryMemorySessionStore
{
    private readonly ConcurrentDictionary<Guid, AgentSession> _sessions = new();

    public bool TryGet(Guid conversationId, out AgentSession? session)
        => _sessions.TryGetValue(conversationId, out session);

    public void Set(Guid conversationId, AgentSession session)
        => _sessions[conversationId] = session;
}
