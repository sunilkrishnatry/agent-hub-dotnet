using AgentHub.API.Agents;
using Microsoft.Agents.AI;

namespace AgentHub.API.Routes;

public static class AgentRoutes
{
    public static IServiceCollection AddAgents(this IServiceCollection services, Settings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(DemoAgent.Create(settings));

        return services;
    }

    public static WebApplication MapAgentRoutes(this WebApplication app)
    {
        app.MapPost("/agents/demo", async (AgentRequest request, AIAgent agent) =>
        {
            var response = await agent.RunAsync(request.Message);
            return Results.Ok(new { response });
        });

        return app;
    }
}

public record AgentRequest(string Message);
