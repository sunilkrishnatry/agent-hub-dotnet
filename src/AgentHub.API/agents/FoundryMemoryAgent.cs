using System.ClientModel;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Projects.Memory;
using Azure.Identity;
using Microsoft.Agents.AI;

namespace AgentHub.API.Agents;

#pragma warning disable AAIP001
#pragma warning disable OPENAI001

/// <summary>
/// Thread-safe in-memory cache for Foundry memory agent sessions keyed by userId.
/// Sessions are reused across requests for the same userId, enabling conversation continuity.
/// Note: Sessions are lost on app restart; long-term user context is persisted in Foundry's memory store.
/// </summary>
public sealed class FoundryMemorySessionCache
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessionCache = new();
    private readonly ConcurrentDictionary<string, BoundedTurnBuffer> _turnCache = new();
    private readonly ILogger _logger;
    private const int MaxTurnsPerUser = 10;

    public FoundryMemorySessionCache(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets an existing session for userId or creates and caches a new one.
    /// Returns (session, isNew) so the caller knows whether a Foundry memory search is needed.
    /// </summary>
    public async Task<(AgentSession Session, bool IsNew)> GetOrCreateSessionAsync(string userId, Func<Task<AgentSession>> sessionFactory)
    {
        if (_sessionCache.TryGetValue(userId, out var cachedSession))
        {
            _logger.LogDebug("Reusing cached session for userId={UserId}", userId);
            return (cachedSession, false);
        }

        _logger.LogDebug("Creating new session for userId={UserId} (not in cache)", userId);
        var newSession = await sessionFactory();
        var added = _sessionCache.TryAdd(userId, newSession);

        if (added)
        {
            _logger.LogDebug("Cached new session for userId={UserId}", userId);
        }
        else
        {
            _logger.LogDebug("Race condition detected for userId={UserId}, using cached session from other thread", userId);
            return (_sessionCache[userId], false);
        }

        return (newSession, true);
    }

    /// <summary>
    /// Appends a user/assistant turn to the bounded local cache for the given userId.
    /// </summary>
    public void AppendTurn(string userId, string userMessage, string assistantResponse)
    {
        var buffer = _turnCache.GetOrAdd(userId, _ => new BoundedTurnBuffer(MaxTurnsPerUser));
        buffer.Add(userMessage, assistantResponse);
        _logger.LogDebug("Appended turn to local cache for userId={UserId}. TurnCount={TurnCount}", userId, buffer.Count);
    }

    /// <summary>
    /// Returns the cached turns for the given userId (empty if no cache entry exists).
    /// </summary>
    public IReadOnlyList<ConversationTurn> GetTurns(string userId)
    {
        return _turnCache.TryGetValue(userId, out var buffer) ? buffer.GetTurns() : [];
    }

    public int GetActiveCacheSize() => _sessionCache.Count;
}

/// <summary>
/// A single user/assistant exchange.
/// </summary>
public sealed record ConversationTurn(string UserMessage, string AssistantResponse);

/// <summary>
/// Thread-safe bounded ring buffer that keeps the most recent N turns.
/// </summary>
public sealed class BoundedTurnBuffer
{
    private readonly ConversationTurn[] _buffer;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public BoundedTurnBuffer(int capacity)
    {
        _buffer = new ConversationTurn[capacity];
    }

    public int Count { get { lock (_lock) { return _count; } } }

    public void Add(string userMessage, string assistantResponse)
    {
        lock (_lock)
        {
            _buffer[_head] = new ConversationTurn(userMessage, assistantResponse);
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
    }

    public IReadOnlyList<ConversationTurn> GetTurns()
    {
        lock (_lock)
        {
            var result = new ConversationTurn[_count];
            var start = _count < _buffer.Length ? 0 : _head;
            for (var i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % _buffer.Length];
            }
            return result;
        }
    }
}

/// <summary>
/// Holds the Foundry memory agent, memory client, store name, and session cache.
/// Registered as a singleton; the route handler injects this directly.
/// </summary>
public sealed class FoundryMemoryContext
{
    public required AIAgent Agent { get; init; }
    public required AIProjectMemoryStores MemoryClient { get; init; }
    public required string MemoryStoreName { get; init; }
    public required FoundryMemorySessionCache SessionCache { get; init; }
    public required FoundryMemoryOperationCache OperationCache { get; init; }
}

