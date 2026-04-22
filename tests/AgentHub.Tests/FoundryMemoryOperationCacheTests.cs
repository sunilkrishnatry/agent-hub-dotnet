using AgentHub.API.Agents;

namespace AgentHub.Tests;

public class FoundryMemoryOperationCacheTests
{
    private readonly FoundryMemoryOperationCache _cache = new();

    [Fact]
    public void GetPreviousSearchId_UnknownScope_ReturnsNull()
    {
        Assert.Null(_cache.GetPreviousSearchId("unknown"));
    }

    [Fact]
    public void GetPreviousUpdateId_UnknownScope_ReturnsNull()
    {
        Assert.Null(_cache.GetPreviousUpdateId("unknown"));
    }

    [Fact]
    public void RememberSearchId_ThenGet_ReturnsStoredId()
    {
        _cache.RememberSearchId("scope1", "search-123");

        Assert.Equal("search-123", _cache.GetPreviousSearchId("scope1"));
    }

    [Fact]
    public void RememberUpdateId_ThenGet_ReturnsStoredId()
    {
        _cache.RememberUpdateId("scope1", "update-456");

        Assert.Equal("update-456", _cache.GetPreviousUpdateId("scope1"));
    }

    [Fact]
    public void RememberSearchId_OverwritesPreviousValue()
    {
        _cache.RememberSearchId("scope1", "search-1");
        _cache.RememberSearchId("scope1", "search-2");

        Assert.Equal("search-2", _cache.GetPreviousSearchId("scope1"));
    }

    [Fact]
    public void RememberUpdateId_OverwritesPreviousValue()
    {
        _cache.RememberUpdateId("scope1", "update-1");
        _cache.RememberUpdateId("scope1", "update-2");

        Assert.Equal("update-2", _cache.GetPreviousUpdateId("scope1"));
    }

    [Fact]
    public void RememberSearchId_WithNull_DoesNotStore()
    {
        _cache.RememberSearchId("scope1", null);

        Assert.Null(_cache.GetPreviousSearchId("scope1"));
    }

    [Fact]
    public void RememberSearchId_WithEmptyString_DoesNotStore()
    {
        _cache.RememberSearchId("scope1", "");

        Assert.Null(_cache.GetPreviousSearchId("scope1"));
    }

    [Fact]
    public void RememberSearchId_WithWhitespace_DoesNotStore()
    {
        _cache.RememberSearchId("scope1", "   ");

        Assert.Null(_cache.GetPreviousSearchId("scope1"));
    }

    [Fact]
    public void RememberUpdateId_WithNull_DoesNotStore()
    {
        _cache.RememberUpdateId("scope1", null);

        Assert.Null(_cache.GetPreviousUpdateId("scope1"));
    }

    [Fact]
    public void RememberUpdateId_WithEmptyString_DoesNotStore()
    {
        _cache.RememberUpdateId("scope1", "");

        Assert.Null(_cache.GetPreviousUpdateId("scope1"));
    }

    [Fact]
    public void DifferentScopes_AreIndependent()
    {
        _cache.RememberSearchId("scope1", "search-A");
        _cache.RememberSearchId("scope2", "search-B");
        _cache.RememberUpdateId("scope1", "update-A");
        _cache.RememberUpdateId("scope2", "update-B");

        Assert.Equal("search-A", _cache.GetPreviousSearchId("scope1"));
        Assert.Equal("search-B", _cache.GetPreviousSearchId("scope2"));
        Assert.Equal("update-A", _cache.GetPreviousUpdateId("scope1"));
        Assert.Equal("update-B", _cache.GetPreviousUpdateId("scope2"));
    }

    [Fact]
    public void SearchAndUpdateIds_AreIndependent()
    {
        _cache.RememberSearchId("scope1", "search-123");
        _cache.RememberUpdateId("scope1", "update-456");

        Assert.Equal("search-123", _cache.GetPreviousSearchId("scope1"));
        Assert.Equal("update-456", _cache.GetPreviousUpdateId("scope1"));
    }

    [Fact]
    public void RememberSearchId_WithNull_DoesNotEraseExisting()
    {
        _cache.RememberSearchId("scope1", "search-123");
        _cache.RememberSearchId("scope1", null);

        Assert.Equal("search-123", _cache.GetPreviousSearchId("scope1"));
    }

    [Fact]
    public void RememberUpdateId_WithNull_DoesNotEraseExisting()
    {
        _cache.RememberUpdateId("scope1", "update-456");
        _cache.RememberUpdateId("scope1", null);

        Assert.Equal("update-456", _cache.GetPreviousUpdateId("scope1"));
    }
}
