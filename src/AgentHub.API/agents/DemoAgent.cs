using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

namespace AgentHub.API.Agents;

public static class DemoAgent
{
    public static AIAgent Create(Settings settings)
    {
        return new AIProjectClient(settings.AzureAIProjectEndpoint, new DefaultAzureCredential())
            .AsAIAgent(
                model: settings.AzureAIModelDeploymentName,
                instructions: "You are a friendly assistant. Keep your answers brief.",
                name: "DemoAgent");
    }
}
