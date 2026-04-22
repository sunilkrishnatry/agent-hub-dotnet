using AgentHub.API.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentHub.Tests;

public class FoundryMemorySessionCacheTests
{
    private readonly FoundryMemorySessionCache _cache = new(NullLoggerFactory.Instance.CreateLogger("test"));

    [Fact]
    public async Task GetOrCreateSessionAsync_NewUser_ReturnsIsNewTrue()
    {
        var (_, isNew) = await _cache.GetOrCreateSessionAsync(
            "user1",
            () => Task.FromResult<AgentSession>(null!));

        Assert.True(isNew);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_ReturningUser_ReturnsIsNewFalse()
    {
        await _cache.GetOrCreateSessionAsync("user1", () => Task.FromResult<AgentSession>(null!));

        var (_, isNew) = await _cache.GetOrCreateSessionAsync(
            "user1",
            () => Task.FromResult<AgentSession>(null!));

        Assert.False(isNew);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_DifferentUsers_CreatesSeparateSessions()
    {
        await _cache.GetOrCreateSessionAsync("user1", () => Task.FromResult<AgentSession>(null!));
        await _cache.GetOrCreateSessionAsync("user2", () => Task.FromResult<AgentSession>(null!));

        Assert.Equal(2, _cache.GetActiveCacheSize());
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_FactoryCalledOnlyOnceForSameUser()
    {
        var callCount = 0;

        await _cache.GetOrCreateSessionAsync("user1", () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<AgentSession>(null!);
        });

        await _cache.GetOrCreateSessionAsync("user1", () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<AgentSession>(null!);
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void AppendTurn_And_GetTurns_WorkCorrectly()
    {
        _cache.AppendTurn("user1", "hello", "hi");
        _cache.AppendTurn("user1", "how are you", "good");

        var turns = _cache.GetTurns("user1");

        Assert.Equal(2, turns.Count);
        Assert.Equal("hello", turns[0].UserMessage);
        Assert.Equal("how are you", turns[1].UserMessage);
    }

    [Fact]
    public void GetTurns_UnknownUser_ReturnsEmpty()
    {
        var turns = _cache.GetTurns("nonexistent");

        Assert.Empty(turns);
    }

    [Fact]
    public void AppendTurn_DifferentUsers_IndependentBuffers()
    {
        _cache.AppendTurn("user1", "msg1", "resp1");
        _cache.AppendTurn("user2", "msg2", "resp2");

        var turns1 = _cache.GetTurns("user1");
        var turns2 = _cache.GetTurns("user2");

        Assert.Single(turns1);
        Assert.Single(turns2);
        Assert.Equal("msg1", turns1[0].UserMessage);
        Assert.Equal("msg2", turns2[0].UserMessage);
    }

    [Fact]
    public async Task GetActiveCacheSize_ReflectsSessionCount()
    {
        Assert.Equal(0, _cache.GetActiveCacheSize());

        await _cache.GetOrCreateSessionAsync("user1", () => Task.FromResult<AgentSession>(null!));
        Assert.Equal(1, _cache.GetActiveCacheSize());

        await _cache.GetOrCreateSessionAsync("user2", () => Task.FromResult<AgentSession>(null!));
        Assert.Equal(2, _cache.GetActiveCacheSize());
    }
}
