using Azure.AI.Projects.Memory;

namespace AgentHub.API.Agents;

/// <summary>
/// Encapsulates memory audit operations: inspect what is stored for a user, and delete their memory footprint.
/// Separation of concern: this class owns the memory audit lifecycle; FoundryMemoryAgent owns the
/// conversational memory read/write lifecycle.
/// </summary>
public sealed class MemoryAuditService
{
    private readonly Func<string, string, CancellationToken, Task<MemoryStoreSearchResponse>> _searchMemories;
    private readonly Func<string, CancellationToken, Task<MemoryStoreDeleteScopeResponse>> _deleteScope;
    private readonly ILogger _logger;

    public MemoryAuditService(FoundryMemoryContext context, ILogger logger)
        : this(
            (scope, query, ct) => FoundryMemoryAgent.SearchMemoriesAsync(
                context.MemoryClient, context.MemoryStoreName, scope, query, null, ct),
            async (scope, ct) => (await context.MemoryClient.DeleteScopeAsync(context.MemoryStoreName, scope, ct)).Value,
            logger)
    {
    }

    internal MemoryAuditService(
        Func<string, string, CancellationToken, Task<MemoryStoreSearchResponse>> searchMemories,
        Func<string, CancellationToken, Task<MemoryStoreDeleteScopeResponse>> deleteScope,
        ILogger logger)
    {
        _searchMemories = searchMemories;
        _deleteScope = deleteScope;
        _logger = logger;
    }

    /// <summary>
    /// Returns all memories stored for the given userId in the Foundry memory store.
    /// Uses <paramref name="topic"/> to narrow the search; defaults to a broad query if omitted.
    /// </summary>
    public async Task<MemoryInspectResult> InspectAsync(
        string userId,
        string? topic,
        CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(topic)
            ? "user context preferences history"
            : topic;

        _logger.LogInformation(
            "Inspecting Foundry memory for userId={UserId}, Query={Query}",
            userId, query);

        var response = await _searchMemories(userId, query, cancellationToken);

        var memories = (response.Memories ?? [])
            .Select(m => m.MemoryItem?.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Cast<string>()
            .ToArray();

        _logger.LogInformation(
            "Memory inspect completed. UserId={UserId}, MemoryCount={Count}",
            userId, memories.Length);

        return new MemoryInspectResult(userId, memories);
    }

    /// <summary>
    /// Deletes the memory footprint for the given userId:
    /// calls the Foundry SDK's DeleteScope API to remove persisted memories, then clears in-process caches.
    /// </summary>
    public async Task<MemoryDeleteResult> DeleteAsync(string userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting memory footprint for userId={UserId}", userId);

        var deleteResponse = await _deleteScope(userId, cancellationToken);
        var foundryDeleted = deleteResponse.IsDeleted;

        _logger.LogInformation(
            "Memory delete completed. UserId={UserId}, FoundryScopeDeleted={Foundry}",
            userId, foundryDeleted);

        return new MemoryDeleteResult(userId, foundryDeleted);
    }
}

public record MemoryInspectResult(string UserId, string[] Memories);

public record MemoryDeleteResult(string UserId, bool FoundryScopeDeleted);
