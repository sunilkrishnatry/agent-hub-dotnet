using Microsoft.Extensions.Configuration;

namespace AgentHub.API;

public class Settings
{
    public required Uri AzureAIProjectEndpoint { get; init; }
    public string AzureAIModelDeploymentName { get; init; } = "gpt-4o-mini";

    public static Settings Load(IConfiguration configuration)
    {
        var endpoint = configuration["AZURE_AI_PROJECT_ENDPOINT"]
            ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");

        var modelDeploymentName = configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"]
            ?? "gpt-4o-mini";

        return new Settings
        {
            AzureAIProjectEndpoint = new Uri(endpoint),
            AzureAIModelDeploymentName = modelDeploymentName
        };
    }
}
