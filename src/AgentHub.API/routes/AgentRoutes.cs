using AgentHub.API.Agents;
using Microsoft.Agents.AI;

namespace AgentHub.API.Routes;

public static class AgentRoutes
{
    public static IServiceCollection AddAgents(this IServiceCollection services, Settings settings)
    {
        services.AddSingleton(settings);
        services.AddKeyedSingleton<AIAgent>("demo", (_, _) => DemoAgent.Create(settings));
        services.AddKeyedSingleton<AIAgent>("foundry-demo", (_, _) =>
            FoundryDemoAgent.CreateAsync(settings).GetAwaiter().GetResult());

        return services;
    }

    public static WebApplication MapAgentRoutes(this WebApplication app)
    {
        app.MapPost("/agents/demo", async ([FromKeyedServices("demo")] AIAgent agent, AgentRequest request) =>
        {
            var response = await agent.RunAsync(request.Message);
            return Results.Ok(new { response });
        });

        app.MapPost("/agents/foundry-demo", async ([FromKeyedServices("foundry-demo")] AIAgent agent, AgentRequest request) =>
        {
            var response = await agent.RunAsync(request.Message);
            return Results.Ok(new { response });
        });

        return app;
    }
}

public record AgentRequest(string Message);