public sealed class FoundryMemoryOperationCache
{
    private readonly ConcurrentDictionary<string, string> _searchIds = new();
    private readonly ConcurrentDictionary<string, string> _updateIds = new();

    public string? GetPreviousSearchId(string scope)
        => _searchIds.TryGetValue(scope, out var searchId) ? searchId : null;

    public string? GetPreviousUpdateId(string scope)
        => _updateIds.TryGetValue(scope, out var updateId) ? updateId : null;

    public void RememberSearchId(string scope, string? searchId)
    {
        if (!string.IsNullOrWhiteSpace(searchId))
        {
            _searchIds[scope] = searchId;
        }
    }

    public void RememberUpdateId(string scope, string? updateId)
    {
        if (!string.IsNullOrWhiteSpace(updateId))
        {
            _updateIds[scope] = updateId;
        }
    }
}

public static class FoundryMemoryAgent
{
    public const string DefaultAgentName = "MemoryAgent";

    public static async Task<FoundryMemoryContext> CreateAsync(Settings settings, ILogger logger)
    {
        logger.LogInformation(
            "Initializing Foundry memory agent. Endpoint={Endpoint}, MemoryStore={MemoryStore}, EmbeddingModel={EmbeddingModel}",
            settings.AzureAIProjectEndpoint,
            settings.MemoryStoreName,
            settings.MemoryEmbeddingModel);
        logger.LogDebug("Foundry memory agent configuration: isolated from PostgreSQL, userId-scoped memory, in-memory session cache");

        var client = new AIProjectClient(settings.AzureAIProjectEndpoint, new DefaultAzureCredential());
        logger.LogDebug("AIProjectClient created with DefaultAzureCredential");

        var memoryClient = client.GetAIProjectMemoryStoresClient();
        logger.LogDebug("Memory stores client obtained");

        var memoryStore = await GetOrCreateMemoryStoreAsync(memoryClient, settings, logger);
        logger.LogInformation("Memory store ready. Name={MemoryStoreName}", memoryStore.Name);
        logger.LogDebug("Memory store type={Type}, persistent in Azure, scoped by userId", memoryStore.GetType().Name);

        var agentName = settings.FoundryAgentName is not null
            ? $"{settings.FoundryAgentName}-memory"
            : DefaultAgentName;

        var record = await GetOrCreateAgentAsync(client, agentName, settings, logger);
        logger.LogInformation("Foundry memory agent is ready. AgentName={AgentName}", record.Name);

        var sessionCache = new FoundryMemorySessionCache(logger);
        var operationCache = new FoundryMemoryOperationCache();
        logger.LogDebug("In-memory session cache initialized (thread-safe, keyed by userId)");

        return new FoundryMemoryContext
        {
            Agent = client.AsAIAgent(record),
            MemoryClient = memoryClient,
            MemoryStoreName = settings.MemoryStoreName,
            SessionCache = sessionCache,
            OperationCache = operationCache
        };
    }

    internal static async Task<MemoryStore> GetOrCreateMemoryStoreAsync(
        AIProjectMemoryStores memoryClient, Settings settings, ILogger logger)
    {
        try
        {
            logger.LogDebug("Attempting to resolve memory store. Name={Name}", settings.MemoryStoreName);
            var store = await memoryClient.GetMemoryStoreAsync(settings.MemoryStoreName);
            logger.LogDebug("Memory store resolved successfully");
            return store;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            logger.LogInformation("Memory store not found, creating. Name={Name}", settings.MemoryStoreName);
            logger.LogDebug("Creating MemoryStoreDefaultDefinition with chatModel={ChatModel}, embeddingModel={EmbeddingModel}",
                settings.AzureAIModelDeploymentName, settings.MemoryEmbeddingModel);

            var definition = new MemoryStoreDefaultDefinition(
                chatModel: settings.AzureAIModelDeploymentName,
                embeddingModel: settings.MemoryEmbeddingModel);
            definition.Options = new MemoryStoreDefaultOptions(
                isUserProfileEnabled: true,
                isChatSummaryEnabled: true);
            logger.LogDebug("Memory store options set: isUserProfileEnabled=true, isChatSummaryEnabled=true");

            var created = await memoryClient.CreateMemoryStoreAsync(
                name: settings.MemoryStoreName,
                definition: definition,
                description: "Memory store for Agent Hub memory agent");
            logger.LogDebug("Memory store created successfully");
            return created;
        }
    }

