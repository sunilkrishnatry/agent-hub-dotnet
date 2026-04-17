using AgentHub.API.Agents;
using AgentHub.Persistence;
using AgentHub.SessionState;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

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

public record ConversationHistoryResult(Guid ConversationId, IReadOnlyList<ConversationMessage> Messages);
