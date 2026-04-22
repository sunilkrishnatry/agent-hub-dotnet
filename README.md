# Agent Hub

Agent Hub is a .NET 10 minimal API for hosting AI agents using Microsoft Agent Framework and Azure AI Foundry. It includes:

- A code-first agent built directly from an Azure AI Foundry model deployment
- A Foundry-managed agent created and resolved through the Foundry project
- A Foundry memory-backed agent that provisions a Foundry memory store
- Conversation memory using `ConversationId` for demo and foundry-demo endpoints
- PostgreSQL-backed conversation history persistence
- Restart-safe conversation rehydration from stored history

## What This Project Does

The API exposes three agent routes with two memory models.

- `POST /agents/demo` and `POST /agents/foundry-demo` accept `message` plus optional `conversationId`
- These two routes persist turns in PostgreSQL and can replay history after restart
- `POST /agents/foundryMemoryAgent` accepts `message` plus `userId`
- This route uses a Foundry memory store and relies on Foundry-managed memory behaviors

## Agents

| Agent | Endpoint | Type | Description |
|-------|----------|------|-------------|
| DemoAgent | `POST /agents/demo` | Code-first agent | Uses `AIProjectClient.AsAIAgent()` with runtime-defined model and instructions |
| FoundryDemoAgent | `POST /agents/foundry-demo` | Foundry-managed agent | Uses `AgentAdministrationClient` and creates the configured Foundry agent if it does not already exist |
| FoundryMemoryAgent | `POST /agents/foundryMemoryAgent` | Foundry-managed memory agent | Uses a Foundry memory store and a dedicated Foundry agent (`<FoundryAgentName>-memory` by default) |

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/agents/demo` | Sends a message to the code-first agent |
| `POST` | `/agents/foundry-demo` | Sends a message to the Foundry-managed agent |
| `POST` | `/agents/foundryMemoryAgent` | Sends a message to the Foundry memory-backed agent (`message`, `userId`) |
| `GET` | `/conversations/{conversationId}/history` | Returns persisted message history for a conversation |
| `GET` | `/health` | Health check |
| `GET` | `/swagger` | Swagger UI |

## Architecture

The solution is split into focused projects.

| Project | Purpose |
|--------|---------|
| `src/AgentHub.API` | ASP.NET Core minimal API, route handlers, agent registration, configuration loading |
| `src/AgentHub.Persistence` | PostgreSQL conversation history storage |
| `src/AgentHub.SessionState` | In-memory session tracking and history-based session rehydration |

## Memory Model

The project uses two different memory paths.

### Path A: `demo` and `foundry-demo`

Conversation memory is keyed by `ConversationId`.

1. Client sends a message without a `ConversationId`
2. API creates a new conversation and returns the generated `ConversationId`
3. Client sends later messages with the same `ConversationId`
4. The session manager reuses the existing in-memory session when possible
5. Every user and assistant turn is also written to PostgreSQL
6. If the app restarts, the next request with the same `ConversationId` reloads the stored history and replays it before generating the next response

This means the conversation can survive process restarts as long as PostgreSQL history is available.

### Path B: `foundryMemoryAgent`

1. On startup, the API resolves or creates a Foundry memory store (persists in Azure)
2. On startup, the API resolves or creates a dedicated Foundry memory agent
3. On startup, an in-memory session cache is initialized (keyed by `userId`, thread-safe)
4. Requests include `message` and `userId`
5. The route checks the session cache for the `userId`:
   - **Cache hit** — reuses the existing `AgentSession`, continuing the conversation thread
   - **Cache miss** — creates a new `AgentSession`, caches it by `userId` for future requests
6. Foundry's memory store reads and writes user context scoped to the `userId`

**Two layers of persistence:**

| Layer | Scope | Survives app restart? | Backed by |
|-------|-------|-----------------------|-----------|
| Session cache | In-memory per `userId` | No | RAM (thread-safe `ConcurrentDictionary`) |
| Foundry memory store | Long-term per `userId` | **Yes** | Azure AI Foundry |

When the app restarts, the session cache is cleared. However, Foundry's memory store retains long-term context (user profile, chat summaries) from all previous sessions and makes it available to the agent on the next request.

This path does not use the PostgreSQL conversation pipeline.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure AI Foundry project with a deployed model
- Azure sign-in available to `DefaultAzureCredential` such as `az login`
- A PostgreSQL server reachable from the API

## Configuration

Configuration is loaded in this order:

1. `appsettings.json`
2. `appsettings.Development.json`
3. Environment variables

The application uses the `AgentHub` configuration section.

### Required Settings

| Setting | Required | Description |
|--------|----------|-------------|
| `AgentHub:AzureAIProjectEndpoint` | Yes | Azure AI Foundry project endpoint |
| `AgentHub:AzureAIModelDeploymentName` | Yes | Model deployment name in the Foundry project |
| `AgentHub:FoundryAgentName` | No | Name of the Foundry-managed agent; defaults to `DemoAgent` when omitted |
| `AgentHub:MemoryStoreName` | No | Foundry memory store name for `foundryMemoryAgent`; defaults to `agent-hub-memory` |
| `AgentHub:MemoryEmbeddingModel` | No | Embedding deployment/model for Foundry memory store; defaults to `text-embedding-3-small` |

### PostgreSQL Settings

Use either a full connection string or individual connection properties.

Option 1: full connection string

| Setting | Required | Description |
|--------|----------|-------------|
| `AgentHub:Postgres:ConnectionString` | Yes | Full PostgreSQL connection string |

Option 2: individual properties

| Setting | Required | Description |
|--------|----------|-------------|
| `AgentHub:Postgres:Host` | Yes | PostgreSQL server host |
| `AgentHub:Postgres:Port` | No | PostgreSQL server port, default `5432` |
| `AgentHub:Postgres:Database` | Yes | Database name |
| `AgentHub:Postgres:Username` | Yes | Database username |
| `AgentHub:Postgres:Password` | Yes | Database password |
| `AgentHub:Postgres:SslMode` | No | PostgreSQL SSL mode, default `Prefer` |

Environment variable fallbacks are also supported:

- `AZURE_AI_PROJECT_ENDPOINT`
- `AZURE_AI_MODEL_DEPLOYMENT_NAME`
- `AZURE_AI_FOUNDRY_AGENT_NAME`
- `AZURE_AI_MEMORY_STORE_NAME`
- `AZURE_AI_MEMORY_EMBEDDING_MODEL`
- `POSTGRES_CONNECTION_STRING`
- `POSTGRES_URL`
- `POSTGRES_HOST`
- `POSTGRES_PORT`
- `POSTGRES_DATABASE`
- `POSTGRES_USERNAME`
- `POSTGRES_PASSWORD`
- `POSTGRES_SSL_MODE`

## Example Configuration

Use placeholder values similar to the following in `src/AgentHub.API/appsettings.Development.json`.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "AgentHub": "Debug"
    }
  },
  "AgentHub": {
    "AzureAIProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureAIModelDeploymentName": "gpt-4o-mini",
    "FoundryAgentName": "foundry-demo-agent",
    "MemoryStoreName": "agent-hub-memory",
    "MemoryEmbeddingModel": "text-embedding-3-small",
    "Postgres": {
      "Host": "<server>.postgres.database.azure.com",
      "Port": "5432",
      "Database": "<database>",
      "Username": "<username>",
      "Password": "<password>",
      "SslMode": "Prefer"
    }
  }
}
```

