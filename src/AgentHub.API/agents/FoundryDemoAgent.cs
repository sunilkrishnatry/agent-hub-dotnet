using System.ClientModel;
using System.Text.RegularExpressions;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Agents.AI.Foundry;

namespace AgentHub.API.Agents;

public static class FoundryDemoAgent
{
    public const string DefaultName = "DemoAgent";
    private static readonly Regex AgentNamePattern = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

#pragma warning disable OPENAI001 // FoundryAgent is experimental
    public static async Task<FoundryAgent> CreateAsync(Settings settings, ILogger logger)
    {
        logger.LogInformation(
            "Initializing Foundry agent. Endpoint={Endpoint}, ModelDeployment={ModelDeployment}, ConfiguredAgentName={ConfiguredAgentName}",
            settings.AzureAIProjectEndpoint,
            settings.AzureAIModelDeploymentName,
            settings.FoundryAgentName ?? DefaultName);

        var client = new AIProjectClient(settings.AzureAIProjectEndpoint, new DefaultAzureCredential());
        var agentName = settings.FoundryAgentName ?? DefaultName;

        ValidateAgentName(agentName);
        ValidateModelDeploymentName(settings.AzureAIModelDeploymentName);

        var record = await GetOrCreateAgentAsync(client, agentName, settings.AzureAIModelDeploymentName, logger);
        if (!string.Equals(record.Name, agentName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Foundry resolved agent name differs from configured value. ConfiguredAgentName={ConfiguredAgentName}, ResolvedAgentName={ResolvedAgentName}",
                agentName,
                record.Name);
        }

        logger.LogInformation("Foundry agent is ready. AgentName={AgentName}", record.Name);
        return client.AsAIAgent(record);
    }
#pragma warning restore OPENAI001

    private static async Task<ProjectsAgentRecord> GetOrCreateAgentAsync(
        AIProjectClient client, string agentName, string model, ILogger logger)
    {
        try
        {
            logger.LogInformation("Attempting to resolve existing Foundry agent. AgentName={AgentName}", agentName);
            return await client.AgentAdministrationClient.GetAgentAsync(agentName);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            logger.LogInformation("Foundry agent does not exist. Creating agent version. AgentName={AgentName}, ModelDeployment={ModelDeployment}", agentName, model);
            var definition = new DeclarativeAgentDefinition(model: model)
            {
                Instructions = "You are a friendly assistant. Keep your answers brief."
            };
            var options = new ProjectsAgentVersionCreationOptions(definition);

            try
            {
                await client.AgentAdministrationClient.CreateAgentVersionAsync(agentName, options);
            }
            catch (ClientResultException createEx)
            {
                logger.LogError(
                    createEx,
                    "Failed to create Foundry agent version. AgentName={AgentName}, ModelDeployment={ModelDeployment}, Status={Status}. " +
                    "Likely causes: invalid AgentHub:FoundryAgentName format, invalid AgentHub:AzureAIModelDeploymentName, or missing Foundry permissions.",
                    agentName,
                    model,
                    createEx.Status);
                throw;
            }

            logger.LogInformation("Foundry agent version created. Fetching agent details. AgentName={AgentName}", agentName);
            return await client.AgentAdministrationClient.GetAgentAsync(agentName);
        }
    }

    private static void ValidateAgentName(string agentName)
    {
        if (!AgentNamePattern.IsMatch(agentName))
        {
            throw new InvalidOperationException(
                "AgentHub:FoundryAgentName is invalid. Use only letters, numbers, dots, underscores, and hyphens (1-64 chars). " +
                "Example: demo-agent.");
        }
    }

    private static void ValidateModelDeploymentName(string modelDeploymentName)
    {
        if (string.IsNullOrWhiteSpace(modelDeploymentName))
        {
            throw new InvalidOperationException(
                "AgentHub:AzureAIModelDeploymentName is required and must match a deployed model name in your Foundry project.");
        }
    }
}
