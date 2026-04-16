using Microsoft.Extensions.Configuration;

namespace AgentHub.API;

public class Settings
{
    public required Uri AzureAIProjectEndpoint { get; init; }
    public string AzureAIModelDeploymentName { get; init; } = "gpt-4o-mini";
    public string? FoundryAgentName { get; init; }

    public static Settings Load(IConfiguration configuration)
    {
        var endpoint = configuration["AZURE_AI_PROJECT_ENDPOINT"]
            ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");

        var modelDeploymentName = configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"]
            ?? "gpt-4o-mini";

        var foundryAgentName = configuration["AZURE_AI_FOUNDRY_AGENT_NAME"];

        return new Settings
        {
            AzureAIProjectEndpoint = new Uri(endpoint),
            AzureAIModelDeploymentName = modelDeploymentName,
            FoundryAgentName = foundryAgentName
        };
    }
}
