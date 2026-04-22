using AgentHub.Persistence;

namespace AgentHub.Tests;

public class ConversationMessageTests
{
    [Fact]
    public void Record_StoresAllProperties()
    {
        var id = 42L;
        var conversationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var message = new ConversationMessage(id, conversationId, "user", "hello", createdAt);

        Assert.Equal(id, message.Id);
        Assert.Equal(conversationId, message.ConversationId);
        Assert.Equal("user", message.Role);
        Assert.Equal("hello", message.Content);
        Assert.Equal(createdAt, message.CreatedAt);
    }

    [Fact]
    public void Record_EqualityByValue()
    {
        var conversationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var msg1 = new ConversationMessage(1, conversationId, "user", "hello", createdAt);
        var msg2 = new ConversationMessage(1, conversationId, "user", "hello", createdAt);

        Assert.Equal(msg1, msg2);
    }

    [Fact]
    public void Record_InequalityWhenDifferent()
    {
        var conversationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var msg1 = new ConversationMessage(1, conversationId, "user", "hello", createdAt);
        var msg2 = new ConversationMessage(2, conversationId, "user", "hello", createdAt);

        Assert.NotEqual(msg1, msg2);
    }
}

public class ConversationSessionContextTests
{
    [Fact]
    public void Record_StoresAllProperties()
    {
        var conversationId = Guid.NewGuid();
        var session = new object();
        var history = new List<ConversationMessage>();

        var context = new AgentHub.SessionState.ConversationSessionContext(
            conversationId, session, history, RequiresHistoryReplay: true);

        Assert.Equal(conversationId, context.ConversationId);
        Assert.Same(session, context.Session);
        Assert.Same(history, context.History);
        Assert.True(context.RequiresHistoryReplay);
    }

    [Fact]
    public void Record_RequiresHistoryReplayFalse()
    {
        var context = new AgentHub.SessionState.ConversationSessionContext(
            Guid.NewGuid(), new object(), Array.Empty<ConversationMessage>(), RequiresHistoryReplay: false);

        Assert.False(context.RequiresHistoryReplay);
    }
}

public class PostgresConversationOptionsTests
{
    [Fact]
    public void ConnectionString_DefaultsToEmpty()
    {
        var options = new PostgresConversationOptions();

        Assert.Equal(string.Empty, options.ConnectionString);
    }

    [Fact]
    public void ConnectionString_CanBeSet()
    {
        var options = new PostgresConversationOptions
        {
            ConnectionString = "Host=localhost;Database=test"
        };

        Assert.Equal("Host=localhost;Database=test", options.ConnectionString);
    }
}
