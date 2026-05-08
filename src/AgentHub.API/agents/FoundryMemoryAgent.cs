using System.ClientModel;
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
/// Holds the Foundry memory agent, memory client, store name, and session store.
/// Registered as a singleton; the route handler injects this directly.
/// Sessions are keyed by conversationId (short-term memory); userId is passed via
/// the x-memory-user-id header so Foundry scopes long-term memory per user.
/// </summary>
public sealed class FoundryMemoryContext
{
    public required AIAgent Agent { get; init; }
    public required AIProjectMemoryStores MemoryClient { get; init; }
    public required string MemoryStoreName { get; init; }
    public required FoundryMemorySessionStore SessionStore { get; init; }
}

public static class FoundryMemoryAgent
{
    public const string DefaultAgentName = "MemoryAgent";

    public static async Task<FoundryMemoryContext> CreateAsync(Settings settings, ILogger logger)
    {
        logger.LogInformation(
            "Initializing Foundry memory agent. Endpoint={Endpoint}, MemoryStore={MemoryStore}",
            settings.AzureAIProjectEndpoint,
            settings.MemoryStoreName);

        var client = new AIProjectClient(settings.AzureAIProjectEndpoint, new DefaultAzureCredential());
        logger.LogDebug("AIProjectClient created with DefaultAzureCredential");

        var memoryClient = client.GetAIProjectMemoryStoresClient();
        logger.LogDebug("Memory stores client obtained");

        var memoryStore = await GetOrCreateMemoryStoreAsync(memoryClient, settings, logger);
        logger.LogInformation("Memory store ready. Name={MemoryStoreName}", memoryStore.Name);

        var agentName = settings.FoundryAgentName is not null
            ? $"{settings.FoundryAgentName}-memory"
            : DefaultAgentName;

        var record = await GetOrCreateAgentAsync(client, agentName, settings, logger);
        logger.LogInformation("Foundry memory agent is ready. AgentName={AgentName}", record.Name);

        return new FoundryMemoryContext
        {
            Agent = client.AsAIAgent(record),
            MemoryClient = memoryClient,
            MemoryStoreName = settings.MemoryStoreName,
            SessionStore = new FoundryMemorySessionStore()
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
}

#pragma warning restore OPENAI001
#pragma warning restore AAIP001
