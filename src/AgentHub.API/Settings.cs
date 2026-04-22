using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AgentHub.API;

public class Settings
{
    public required Uri AzureAIProjectEndpoint { get; init; }
    public string AzureAIModelDeploymentName { get; init; } = "gpt-4o-mini";
    public string? FoundryAgentName { get; init; }
    public string MemoryStoreName { get; init; } = "agent-hub-memory";
    public string MemoryEmbeddingModel { get; init; } = "text-embedding-3-small";
    public required string PostgresConnectionString { get; init; }

    public static Settings Load(IConfiguration configuration)
    {
        var agentHubSection = configuration.GetSection("AgentHub");

        var endpoint = agentHubSection["AzureAIProjectEndpoint"]
            ?? configuration["AZURE_AI_PROJECT_ENDPOINT"]
            ?? throw new InvalidOperationException(
                "Azure AI project endpoint is not configured. Set AgentHub:AzureAIProjectEndpoint or AZURE_AI_PROJECT_ENDPOINT.");

        var modelDeploymentName = agentHubSection["AzureAIModelDeploymentName"]
            ?? configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"]
            ?? "gpt-4o-mini";

        var foundryAgentName = agentHubSection["FoundryAgentName"]
            ?? configuration["AZURE_AI_FOUNDRY_AGENT_NAME"];

        var memoryStoreName = agentHubSection["MemoryStoreName"]
            ?? configuration["AZURE_AI_MEMORY_STORE_NAME"]
            ?? "agent-hub-memory";

        var memoryEmbeddingModel = agentHubSection["MemoryEmbeddingModel"]
            ?? configuration["AZURE_AI_MEMORY_EMBEDDING_MODEL"]
            ?? "text-embedding-3-small";

        var postgresConnectionString = LoadPostgresConnectionString(configuration);

        return new Settings
        {
            AzureAIProjectEndpoint = new Uri(endpoint),
            AzureAIModelDeploymentName = modelDeploymentName,
            FoundryAgentName = foundryAgentName,
            MemoryStoreName = memoryStoreName,
            MemoryEmbeddingModel = memoryEmbeddingModel,
            PostgresConnectionString = postgresConnectionString
        };
    }

    private static string LoadPostgresConnectionString(IConfiguration configuration)
    {
        var postgresSection = configuration.GetSection("AgentHub:Postgres");

        var explicitConnectionString = postgresSection["ConnectionString"]
            ?? configuration["POSTGRES_CONNECTION_STRING"]
            ?? configuration["POSTGRES_URL"];

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var host = postgresSection["Host"] ?? configuration["POSTGRES_HOST"];
        var database = postgresSection["Database"] ?? configuration["POSTGRES_DATABASE"];
        var username = postgresSection["Username"] ?? configuration["POSTGRES_USERNAME"];
        var password = postgresSection["Password"] ?? configuration["POSTGRES_PASSWORD"];
        var port = postgresSection["Port"] ?? configuration["POSTGRES_PORT"] ?? "5432";
        var sslMode = postgresSection["SslMode"] ?? configuration["POSTGRES_SSL_MODE"] ?? "Prefer";

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection is not configured. Set AgentHub:Postgres:ConnectionString, POSTGRES_CONNECTION_STRING, or POSTGRES_URL, " +
                "or provide AgentHub:Postgres:Host/Database/Username/Password (or the matching POSTGRES_* variables).");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var parsedPort) ? parsedPort : 5432,
            Database = database,
            Username = username,
            Password = password,
            SslMode = Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var parsedSsl) ? parsedSsl : SslMode.Prefer
        };

        return builder.ConnectionString;
    }
}
