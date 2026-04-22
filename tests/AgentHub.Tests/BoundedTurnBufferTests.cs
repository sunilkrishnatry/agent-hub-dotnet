using AgentHub.API.Agents;

namespace AgentHub.Tests;

public class BoundedTurnBufferTests
{
    [Fact]
    public void GetTurns_EmptyBuffer_ReturnsEmpty()
    {
        var buffer = new BoundedTurnBuffer(5);

        var turns = buffer.GetTurns();

        Assert.Empty(turns);
    }

    [Fact]
    public void Count_EmptyBuffer_ReturnsZero()
    {
        var buffer = new BoundedTurnBuffer(5);

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Add_SingleTurn_ReturnsThatTurn()
    {
        var buffer = new BoundedTurnBuffer(5);

        buffer.Add("hello", "hi there");

        var turns = buffer.GetTurns();
        Assert.Single(turns);
        Assert.Equal("hello", turns[0].UserMessage);
        Assert.Equal("hi there", turns[0].AssistantResponse);
    }

    [Fact]
    public void Add_WithinCapacity_ReturnsAllInOrder()
    {
        var buffer = new BoundedTurnBuffer(5);

        buffer.Add("msg1", "resp1");
        buffer.Add("msg2", "resp2");
        buffer.Add("msg3", "resp3");

        var turns = buffer.GetTurns();
        Assert.Equal(3, turns.Count);
        Assert.Equal("msg1", turns[0].UserMessage);
        Assert.Equal("msg2", turns[1].UserMessage);
        Assert.Equal("msg3", turns[2].UserMessage);
    }

    [Fact]
    public void Add_ExactlyAtCapacity_ReturnsAllInOrder()
    {
        var buffer = new BoundedTurnBuffer(3);

        buffer.Add("msg1", "resp1");
        buffer.Add("msg2", "resp2");
        buffer.Add("msg3", "resp3");

        var turns = buffer.GetTurns();
        Assert.Equal(3, turns.Count);
        Assert.Equal("msg1", turns[0].UserMessage);
        Assert.Equal("msg3", turns[2].UserMessage);
    }

    [Fact]
    public void Add_BeyondCapacity_DropsOldest()
    {
        var buffer = new BoundedTurnBuffer(3);

        buffer.Add("msg1", "resp1");
        buffer.Add("msg2", "resp2");
        buffer.Add("msg3", "resp3");
        buffer.Add("msg4", "resp4"); // should drop msg1

        var turns = buffer.GetTurns();
        Assert.Equal(3, turns.Count);
        Assert.Equal("msg2", turns[0].UserMessage);
        Assert.Equal("msg3", turns[1].UserMessage);
        Assert.Equal("msg4", turns[2].UserMessage);
    }

    [Fact]
    public void Add_WellBeyondCapacity_KeepsOnlyMostRecent()
    {
        var buffer = new BoundedTurnBuffer(2);

        for (var i = 1; i <= 10; i++)
        {
            buffer.Add($"msg{i}", $"resp{i}");
        }

        var turns = buffer.GetTurns();
        Assert.Equal(2, turns.Count);
        Assert.Equal("msg9", turns[0].UserMessage);
        Assert.Equal("msg10", turns[1].UserMessage);
    }

    [Fact]
    public void Count_ReflectsActualCount()
    {
        var buffer = new BoundedTurnBuffer(3);

        buffer.Add("msg1", "resp1");
        Assert.Equal(1, buffer.Count);

        buffer.Add("msg2", "resp2");
        Assert.Equal(2, buffer.Count);

        buffer.Add("msg3", "resp3");
        Assert.Equal(3, buffer.Count);

        buffer.Add("msg4", "resp4"); // wraps, count stays at capacity
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public async Task ConcurrentAdds_DoNotCorruptBuffer()
    {
        var buffer = new BoundedTurnBuffer(100);
        var tasks = new Task[50];

        for (var i = 0; i < 50; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() => buffer.Add($"msg{index}", $"resp{index}"));
        }

        await Task.WhenAll(tasks);

        var turns = buffer.GetTurns();
        Assert.Equal(50, turns.Count);
        // All turns should be valid (non-null)
        Assert.All(turns, t =>
        {
            Assert.NotNull(t.UserMessage);
            Assert.NotNull(t.AssistantResponse);
        });
    }

    [Fact]
    public void GetTurns_ReturnsSnapshotNotLiveReference()
    {
        var buffer = new BoundedTurnBuffer(5);
        buffer.Add("msg1", "resp1");

        var snapshot = buffer.GetTurns();
        buffer.Add("msg2", "resp2");

        Assert.Single(snapshot); // snapshot should not change
    }
}