## Restore and Build

From the repository root:

```powershell
dotnet restore AgentHub.slnx
dotnet build AgentHub.slnx
```

## Run the API

From the repository root:

```powershell
dotnet run --project src/AgentHub.API/AgentHub.API.csproj --launch-profile http
```

The default local URLs are defined in `src/AgentHub.API/Properties/launchSettings.json`.

- `http://localhost:5023`
- `https://localhost:7132`

Swagger UI is available at:

- `http://localhost:5023/swagger`

## Hot Reload

For local development, use:

```powershell
dotnet watch --project src/AgentHub.API/AgentHub.API.csproj
```

Hot reload can apply many code changes automatically, but changes to startup wiring, DI registrations, route shape, or some constructor signatures may still require a restart.

## Example Requests

### Start a New Conversation

```powershell
curl -X POST http://localhost:5023/agents/foundry-demo `
  -H "Content-Type: application/json" `
  -d '{"message":"Hello, introduce yourself."}'
```

Example response:

```json
{
  "conversationId": "7f4c0cf7-f6ab-4c32-9d82-7c61d9f25a8a",
  "response": "Hello, I am your assistant..."
}
```

### Continue an Existing Conversation

```powershell
curl -X POST http://localhost:5023/agents/foundry-demo `
  -H "Content-Type: application/json" `
  -d '{"message":"What did I just ask you?","conversationId":"7f4c0cf7-f6ab-4c32-9d82-7c61d9f25a8a"}'
```

### Call the Foundry Memory Agent

```powershell
curl -X POST http://localhost:5023/agents/foundryMemoryAgent `
  -H "Content-Type: application/json" `
  -d '{"message":"My favorite color is teal.","userId":"user-123"}'
```

### Fetch Conversation History

```powershell
curl http://localhost:5023/conversations/7f4c0cf7-f6ab-4c32-9d82-7c61d9f25a8a/history
```

### Health Check

```powershell
curl http://localhost:5023/health
```

## PostgreSQL Behavior

The persistence project automatically creates the required tables on first use.

Tables created:

- `conversations`
- `conversation_messages`

Each message is stored with:

- conversation id
- role
- content
- timestamp

## Logging

The API logs:

- startup configuration details
- request start and completion
- memory store creation and resolution flow
- agent creation flow
- session reuse and session rehydration
- PostgreSQL initialization and persistence errors

For development, set `AgentHub` logging to `Debug` in `appsettings.Development.json`.

## Project Structure

```text
AgentHub.slnx
src/
  AgentHub.API/
    Program.cs
    Settings.cs
    appsettings.json
    appsettings.Development.json
    agents/
      DemoAgent.cs
      FoundryDemoAgent.cs
      FoundryMemoryAgent.cs
    routes/
      AgentRoutes.cs
  AgentHub.Persistence/
    ConversationMessage.cs
    IConversationHistoryRepository.cs
    PostgresConversationHistoryRepository.cs
    PostgresConversationOptions.cs
  AgentHub.SessionState/
    ConversationSessionContext.cs
    ConversationSessionManager.cs
    IConversationSessionManager.cs
```

## Notes

- The Foundry agent name and model deployment name must match valid resources in your Foundry project
- PostgreSQL connection values containing special characters are handled through `NpgsqlConnectionStringBuilder`
- A colleague in another location cannot use `localhost`; expose the app through a tunnel or deploy it to a reachable host