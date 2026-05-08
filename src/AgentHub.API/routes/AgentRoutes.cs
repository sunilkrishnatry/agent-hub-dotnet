using System.Text.RegularExpressions;
using AgentHub.API.Agents;
using AgentHub.Persistence;
using AgentHub.SessionState;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentHub.API.Routes;

public static partial class AgentRoutes
{
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._%+@\-]{0,127}$", RegexOptions.Compiled)]
    internal static partial Regex UserIdPattern();

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

        services.AddSingleton(serviceProvider =>
        {
            var memoryContext = serviceProvider.GetRequiredService<FoundryMemoryContext>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.MemoryAuditService");
            return new MemoryAuditService(memoryContext, logger);
        });

        services.AddSingleton<IMemoryAuditRepository>(serviceProvider =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<PostgresConversationOptions>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<PostgresMemoryAuditRepository>();
            return new PostgresMemoryAuditRepository(postgresOptions, logger);
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
            HttpContext httpContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.FoundryMemoryAgentRoute");

            var validationResult = ValidateFoundryMemoryRequest(request, logger);
            if (validationResult is not null)
            {
                return validationResult;
            }

            // Scope long-term memory to this user; Foundry reads this header for user-scoped memory retrieval
            httpContext.Request.Headers["x-memory-user-id"] = request.UserId;

            logger.LogInformation(
                "Received Foundry memory agent request. UserId={UserId}, ConversationId={ConversationId}, MessageLength={MessageLength}",
                request.UserId, request.ConversationId, request.Message.Length);

            // Resolve or create the Foundry session (conversation thread).
            // ConversationId tracks short-term memory; UserId (via header) tracks long-term memory.
            Guid conversationId;
            AgentSession agentSession;

            if (request.ConversationId.HasValue
                && memoryContext.SessionStore.TryGet(request.ConversationId.Value, out var existingSession))
            {
                agentSession = existingSession!;
                conversationId = request.ConversationId.Value;
                logger.LogDebug("Resuming existing session. UserId={UserId}, ConversationId={ConversationId}", request.UserId, conversationId);
            }
            else
            {
                agentSession = await memoryContext.Agent.CreateSessionAsync();
                conversationId = Guid.NewGuid();
                memoryContext.SessionStore.Set(conversationId, agentSession);
                logger.LogDebug("Created new session. UserId={UserId}, ConversationId={ConversationId}", request.UserId, conversationId);
            }

            // Foundry handles memory retrieval and persistence natively via the attached memory store.
            var response = await memoryContext.Agent.RunAsync(request.Message, agentSession, cancellationToken: cancellationToken);
            var responseText = response.ToString();

            logger.LogInformation(
                "Foundry memory agent response completed. UserId={UserId}, ConversationId={ConversationId}, ResponseLength={ResponseLength}",
                request.UserId, conversationId, responseText.Length);

            return Results.Ok(new MemoryAgentRunResult(request.UserId, conversationId, responseText));
        });

        app.MapGet("/users/{userId}/memory", async (
            string userId,
            MemoryAuditService auditService,
            string? topic,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.MemoryAuditRoute");

            if (!UserIdPattern().IsMatch(userId))
            {
                logger.LogWarning("Invalid userId format in memory inspect request. UserId={UserId}", userId);
                return Results.BadRequest("Invalid userId format.");
            }

            var result = await auditService.InspectAsync(userId, topic, cancellationToken);
            return Results.Ok(result);
        });

        app.MapDelete("/users/{userId}/memory", async (
            string userId,
            MemoryAuditService auditService,
            IMemoryAuditRepository auditRepository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.MemoryDeleteRoute");

            if (!UserIdPattern().IsMatch(userId))
            {
                logger.LogWarning("Invalid userId format in memory delete request. UserId={UserId}", userId);
                return Results.BadRequest("Invalid userId format.");
            }

            try
            {
                var result = await auditService.DeleteAsync(userId, cancellationToken);
                
                // Check if deletion was actually successful
                if (!result.FoundryScopeDeleted)
                {
                    var errorMsg = $"Failed to delete memory scope for user {userId}. User may not exist or scope not found.";
                    logger.LogWarning("Memory deletion failed. UserId={UserId}, FoundryDeleted={FoundryDeleted}", 
                        userId, result.FoundryScopeDeleted);
                    
                    // Log failed deletion to audit trail for compliance
                    var auditMessage = $"Attempted memory deletion for non-existent user or empty scope";
                    await auditRepository.LogMemoryDeletionAsync(
                        userId,
                        "foundry-memory",
                        auditMessage,
                        wasSuccessful: false,
                        errorMessage: errorMsg,
                        cancellationToken);

                    return Results.BadRequest(new { error = errorMsg, result });
                }

                // Log successful deletion to audit trail
                var successMessage = $"Memory scope successfully deleted";
                await auditRepository.LogMemoryDeletionAsync(
                    userId,
                    "foundry-memory",
                    successMessage,
                    wasSuccessful: true,
                    errorMessage: null,
                    cancellationToken);

                logger.LogInformation(
                    "Memory deletion completed and audited. UserId={UserId}, FoundryDeleted={FoundryDeleted}",
                    userId, result.FoundryScopeDeleted);

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Memory deletion failed with exception. UserId={UserId}", userId);
                
                // Log failed deletion to audit trail for compliance
                var errorAuditMessage = $"Memory deletion error: {ex.GetType().Name}";
                await auditRepository.LogMemoryDeletionAsync(
                    userId,
                    "foundry-memory",
                    errorAuditMessage,
                    wasSuccessful: false,
                    errorMessage: ex.Message,
                    cancellationToken);

                throw;
            }
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

    /// <summary>
    /// Validates the incoming Foundry memory agent request payload.
    /// Returns a BadRequest result when validation fails; otherwise returns null.
    /// </summary>
    /// <param name="request">The incoming memory agent request to validate.</param>
    /// <param name="logger">Logger used to emit validation failure details.</param>
    /// <returns>
    /// An <see cref="IResult"/> representing a validation error response when invalid; otherwise null.
    /// </returns>
#pragma warning disable AAIP001
    private static IResult? ValidateFoundryMemoryRequest(MemoryAgentRequest request, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            logger.LogWarning("Foundry memory agent request rejected due to empty message. UserId={UserId}", request.UserId);
            return Results.BadRequest("Message is required.");
        }

        if (request.Message.Length > 4000)
        {
            logger.LogWarning("Foundry memory agent request rejected. Message too long: {Length} chars. UserId={UserId}", request.Message.Length, request.UserId);
            return Results.BadRequest("Message must not exceed 4000 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            logger.LogWarning("Foundry memory agent request rejected due to missing userId.");
            return Results.BadRequest("UserId is required.");
        }

        if (request.UserId.Length > 128 || !UserIdPattern().IsMatch(request.UserId))
        {
            logger.LogWarning("Foundry memory agent request rejected due to invalid userId format.");
            return Results.BadRequest("UserId must be alphanumeric (dots, hyphens, underscores allowed), max 128 characters.");
        }

        return null;
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

public record MemoryAgentRequest(string Message, string UserId, Guid? ConversationId = null);

public record MemoryAgentRunResult(string UserId, Guid ConversationId, string Response);

public record ConversationHistoryResult(Guid ConversationId, IReadOnlyList<ConversationMessage> Messages);
