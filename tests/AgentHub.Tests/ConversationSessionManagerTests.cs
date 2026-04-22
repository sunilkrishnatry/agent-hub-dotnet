using AgentHub.Persistence;
using AgentHub.SessionState;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AgentHub.Tests;

public class ConversationSessionManagerTests
{
    private readonly IConversationHistoryRepository _repository = Substitute.For<IConversationHistoryRepository>();
    private readonly ConversationSessionManager _manager;

    public ConversationSessionManagerTests()
    {
        _manager = new ConversationSessionManager(
            _repository,
            NullLogger<ConversationSessionManager>.Instance);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_NullConversationId_GeneratesNewId()
    {
        var session = new object();

        var result = await _manager.GetOrCreateSessionAsync(
            null,
            _ => Task.FromResult(session));

        Assert.NotEqual(Guid.Empty, result.ConversationId);
        Assert.Same(session, result.Session);
        Assert.False(result.RequiresHistoryReplay);
        Assert.Empty(result.History);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_NewConversationIdWithNoHistory_NoReplay()
    {
        var conversationId = Guid.NewGuid();
        _repository.GetMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConversationMessage>());

        var result = await _manager.GetOrCreateSessionAsync(
            conversationId,
            _ => Task.FromResult(new object()));

        Assert.Equal(conversationId, result.ConversationId);
        Assert.False(result.RequiresHistoryReplay);
        Assert.Empty(result.History);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_ExistingConversationIdWithHistory_RequiresReplay()
    {
        var conversationId = Guid.NewGuid();
        var history = new List<ConversationMessage>
        {
            new(1, conversationId, "user", "hello", DateTimeOffset.UtcNow),
            new(2, conversationId, "assistant", "hi there", DateTimeOffset.UtcNow),
        };
        _repository.GetMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(history);

        var result = await _manager.GetOrCreateSessionAsync(
            conversationId,
            _ => Task.FromResult(new object()));

        Assert.Equal(conversationId, result.ConversationId);
        Assert.True(result.RequiresHistoryReplay);
        Assert.Equal(2, result.History.Count);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_SecondCallReusesSession()
    {
        var conversationId = Guid.NewGuid();
        var session = new object();
        _repository.GetMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConversationMessage>());

        await _manager.GetOrCreateSessionAsync(conversationId, _ => Task.FromResult(session));
        var result = await _manager.GetOrCreateSessionAsync(conversationId, _ => Task.FromResult(new object()));

        Assert.Same(session, result.Session);
        Assert.False(result.RequiresHistoryReplay);
    }

    [Fact]
    public async Task AppendTurnAsync_PersistsBothMessages()
    {
        var conversationId = Guid.NewGuid();

        await _manager.AppendTurnAsync(conversationId, "user message", "assistant message");

        await _repository.Received(1).AppendMessageAsync(
            conversationId, "user", "user message", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).AppendMessageAsync(
            conversationId, "assistant", "assistant message", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHistoryAsync_DelegatesToRepository()
    {
        var conversationId = Guid.NewGuid();
        var expected = new List<ConversationMessage>
        {
            new(1, conversationId, "user", "hello", DateTimeOffset.UtcNow)
        };
        _repository.GetMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _manager.GetHistoryAsync(conversationId);

        Assert.Same(expected, result);
    }
}
