using System.ClientModel;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace AgentHub.API.Agents;

public static class FoundryDemoAgent
{
    public const string DefaultName = "DemoAgent";

#pragma warning disable OPENAI001 // FoundryAgent is experimental
    public static async Task<FoundryAgent> CreateAsync(Settings settings)
    {
        var client = new AIProjectClient(settings.AzureAIProjectEndpoint, new DefaultAzureCredential());
        var agentName = settings.FoundryAgentName ?? DefaultName;

        var record = await GetOrCreateAgentAsync(client, agentName, settings.AzureAIModelDeploymentName);
        return client.AsAIAgent(record);
    }
#pragma warning restore OPENAI001

    private static async Task<ProjectsAgentRecord> GetOrCreateAgentAsync(
        AIProjectClient client, string agentName, string model)
    {
        try
        {
            return await client.AgentAdministrationClient.GetAgentAsync(agentName);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            var definition = new DeclarativeAgentDefinition(model: model)
            {
                Instructions = "You are a friendly assistant. Keep your answers brief."
            };
            var options = new ProjectsAgentVersionCreationOptions(definition);
            await client.AgentAdministrationClient.CreateAgentVersionAsync(agentName, options);
            return await client.AgentAdministrationClient.GetAgentAsync(agentName);
        }
    }
}
