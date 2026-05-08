using AgentHub.API.Agents;
using Microsoft.Agents.AI;

namespace AgentHub.Tests;

/// <summary>
/// Tests that verify FoundryMemorySessionStore correctly isolates sessions by conversationId.
/// Each conversation must have its own session so Foundry can maintain independent short-term memory.
/// </summary>
public class FoundryMemorySessionStoreTests
{
    [Fact]
    public void TryGet_UnknownConversationId_ReturnsFalse()
    {
        var store = new FoundryMemorySessionStore();

        var found = store.TryGet(Guid.NewGuid(), out var session);

        Assert.False(found);
        Assert.Null(session);
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsTrueAndCorrectSession()
    {
        var store = new FoundryMemorySessionStore();
        var conversationId = Guid.NewGuid();
        var agentSession = (AgentSession)null!;

        store.Set(conversationId, agentSession);
        var found = store.TryGet(conversationId, out var retrieved);

        Assert.True(found);
        Assert.Equal(agentSession, retrieved);
    }

    [Fact]
    public void TwoConversations_HaveIsolatedSessions()
    {
        var store = new FoundryMemorySessionStore();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var session1 = (AgentSession)null!;
        var session2 = (AgentSession)null!;

        store.Set(id1, session1);
        store.Set(id2, session2);

        Assert.True(store.TryGet(id1, out var retrieved1));
        Assert.True(store.TryGet(id2, out var retrieved2));
        Assert.Equal(session1, retrieved1);
        Assert.Equal(session2, retrieved2);
    }

    [Fact]
    public void Set_OverwritesExistingSession()
    {
        var store = new FoundryMemorySessionStore();
        var conversationId = Guid.NewGuid();
        var original = (AgentSession)null!;
        var replacement = (AgentSession)null!;

        store.Set(conversationId, original);
        store.Set(conversationId, replacement);

        store.TryGet(conversationId, out var retrieved);
        Assert.Equal(replacement, retrieved);
    }
}
