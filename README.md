# Agent Hub

A demo app for hosting AI agents via minimal API using the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/).

## Agents

| Agent | Endpoint | Type | Description |
|-------|----------|------|-------------|
| DemoAgent | `POST /agents/demo` | Responses (code-first) | Model, instructions, and tools defined at runtime via `AIProjectClient.AsAIAgent()`. Always available. |
| FoundryDemoAgent | `POST /agents/foundry-demo` | Foundry Agent (versioned) | Server-managed agent via `AgentAdministrationClient`. Automatically created on startup if it doesn't exist. |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An [Azure AI Foundry](https://ai.azure.com/) project with a deployed model
- Azure credentials configured for `DefaultAzureCredential` (e.g. `az login`)

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `AZURE_AI_PROJECT_ENDPOINT` | Yes | — | Azure AI Foundry project endpoint |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` | No | `gpt-4o-mini` | Model deployment name for the DemoAgent |
| `AZURE_AI_FOUNDRY_AGENT_NAME` | No | `DemoAgent` | Name of the Foundry-managed agent. Auto-created if it doesn't exist. |

## Getting Started

```bash
# Set required environment variable
export AZURE_AI_PROJECT_ENDPOINT="https://<resource>.services.ai.azure.com/api/projects/<project>"

# Optional: set model and foundry agent name
export AZURE_AI_MODEL_DEPLOYMENT_NAME="gpt-4o-mini"
export AZURE_AI_FOUNDRY_AGENT_NAME="MyFoundryAgent"

# Run the API
cd src/AgentHub.API
dotnet run
```

The API starts at `http://localhost:5023`. Swagger UI is available at the root (`/`).

## Usage

```bash
# Health check
curl http://localhost:5023/health

# Chat with the demo agent
curl -X POST http://localhost:5023/agents/demo \
  -H "Content-Type: application/json" \
  -d '{"message": "What is the largest city in France?"}'

# Chat with the foundry agent (auto-created on startup)
curl -X POST http://localhost:5023/agents/foundry-demo \
  -H "Content-Type: application/json" \
  -d '{"message": "What is the largest city in France?"}'
```

## Project Structure

```
src/AgentHub.API/
├── Program.cs              # App startup and wiring
├── Settings.cs             # Centralized environment variable loading
├── agents/
│   ├── DemoAgent.cs        # Code-first agent factory
│   └── FoundryDemoAgent.cs # Foundry versioned agent factory
├── routes/
│   └── AgentRoutes.cs      # DI registration and route definitions
└── AgentHub.API.http       # HTTP test requests
```