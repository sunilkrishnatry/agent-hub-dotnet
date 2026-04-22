using AgentHub.API.Agents;
using AgentHub.Persistence;
using AgentHub.SessionState;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentHub.API.Routes;

public static class AgentRoutes
{
    public static IServiceCollection AddAgents(this IServiceCollection services, Settings settings)
    {
        services.AddSingleton(settings);
        services.AddKeyedSingleton<AIAgent>("demo", (serviceProvider, _) =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.AgentRegistration");
            logger.LogInformation("Registering demo agent instance using direct AI project model inference.");
            return DemoAgent.Create(settings);
        });

        services.AddKeyedSingleton<AIAgent>("foundry-demo", (serviceProvider, _) =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.FoundryAgentRegistration");
            logger.LogInformation("Registering Foundry demo agent instance.");
            return FoundryDemoAgent.CreateAsync(settings, logger).GetAwaiter().GetResult();
        });

        services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.FoundryMemoryAgentRegistration");
            logger.LogInformation("Registering Foundry memory agent with memory store and in-memory session cache.");
            logger.LogDebug("Session cache: userId-keyed, thread-safe, survives app lifetime (lost on restart). Memory store: persists in Azure beyond restarts.");
            return FoundryMemoryAgent.CreateAsync(settings, logger).GetAwaiter().GetResult();
        });

        return services;
    }

    public static WebApplication MapAgentRoutes(this WebApplication app)
    {
        app.MapPost("/agents/demo", async (
            [FromKeyedServices("demo")] AIAgent agent,
            IConversationSessionManager sessionManager,
            AgentRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.DemoAgentRoute");
            logger.LogInformation(
                "Received demo agent request. ConversationId={ConversationId}, MessageLength={MessageLength}",
                request.ConversationId,
                request.Message?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                logger.LogWarning("Demo agent request rejected due to empty message. ConversationId={ConversationId}", request.ConversationId);
                return Results.BadRequest("Message is required.");
            }

            var session = await sessionManager.GetOrCreateSessionAsync(
                request.ConversationId,
                async _ => await agent.CreateSessionAsync(),
                cancellationToken);

            var response = await RunWithConversationMemoryAsync(
                agent,
                session,
                request.Message,
                logger,
                cancellationToken);

            await sessionManager.AppendTurnAsync(
                session.ConversationId,
                request.Message,
                response.ToString(),
                cancellationToken);

            logger.LogInformation(
                "Demo agent response completed. ConversationId={ConversationId}, ResponseLength={ResponseLength}",
                session.ConversationId,
                response.ToString().Length);

            return Results.Ok(new AgentRunResult(session.ConversationId, response.ToString()));
        });

        app.MapPost("/agents/foundry-demo", async (
            [FromKeyedServices("foundry-demo")] AIAgent agent,
            IConversationSessionManager sessionManager,
            AgentRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.FoundryAgentRoute");
            logger.LogInformation(
                "Received Foundry agent request. ConversationId={ConversationId}, MessageLength={MessageLength}",
                request.ConversationId,
                request.Message?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                logger.LogWarning("Foundry agent request rejected due to empty message. ConversationId={ConversationId}", request.ConversationId);
                return Results.BadRequest("Message is required.");
            }

            var session = await sessionManager.GetOrCreateSessionAsync(
                request.ConversationId,
                async _ => await agent.CreateSessionAsync(),
                cancellationToken);

            var response = await RunWithConversationMemoryAsync(
                agent,
                session,
                request.Message,
                logger,
                cancellationToken);

            await sessionManager.AppendTurnAsync(
                session.ConversationId,
                request.Message,
                response.ToString(),
                cancellationToken);

            logger.LogInformation(
                "Foundry agent response completed. ConversationId={ConversationId}, ResponseLength={ResponseLength}",
                session.ConversationId,
                response.ToString().Length);

            return Results.Ok(new AgentRunResult(session.ConversationId, response.ToString()));
        });

        app.MapPost("/agents/foundryMemoryAgent", async (
            FoundryMemoryContext memoryContext,
            MemoryAgentRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.FoundryMemoryAgentRoute");
            logger.LogInformation(
                "Received Foundry memory agent request. UserId={UserId}, MessageLength={MessageLength}",
                request.UserId,
                request.Message?.Length ?? 0);
            logger.LogDebug("Request details: Message={Message}\n[Isolation: No PostgreSQL, uses Foundry memory store + session cache]", 
                request.Message);

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                logger.LogWarning("Foundry memory agent request rejected due to empty message. UserId={UserId}", request.UserId);
                return Results.BadRequest("Message is required.");
            }

            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                logger.LogWarning("Foundry memory agent request rejected due to missing userId.");
                return Results.BadRequest("UserId is required.");
            }

            logger.LogDebug("Validation passed. UserId={UserId}, proceeding to session cache lookup", request.UserId);

            var agent = memoryContext.Agent;
            logger.LogDebug("Agent obtained from FoundryMemoryContext. AgentName={AgentName}", agent.GetType().Name);

            var agentSession = await memoryContext.SessionCache.GetOrCreateSessionAsync(
                request.UserId,
                async () =>
                {
                    logger.LogDebug("Session factory invoked for UserId={UserId}, creating new AgentSession", request.UserId);
                    return await agent.CreateSessionAsync();
                });

            logger.LogDebug("Session ready for UserId={UserId}. Cache size={CacheSize} active users", 
                request.UserId, memoryContext.SessionCache.GetActiveCacheSize());

            logger.LogDebug("Running agent with message for UserId={UserId}", request.UserId);
            var memoryPrompt = await SearchFoundryMemoryAsync(
                memoryContext.MemoryClient,
                memoryContext.MemoryStoreName,
                memoryContext.OperationCache,
                request.UserId,
                request.Message,
                logger,
                cancellationToken);

            AgentResponse response;
            if (string.IsNullOrWhiteSpace(memoryPrompt))
            {
                response = await agent.RunAsync(request.Message, agentSession, cancellationToken: cancellationToken);
            }
            else
            {
                var messages = new[]
                {
                    new ChatMessage(ChatRole.System, memoryPrompt),
                    new ChatMessage(ChatRole.User, request.Message)
                };
                response = await agent.RunAsync(messages, agentSession, cancellationToken: cancellationToken);
            }
            logger.LogDebug("Agent execution completed. ResponseLength={ResponseLength}", response.ToString().Length);

            await UpdateFoundryMemoryAsync(
                memoryContext.MemoryClient,
                memoryContext.MemoryStoreName,
                memoryContext.OperationCache,
                request.UserId,
                request.Message,
                response.ToString(),
                logger,
                cancellationToken);

            logger.LogInformation(
                "Foundry memory agent response completed. UserId={UserId}, ResponseLength={ResponseLength}",
                request.UserId,
                response.ToString().Length);
            logger.LogDebug("User {UserId} now has persistent context in Foundry memory store. Next request will reuse session.", request.UserId);

            return Results.Ok(new MemoryAgentRunResult(request.UserId, response.ToString()));
        });

        app.MapGet("/conversations/{conversationId:guid}/history", async (
            Guid conversationId,
            IConversationSessionManager sessionManager,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.ConversationHistoryRoute");
            logger.LogInformation("Fetching conversation history. ConversationId={ConversationId}", conversationId);

            var history = await sessionManager.GetHistoryAsync(conversationId, cancellationToken);

            logger.LogInformation(
                "Conversation history returned. ConversationId={ConversationId}, MessageCount={MessageCount}",
                conversationId,
                history.Count);

            return Results.Ok(new ConversationHistoryResult(conversationId, history));
        });

        return app;
    }

    private static Task<AgentResponse> RunWithConversationMemoryAsync(
        AIAgent agent,
        ConversationSessionContext session,
        string message,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var agentSession = (AgentSession)session.Session;

        if (!session.RequiresHistoryReplay)
        {
            return agent.RunAsync(
                message,
                agentSession,
                cancellationToken: cancellationToken);
        }

        logger.LogInformation(
            "Rehydrating session from persisted conversation history. ConversationId={ConversationId}, HistoryCount={HistoryCount}",
            session.ConversationId,
            session.History.Count);

        var messages = session.History
            .Select(ToChatMessage)
            .Append(new ChatMessage(ChatRole.User, message));

        return agent.RunAsync(
            messages,
            agentSession,
            cancellationToken: cancellationToken);
    }