    internal static async Task<MemoryStoreSearchResponse> SearchMemoriesAsync(
        AIProjectMemoryStores memoryClient,
        string memoryStoreName,
        string scope,
        string items,
        string? previousSearchId,
        CancellationToken cancellationToken)
    {
        var request = new MemorySearchProtocolRequest(
            scope,
            [new InputItemMessage("message", "user", items)],
            previousSearchId,
            new MemorySearchProtocolRequestOptions(5));

        var result = await memoryClient.SearchMemoriesAsync(
            memoryStoreName,
            BinaryContent.Create(BinaryData.FromObjectAsJson(request, JsonSerializerOptions.Default)),
            new System.ClientModel.Primitives.RequestOptions { CancellationToken = cancellationToken });

        return (MemoryStoreSearchResponse)result;
    }

    internal static async Task<MemoryUpdateResult> UpdateMemoriesAsync(
        AIProjectMemoryStores memoryClient,
        string memoryStoreName,
        string scope,
        string userMessage,
        string assistantResponse,
        string? previousUpdateId,
        CancellationToken cancellationToken)
    {
        var request = new MemoryUpdateProtocolRequest(
            scope,
            [
                new InputItemMessage("message", "user", userMessage),
                new InputItemMessage("message", "assistant", assistantResponse)
            ],
            previousUpdateId,
            0);

        var result = await memoryClient.UpdateMemoriesAsync(
            memoryStoreName,
            BinaryContent.Create(BinaryData.FromObjectAsJson(request, JsonSerializerOptions.Default)),
            new System.ClientModel.Primitives.RequestOptions { CancellationToken = cancellationToken });

        return (MemoryUpdateResult)result;
    }

    private static async Task<ProjectsAgentRecord> GetOrCreateAgentAsync(
        AIProjectClient client, string agentName, Settings settings, ILogger logger)
    {
        try
        {
            logger.LogDebug("Attempting to resolve existing Foundry memory agent. AgentName={AgentName}", agentName);
            var agent = await client.AgentAdministrationClient.GetAgentAsync(agentName);
            logger.LogDebug("Foundry memory agent resolved successfully");
            return agent;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            logger.LogInformation(
                "Foundry memory agent not found, creating. AgentName={AgentName}, Model={Model}",
                agentName, settings.AzureAIModelDeploymentName);
            logger.LogDebug("Creating DeclarativeAgentDefinition for memory agent");

            var definition = new DeclarativeAgentDefinition(model: settings.AzureAIModelDeploymentName)
            {
                Instructions = "You are a helpful assistant with persistent memory. You remember context from previous conversations."
            };

            var options = new ProjectsAgentVersionCreationOptions(definition);
            logger.LogDebug("Calling CreateAgentVersionAsync for memory agent creation");
            await client.AgentAdministrationClient.CreateAgentVersionAsync(agentName, options);
            logger.LogDebug("Agent version created, retrieving agent record");

            logger.LogInformation("Foundry memory agent created. AgentName={AgentName}", agentName);
            return await client.AgentAdministrationClient.GetAgentAsync(agentName);
        }
    }

    private sealed record InputItemMessage(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record MemorySearchProtocolRequest(
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("items")] InputItemMessage[] Items,
        [property: JsonPropertyName("previous_search_id")] string? PreviousSearchId,
        [property: JsonPropertyName("options")] MemorySearchProtocolRequestOptions Options);

    private sealed record MemorySearchProtocolRequestOptions(
        [property: JsonPropertyName("max_memories")] int MaxMemories);

    private sealed record MemoryUpdateProtocolRequest(
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("items")] InputItemMessage[] Items,
        [property: JsonPropertyName("previous_update_id")] string? PreviousUpdateId,
        [property: JsonPropertyName("update_delay")] int UpdateDelay);
}

#pragma warning restore OPENAI001
#pragma warning restore AAIP001
