using AgentHub.API.Agents;
using Azure.AI.Projects;
using Azure.AI.Projects.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.ClientModel.Primitives;

#pragma warning disable AAIP001
#pragma warning disable OPENAI001

namespace AgentHub.Tests;

public class MemoryAuditServiceTests
{
    // ----- helpers -----

    private static MemorySearchItem MakeSearchItem(string content, string scope = "user1")
    {
        var json = $@"{{""memory_item"":{{""id"":""id-1"",""updated_at"":1704067200,""scope"":""{scope}"",""content"":""{content}"",""kind"":""user_profile""}}}}";
        return ModelReaderWriter.Read<MemorySearchItem>(BinaryData.FromString(json))!;
    }

    // ----- InspectAsync -----

    [Fact]
    public async Task InspectAsync_ReturnsMemoriesFromResponse()
    {
        var items = new[] { MakeSearchItem("User likes hiking"), MakeSearchItem("User is in Seattle") };
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", items, null)),
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            logger: NullLogger.Instance);

        var result = await auditService.InspectAsync("user1", null, default);

        Assert.Equal("user1", result.UserId);
        Assert.Equal(2, result.Memories.Length);
        Assert.Contains("User likes hiking", result.Memories);
        Assert.Contains("User is in Seattle", result.Memories);
    }

    [Fact]
    public async Task InspectAsync_EmptyTopic_UsesDefaultQuery()
    {
        string? capturedQuery = null;
        var auditService = new MemoryAuditService(
            searchMemories: (_, query, _) =>
            {
                capturedQuery = query;
                return Task.FromResult(AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null));
            },
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            logger: NullLogger.Instance);

        await auditService.InspectAsync("user1", topic: null, default);

        Assert.Equal("user context preferences history", capturedQuery);
    }

    [Fact]
    public async Task InspectAsync_WithTopic_UsesTopicAsQuery()
    {
        string? capturedQuery = null;
        var auditService = new MemoryAuditService(
            searchMemories: (_, query, _) =>
            {
                capturedQuery = query;
                return Task.FromResult(AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null));
            },
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            logger: NullLogger.Instance);

        await auditService.InspectAsync("user1", topic: "project preferences", default);

        Assert.Equal("project preferences", capturedQuery);
    }

    [Fact]
    public async Task InspectAsync_FiltersNullAndWhitespaceContent()
    {
        var items = new[]
        {
            MakeSearchItem("Valid memory"),
            MakeSearchItem("   "),
        };
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", items, null)),
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            logger: NullLogger.Instance);

        var result = await auditService.InspectAsync("user1", null, default);

        Assert.Single(result.Memories);
        Assert.Equal("Valid memory", result.Memories[0]);
    }

    [Fact]
    public async Task InspectAsync_NoMemories_ReturnsEmptyArray()
    {
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            logger: NullLogger.Instance);

        var result = await auditService.InspectAsync("user1", null, default);

        Assert.Empty(result.Memories);
    }

    [Fact]
    public async Task InspectAsync_PassesCorrectScopeToSearch()
    {
        string? capturedScope = null;
        var auditService = new MemoryAuditService(
            searchMemories: (scope, _, _) =>
            {
                capturedScope = scope;
                return Task.FromResult(AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null));
            },
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("alice", "store", true)),
            logger: NullLogger.Instance);

        await auditService.InspectAsync("alice", null, default);

        Assert.Equal("alice", capturedScope);
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_CallsFoundryWithCorrectScope()
    {
        string? capturedScope = null;
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) =>
            {
                capturedScope = scope;
                return Task.FromResult(
                    AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", true));
            },
            logger: NullLogger.Instance);

        await auditService.DeleteAsync("bob", default);

        Assert.Equal("bob", capturedScope);
    }

    [Fact]
    public async Task DeleteAsync_WhenFoundryConfirmsDelete_FoundryScopeDeletedIsTrue()
    {
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", isDeleted: true)),
            logger: NullLogger.Instance);

        var result = await auditService.DeleteAsync("user1", default);

        Assert.True(result.FoundryScopeDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenFoundryReturnsNotDeleted_FoundryScopeDeletedIsFalse()
    {
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", isDeleted: false)),
            logger: NullLogger.Instance);

        var result = await auditService.DeleteAsync("user1", default);

        Assert.False(result.FoundryScopeDeleted);
    }
}