#pragma warning disable AAIP001
    private static async Task<string?> SearchFoundryMemoryAsync(
        Azure.AI.Projects.Memory.AIProjectMemoryStores memoryClient,
        string memoryStoreName,
        FoundryMemoryOperationCache operationCache,
        string? userId,
        string message,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var searchResponse = await FoundryMemoryAgent.SearchMemoriesAsync(
            memoryClient,
            memoryStoreName,
            userId,
            message,
            operationCache.GetPreviousSearchId(userId),
            cancellationToken);

        operationCache.RememberSearchId(userId, searchResponse.SearchId);

        var memories = searchResponse.Memories
            .Select(memory => memory.MemoryItem?.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .Cast<string>()
            .ToArray();

        if (memories.Length == 0)
        {
            logger.LogDebug("No persisted Foundry memories matched for scope={Scope}", userId);
            return null;
        }

        logger.LogDebug("Retrieved {MemoryCount} persisted Foundry memories for scope={Scope}", memories.Length, userId);

        return "Use the following persisted user memory only when it is relevant to the current request:\n" +
               string.Join("\n", memories.Select(memory => $"- {memory}"));
    }

    private static async Task UpdateFoundryMemoryAsync(
        Azure.AI.Projects.Memory.AIProjectMemoryStores memoryClient,
        string memoryStoreName,
        FoundryMemoryOperationCache operationCache,
        string? userId,
        string userMessage,
        string assistantResponse,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var updateResponse = await FoundryMemoryAgent.UpdateMemoriesAsync(
            memoryClient,
            memoryStoreName,
            userId,
            userMessage,
            assistantResponse,
            operationCache.GetPreviousUpdateId(userId),
            cancellationToken);

        operationCache.RememberUpdateId(userId, updateResponse.UpdateId);
        logger.LogDebug(
            "Queued persisted Foundry memory update. Scope={Scope}, UpdateId={UpdateId}, Status={Status}, SupersededBy={SupersededBy}",
            userId,
            updateResponse.UpdateId,
            updateResponse.Status,
            updateResponse.SupersededBy);
    }
#pragma warning restore AAIP001

    private static ChatMessage ToChatMessage(ConversationMessage message)
    {
        var role = message.Role switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

        return new ChatMessage(role, message.Content);
    }
}

public record AgentRequest(string Message, Guid? ConversationId);

public record AgentRunResult(Guid ConversationId, string Response);

public record MemoryAgentRequest(string Message, string UserId);

public record MemoryAgentRunResult(string UserId, string Response);

public record ConversationHistoryResult(Guid ConversationId, IReadOnlyList<ConversationMessage> Messages);
